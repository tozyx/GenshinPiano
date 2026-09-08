using System.IO;
using System.Windows.Media;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;

namespace GenshinPiano.App.Services;

public sealed class WindowsSampleAuditionOutput(string sampleRoot) : ISampleAuditionOutput, IDisposable
{
    private readonly List<MediaPlayer> _players = [];
    private double _masterVolume = 1;

    public void SetVolume(int volume) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            _masterVolume = Math.Clamp(volume, 0, 127) / 127d);

    public void NoteOn(int instrument, int pitch, int velocity) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Play(instrument, pitch, velocity));

    public void NoteOff(int pitch)
    {
        // These samples contain their natural decay. Stopping on key-up would
        // truncate the instrument tail. Repeated attacks use independent voices.
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

        if (_players.Count >= 128)
        {
            var previous = _players[0];
            _players.RemoveAt(0);
            previous.Stop();
            previous.Close();
        }
        var player = new MediaPlayer { Volume = _masterVolume * Math.Clamp(velocity, 1, 127) / 127d };
        player.MediaEnded += (_, _) => RemovePlayer(pitch, player);
        player.MediaFailed += (_, _) => RemovePlayer(pitch, player);
        _players.Add(player);
        player.Open(new Uri(path, UriKind.Absolute));
        player.Play();
    }

    private void RemovePlayer(int pitch, MediaPlayer player)
    {
        _players.Remove(player);
        player.Close();
    }

    private void StopAll()
    {
        var voices = _players.ToArray();
        _players.Clear();
        foreach (var player in voices)
        {
            player.Stop();
            player.Close();
        }
        _players.Clear();
    }
}
