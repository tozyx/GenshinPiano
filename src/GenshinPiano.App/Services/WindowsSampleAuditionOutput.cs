using System.IO;
using System.Windows.Media;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;

namespace GenshinPiano.App.Services;

public sealed class WindowsSampleAuditionOutput(string sampleRoot) : ISampleAuditionOutput, IDisposable
{
    private readonly Dictionary<int, MediaPlayer> _players = [];
    private double _masterVolume = 1;

    public void SetVolume(int volume) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _masterVolume = Math.Clamp(volume, 0, 127) / 127d);

    public void NoteOn(int instrument, int pitch, int velocity) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Play(instrument, pitch, velocity));

    public void NoteOff(int pitch)
    {
        // These samples contain their natural decay. Stopping on key-up would
        // truncate the instrument tail, so replacement happens on the next attack.
    }

    public void AllNotesOff() =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(StopAll);

    public void Dispose()
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) StopAll();
        else System.Windows.Application.Current.Dispatcher.Invoke(StopAll);
    }

    private void Play(int instrument, int pitch, int velocity)
    {
        if (!GenshinKeyMap.TryMapPitch(pitch, 0, GenshinPiano.Core.Scores.OutOfRangePolicy.Drop, out var key)) return;
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
        if (folder is null) return;
        var path = Path.Combine(sampleRoot, folder, key.ToString().ToLowerInvariant() + ".mp3");
        if (!File.Exists(path)) return;

        if (_players.Remove(pitch, out var previous))
        {
            previous.Stop();
            previous.Close();
        }
        var player = new MediaPlayer { Volume = _masterVolume * Math.Clamp(velocity, 1, 127) / 127d };
        player.MediaEnded += (_, _) => RemovePlayer(pitch, player);
        player.MediaFailed += (_, _) => RemovePlayer(pitch, player);
        _players[pitch] = player;
        player.Open(new Uri(path, UriKind.Absolute));
        player.Play();
    }

    private void RemovePlayer(int pitch, MediaPlayer player)
    {
        if (_players.TryGetValue(pitch, out var current) && ReferenceEquals(current, player))
            _players.Remove(pitch);
        player.Close();
    }

    private void StopAll()
    {
        foreach (var player in _players.Values)
        {
            player.Stop();
            player.Close();
        }
        _players.Clear();
    }
}
