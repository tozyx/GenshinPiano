using System.Buffers;
using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GenshinPiano.App.Services;

// Decode each instrument once, then mix lightweight independent voices into
// one output stream. Dense passages no longer create an MP3 decoder per note.
public sealed class WindowsSampleAuditionOutput : ISampleAuditionOutput, IDisposable
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private readonly string _sampleRoot;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private readonly PcmVoiceMixer _mixer = new();
    private readonly Timer _idleTimer;
    private readonly Dictionary<int, Dictionary<string, float[]>> _decodedCache = [];
    private readonly LinkedList<int> _cacheOrder = [];
    private readonly MMDeviceEnumerator? _deviceEnumerator;
    private readonly EndpointNotifications _endpointNotifications;
    private Dictionary<string, float[]> _samples = [];
    private WaveOutEvent? _output;
    private bool _outputReady;
    private bool _endpointChanged;
    private string? _outputEndpointId;
    private int _preparedInstrument = int.MinValue;
    private float _masterVolume = 1;
    private long _lastAttackTimestamp;
    private bool _disposed;

    public event Action? OutputDeviceChanged;
    public int LastPreparedInstrument
    {
        get { lock (_sync) return _preparedInstrument; }
    }

    public WindowsSampleAuditionOutput(string sampleRoot)
    {
        _sampleRoot = sampleRoot;
        _idleTimer = new Timer(_ => ReleaseWhenIdle(), null,
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        _endpointNotifications = new EndpointNotifications(() =>
        {
            lock (_sync) _endpointChanged = true;
            OutputDeviceChanged?.Invoke();
        });
        try
        {
            var enumerator = new MMDeviceEnumerator();
            try { enumerator.RegisterEndpointNotificationCallback(_endpointNotifications); }
            catch { enumerator.Dispose(); throw; }
            _deviceEnumerator = enumerator;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Playback remains available when endpoint notifications are unavailable.
        }
    }

    public async Task PreloadAsync(int instrument, CancellationToken cancellationToken = default)
    {
        await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await GetCachedSamplesAsync(instrument, cancellationToken).ConfigureAwait(false); }
        finally { _prepareGate.Release(); }
    }

    public async Task PrepareAsync(int instrument, CancellationToken cancellationToken = default)
    {
        await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var samples = await GetCachedSamplesAsync(instrument, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _samples = samples;
                _preparedInstrument = instrument;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var endpointId = await Task.Run(GetDefaultEndpointId, cancellationToken)
                    .ConfigureAwait(false);
                WaveOutEvent? staleOutput;
                lock (_sync)
                {
                    if (IsOutputHealthy(endpointId)) return;
                    staleOutput = _output;
                    _output = null;
                    _outputReady = false;
                    _mixer.Clear();
                }
                staleOutput?.Dispose();

                try
                {
                    // Start the device off the UI thread and let its initial silent
                    // buffers drain before the score clock or first key can start.
                    WaveOutEvent? primingOutput = null;
                    long primingReadCount = 0;
                    await Task.Run(() =>
                    {
                        lock (_sync)
                        {
                            ObjectDisposedException.ThrowIf(_disposed, this);
                            _endpointChanged = false;
                            EnsureOutput(endpointId);
                            primingOutput = _output;
                            primingReadCount = _mixer.ReadCount;
                            _output!.Play();
                            _lastAttackTimestamp = Stopwatch.GetTimestamp();
                        }
                    }, cancellationToken).ConfigureAwait(false);

                    // Three buffers are queued initially. The next mixer read
                    // proves that the device has begun consuming them.
                    var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
                    while (_mixer.ReadCount < primingReadCount + 4 &&
                           Stopwatch.GetTimestamp() < deadline)
                        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                    if (_mixer.ReadCount < primingReadCount + 4)
                        throw new TimeoutException("Audio output did not start rendering in time.");
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (!ReferenceEquals(_output, primingOutput) || _endpointChanged)
                            throw new InvalidOperationException("Audio endpoint changed during preparation.");
                        _outputReady = true;
                    }
                    return;
                }
                catch (Exception exception) when (attempt == 0 &&
                    exception is not OperationCanceledException and not ObjectDisposedException)
                {
                    // Bluetooth reconnection can invalidate the stream while it
                    // starts. Retry once against the newly selected endpoint.
                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _prepareGate.Release();
        }
    }

    public void SetVolume(int volume)
    {
        lock (_sync) _masterVolume = Math.Clamp(volume, 0, 127) / 127f;
    }

    public void NoteOn(int instrument, int pitch, int velocity)
    {
        if (!GenshinKeyMap.TryMapPitch(pitch, 0, OutOfRangePolicy.Drop, out var key)) return;
        bool needsPreparation;
        lock (_sync) needsPreparation = !_outputReady || _endpointChanged ||
            Stopwatch.GetElapsedTime(_mixer.LastReadTimestamp) > TimeSpan.FromMilliseconds(500);
        if (needsPreparation) PrepareAsync(instrument).GetAwaiter().GetResult();
        lock (_sync)
        {
            if (_disposed || _preparedInstrument != instrument ||
                !_samples.TryGetValue(key.ToString().ToLowerInvariant(), out var samples)) return;
            _mixer.AddVoice(samples, _masterVolume * Math.Clamp(velocity, 1, 127) / 127f);
            _lastAttackTimestamp = Stopwatch.GetTimestamp();
        }
    }

    public void NoteOff(int pitch)
    {
        // Natural decay continues after key-up, including repeated same-key notes.
    }

    public void AllNotesOff()
    {
        WaveOutEvent? output;
        lock (_sync)
        {
            _mixer.Clear();
            output = _output;
            _output = null;
            _outputReady = false;
        }
        output?.Dispose();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _idleTimer.Dispose();
            _samples.Clear();
            _decodedCache.Clear();
        }
        AllNotesOff();
        if (_deviceEnumerator is not null)
        {
            try { _deviceEnumerator.UnregisterEndpointNotificationCallback(_endpointNotifications); }
            catch (System.Runtime.InteropServices.COMException) { }
            _deviceEnumerator.Dispose();
        }
    }

    private void EnsureOutput(string? endpointId)
    {
        if (_output is not null) return;
        var output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
        output.PlaybackStopped += (_, _) =>
        {
            var unexpectedlyStopped = false;
            lock (_sync)
            {
                if (ReferenceEquals(_output, output))
                {
                    _output = null;
                    _outputReady = false;
                    _mixer.Clear();
                    unexpectedlyStopped = true;
                }
            }
            if (unexpectedlyStopped) _ = Task.Run(output.Dispose);
        };
        try
        {
            output.Init(new SampleToWaveProvider16(_mixer));
            _output = output;
            _outputReady = false;
            _outputEndpointId = endpointId;
            _lastAttackTimestamp = Stopwatch.GetTimestamp();
        }
        catch
        {
            _output = null;
            _outputReady = false;
            output.Dispose();
            throw;
        }
    }

    private bool IsOutputHealthy(string? endpointId) =>
        _outputReady && !_endpointChanged && _output is { PlaybackState: PlaybackState.Playing } &&
        string.Equals(_outputEndpointId, endpointId, StringComparison.Ordinal) &&
        Stopwatch.GetElapsedTime(_mixer.LastReadTimestamp) < TimeSpan.FromMilliseconds(500);

    private static string? GetDefaultEndpointId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return endpoint.ID;
        }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    private async Task<Dictionary<string, float[]>> GetCachedSamplesAsync(
        int instrument, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_decodedCache.TryGetValue(instrument, out var cached))
            {
                _cacheOrder.Remove(instrument);
                _cacheOrder.AddLast(instrument);
                return cached;
            }
            if (_preparedInstrument == instrument && _samples.Count > 0) return _samples;
        }
        var samples = await Task.Run(() => LoadInstrument(instrument, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (samples.Count == 0) return samples;
            _decodedCache[instrument] = samples;
            _cacheOrder.Remove(instrument);
            _cacheOrder.AddLast(instrument);
            while (_cacheOrder.Count > 2)
            {
                var oldest = _cacheOrder.First!.Value;
                _cacheOrder.RemoveFirst();
                _decodedCache.Remove(oldest);
            }
        }
        return samples;
    }

    private void ReleaseWhenIdle()
    {
        WaveOutEvent? output;
        lock (_sync)
        {
            if (_disposed || _output is null || _mixer.VoiceCount != 0 ||
                Stopwatch.GetElapsedTime(_lastAttackTimestamp) < TimeSpan.FromSeconds(3)) return;
            output = _output;
            _output = null;
            _outputReady = false;
        }
        output.Dispose();
    }

    private Dictionary<string, float[]> LoadInstrument(int instrument, CancellationToken cancellationToken)
    {
        var folder = instrument switch
        {
            AuditionInstrumentIds.WindsongLyre => "windsong-lyre",
            AuditionInstrumentIds.FloralZither => "floral-zither",
            AuditionInstrumentIds.OldFloralZither => "old-floral-zither",
            AuditionInstrumentIds.VintageLyre => "vintage-lyre",
            AuditionInstrumentIds.Ukulele => "ukulele",
            AuditionInstrumentIds.LingeringEuphonia => "lingering-euphonia",
            AuditionInstrumentIds.LeapingSpiritPiano => "leaping-spirit-piano",
            _ => null,
        };
        var result = new Dictionary<string, float[]>();
        if (folder is null) return result;
        var directory = Path.Combine(_sampleRoot, folder);
        if (!Directory.Exists(directory)) return result;
        foreach (var path in Directory.EnumerateFiles(directory, "*.mp3"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[Path.GetFileNameWithoutExtension(path).ToLowerInvariant()] = Decode(path);
        }
        return result;
    }

    private static float[] Decode(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider source = reader;
        if (source.WaveFormat.SampleRate != SampleRate)
            source = new WdlResamplingSampleProvider(source, SampleRate);
        if (source.WaveFormat.Channels == 1)
            source = new MonoToStereoSampleProvider(source);
        if (source.WaveFormat.Channels != Channels)
            throw new InvalidDataException($"Unsupported sample channel count: {path}");

        var pcm = new ArrayBufferWriter<float>();
        var buffer = new float[8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            pcm.Write(buffer.AsSpan(0, read));

        // Trim long silent MP3 tails so voices stop consuming mixer work.
        var samples = pcm.WrittenSpan;
        var lastAudible = -1;
        for (var frame = samples.Length / Channels - 1; frame >= 0; frame--)
        {
            var index = frame * Channels;
            if (Math.Max(Math.Abs(samples[index]), Math.Abs(samples[index + 1])) < 0.001f) continue;
            lastAudible = frame;
            break;
        }
        var keptFrames = Math.Min(samples.Length / Channels,
            Math.Max(1, lastAudible + 1 + SampleRate / 10));
        return samples[..(keptFrames * Channels)].ToArray();
    }

    private sealed class PcmVoiceMixer : ISampleProvider
    {
        private const int MaximumVoices = 64;
        private readonly object _sync = new();
        private readonly List<Voice> _voices = [];
        private long _readCount;
        private long _lastReadTimestamp;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        public int VoiceCount { get { lock (_sync) return _voices.Count; } }
        public long ReadCount => Interlocked.Read(ref _readCount);
        public long LastReadTimestamp => Interlocked.Read(ref _lastReadTimestamp);

        public void AddVoice(float[] samples, float gain)
        {
            lock (_sync)
            {
                if (_voices.Count == MaximumVoices) _voices.RemoveAt(0);
                _voices.Add(new Voice(samples, gain));
            }
        }

        public void Clear() { lock (_sync) _voices.Clear(); }

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            lock (_sync)
            {
                for (var voiceIndex = _voices.Count - 1; voiceIndex >= 0; voiceIndex--)
                {
                    var voice = _voices[voiceIndex];
                    var length = Math.Min(count, voice.Samples.Length - voice.Position);
                    for (var index = 0; index < length; index++)
                        buffer[offset + index] += voice.Samples[voice.Position + index] * voice.Gain;
                    voice.Position += length;
                    if (voice.Position >= voice.Samples.Length) _voices.RemoveAt(voiceIndex);
                }
            }
            for (var index = offset; index < offset + count; index++)
                buffer[index] = Math.Clamp(buffer[index], -1f, 1f);
            Interlocked.Increment(ref _readCount);
            Interlocked.Exchange(ref _lastReadTimestamp, Stopwatch.GetTimestamp());
            return count;
        }

        private sealed class Voice(float[] samples, float gain)
        {
            public float[] Samples { get; } = samples;
            public float Gain { get; } = gain;
            public int Position { get; set; }
        }
    }

    private sealed class EndpointNotifications(Action changed) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) changed();
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active) changed();
        }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) { }
    }
}
