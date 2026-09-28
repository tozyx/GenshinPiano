using System.ComponentModel;
using System.Runtime.InteropServices;
using GenshinPiano.Application.Abstractions;

namespace GenshinPiano.Infrastructure.Input;

public sealed class WindowsMidiOutput : IMidiOutput
{
    private const uint MidiMapper = 0xFFFFFFFF;
    private static readonly TimeSpan IdleReleaseDelay = TimeSpan.FromSeconds(3);

    private readonly object _sync = new();
    private readonly int[] _activeNotes = new int[128];
    private readonly Timer _idleTimer;
    private IntPtr _handle;
    private int _activeNoteCount;
    private int _instrument;
    private int _volume = 127;
    private uint _previousDeviceVolume;
    private bool _restoreDeviceVolume;
    private bool _disposed;

    public WindowsMidiOutput()
    {
        // Opening the Windows MIDI mapper can keep a Bluetooth audio endpoint active
        // even when no notes are sounding. Only open it for an actual MIDI note.
        _idleTimer = new Timer(_ => CloseWhenIdle(), null, Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    public Task PrepareAsync(int program, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                ThrowIfDisposed();
                _instrument = Math.Clamp(program, 0, 127);
                _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                OpenIfNeeded();
                Send(0xC0 | (_instrument << 8));
                ScheduleIdleRelease();
            }
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);

    public void SetInstrument(int program)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _instrument = Math.Clamp(program, 0, 127);
            if (_handle != IntPtr.Zero) Send(0xC0 | (_instrument << 8));
        }
    }

    public void SetVolume(int volume)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _volume = Math.Clamp(volume, 0, 127);
            if (_handle != IntPtr.Zero) Send(0xB0 | (7 << 8) | (_volume << 16));
        }
    }

    public void NoteOn(int pitch, int velocity)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            OpenIfNeeded();
            pitch = Math.Clamp(pitch, 0, 127);
            Send(0x90 | (pitch << 8) | (Math.Clamp(velocity, 0, 127) << 16));
            _activeNotes[pitch]++;
            _activeNoteCount++;
        }
    }

    public void NoteOff(int pitch)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            pitch = Math.Clamp(pitch, 0, 127);
            if (_handle != IntPtr.Zero) Send(0x80 | (pitch << 8));
            if (_activeNotes[pitch] > 0)
            {
                _activeNotes[pitch]--;
                _activeNoteCount--;
            }
            ScheduleIdleRelease();
        }
    }

    public void AllNotesOff()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_handle != IntPtr.Zero) Send(0xB0 | (123 << 8));
            Array.Clear(_activeNotes);
            _activeNoteCount = 0;
            ScheduleIdleRelease();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            Close();
            _idleTimer.Dispose();
        }
    }

    private void OpenIfNeeded()
    {
        if (_handle != IntPtr.Zero) return;

        var result = MidiOutOpen(out _handle, MidiMapper, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0)
        {
            _handle = IntPtr.Zero;
            throw new Win32Exception((int)result, "Unable to open the Windows MIDI synthesizer.");
        }

        Send(0xC0 | (_instrument << 8));
        // The WinMM device has a second volume stage beyond MIDI CC7. On
        // synths that expose it, maximize this instance while it is open.
        _restoreDeviceVolume = MidiOutGetVolume(_handle, out _previousDeviceVolume) == 0 &&
                               MidiOutSetVolume(_handle, 0xFFFFFFFF) == 0;
        Send(0xB0 | (7 << 8) | (_volume << 16));
        Send(0xB0 | (11 << 8) | (127 << 16));
    }

    private void ScheduleIdleRelease()
    {
        if (_handle != IntPtr.Zero && _activeNoteCount == 0)
            _idleTimer.Change(IdleReleaseDelay, Timeout.InfiniteTimeSpan);
    }

    private void CloseWhenIdle()
    {
        lock (_sync)
        {
            if (!_disposed && _activeNoteCount == 0) Close();
        }
    }

    private void Close()
    {
        if (_handle == IntPtr.Zero) return;
        Send(0xB0 | (123 << 8));
        if (_restoreDeviceVolume) MidiOutSetVolume(_handle, _previousDeviceVolume);
        _restoreDeviceVolume = false;
        MidiOutClose(_handle);
        _handle = IntPtr.Zero;
        Array.Clear(_activeNotes);
        _activeNoteCount = 0;
    }

    private void Send(int message) => MidiOutShortMsg(_handle, message);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    [DllImport("winmm.dll", EntryPoint = "midiOutOpen")]
    private static extern uint MidiOutOpen(
        out IntPtr handle,
        uint deviceId,
        IntPtr callback,
        IntPtr instance,
        uint flags);

    [DllImport("winmm.dll", EntryPoint = "midiOutShortMsg")]
    private static extern uint MidiOutShortMsg(IntPtr handle, int message);

    [DllImport("winmm.dll", EntryPoint = "midiOutClose")]
    private static extern uint MidiOutClose(IntPtr handle);

    [DllImport("winmm.dll", EntryPoint = "midiOutGetVolume")]
    private static extern uint MidiOutGetVolume(IntPtr handle, out uint volume);

    [DllImport("winmm.dll", EntryPoint = "midiOutSetVolume")]
    private static extern uint MidiOutSetVolume(IntPtr handle, uint volume);
}
