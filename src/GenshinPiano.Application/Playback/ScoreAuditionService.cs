using System.Diagnostics;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Playback;

public sealed record AuditionProgress(long Tick, long DurationTick, TimeSpan Position, TimeSpan Duration);

public sealed class ScoreAuditionService(IMidiOutput output)
{
    private double _velocityGain = 1;

    public void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, 127);
        Volatile.Write(ref _velocityGain, volume / 101.6);
        output.SetVolume(volume);
    }

    public async Task PlayAsync(
        ScoreDocument score,
        long startTick,
        int instrument,
        bool naturalSustain,
        IProgress<AuditionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        long? endTick = null)
    {
        var plan = ScoreAuditionPlanner.Create(score, naturalSustain);
        startTick = Math.Clamp(startTick, 0, plan.DurationTick);
        var playbackEndTick = Math.Clamp(endTick ?? plan.DurationTick, startTick, plan.DurationTick);
        var startTime = ScorePlaybackPlanner.TickToTime(startTick, score.Timing);
        var endTime = ScorePlaybackPlanner.TickToTime(playbackEndTick, score.Timing);
        var events = plan.Events
            .Where(item => item.Tick >= startTick && item.Tick <= playbackEndTick)
            .ToArray();
        var eventIndex = 0;
        output.SetInstrument(Math.Clamp(instrument, 0, 127));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            while (startTime + stopwatch.Elapsed < endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var absoluteTime = startTime + stopwatch.Elapsed;
                while (eventIndex < events.Length && events[eventIndex].Offset <= absoluteTime)
                {
                    var item = events[eventIndex++];
                    foreach (var pitch in item.NotesOff)
                    {
                        output.NoteOff(pitch);
                    }

                    foreach (var note in item.NotesOn)
                    {
                        var velocity = (int)Math.Round(note.Velocity * Volatile.Read(ref _velocityGain));
                        output.NoteOn(note.Pitch, Math.Clamp(velocity, 1, 127));
                    }
                }

                var tick = TimeToTick(absoluteTime, score.Timing, plan.DurationTick);
                progress?.Report(new AuditionProgress(tick, playbackEndTick, absoluteTime, endTime));
                await Task.Delay(16, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new AuditionProgress(
                playbackEndTick,
                playbackEndTick,
                endTime,
                endTime));
        }
        finally
        {
            output.AllNotesOff();
        }
    }

    private static long TimeToTick(TimeSpan time, TimingDefinition timing, long maximumTick)
    {
        long low = 0;
        var high = maximumTick;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (ScorePlaybackPlanner.TickToTime(middle, timing) <= time)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }
}
