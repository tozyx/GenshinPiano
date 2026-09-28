using System.Diagnostics;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Playback;

public sealed record AuditionProgress(
    long Tick,
    long DurationTick,
    TimeSpan Position,
    TimeSpan Duration,
    long SampleTimestamp = 0);

public static class AuditionInstrumentIds
{
    public const int WindsongLyre = -1;
    public const int FloralZither = -2;
    public const int OldFloralZither = -3;
    public const int VintageLyre = -4;
    public const int Ukulele = -5;
    public const int LingeringEuphonia = -6;
    public const int LeapingSpiritPiano = -7;

    public static bool IsSampled(int instrument) => instrument is >= LeapingSpiritPiano and <= WindsongLyre;
}

public sealed class ScoreAuditionService(IMidiOutput output, ISampleAuditionOutput? sampleOutput = null)
{
    private const double ReferenceVelocityGain = 127d / 96d;
    // The Windows MIDI synthesizer has limited output headroom. Keep its bus at
    // full scale and attenuate the sampled-instrument bus by roughly 6 dB.
    private const double SampleBusGain = 0.5;
    private double _velocityGain = ReferenceVelocityGain;

    public void SetVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, 127);
        var normalized = volume / 127d;
        var perceptualVolume = (int)Math.Round(Math.Pow(normalized, .6) * 127);
        Volatile.Write(ref _velocityGain, ReferenceVelocityGain);
        output.SetVolume(perceptualVolume);
        sampleOutput?.SetVolume((int)Math.Round(perceptualVolume * SampleBusGain));
    }

    public Task PrepareAsync(int instrument, CancellationToken cancellationToken = default) =>
        AuditionInstrumentIds.IsSampled(instrument) && sampleOutput is not null
            ? sampleOutput.PrepareAsync(instrument, cancellationToken)
            : output.PrepareAsync(Math.Clamp(instrument, 0, 127), cancellationToken);

    public async Task PlayAsync(
        ScoreDocument score,
        long startTick,
        int instrument,
        bool naturalSustain,
        IProgress<AuditionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        long? endTick = null,
        double playbackSpeed = 1)
    {
        playbackSpeed = double.IsFinite(playbackSpeed) ? Math.Clamp(playbackSpeed, 0.25, 2) : 1;
        var plan = ScoreAuditionPlanner.Create(score, naturalSustain);
        startTick = Math.Clamp(startTick, 0, plan.DurationTick);
        var playbackEndTick = Math.Clamp(endTick ?? plan.DurationTick, startTick, plan.DurationTick);
        var startTime = ScorePlaybackPlanner.TickToTime(startTick, score.Timing);
        var endTime = ScorePlaybackPlanner.TickToTime(playbackEndTick, score.Timing);
        var events = plan.Events
            .Where(item => item.Tick >= startTick && item.Tick <= playbackEndTick)
            .ToArray();
        var eventIndex = 0;
        var sampled = AuditionInstrumentIds.IsSampled(instrument) && sampleOutput is not null;
        await PrepareAsync(instrument, cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        var completed = false;
        try
        {
            // The final note-off is scheduled exactly at endTime. A loop guarded by
            // "now < endTime" can exit before dispatching it (or even the last short
            // note-on when the scheduler wakes late).
            while (eventIndex < events.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var absoluteTime = startTime +
                    TimeSpan.FromTicks((long)(stopwatch.Elapsed.Ticks * playbackSpeed));
                if (events[eventIndex].Offset > absoluteTime)
                {
                    var remaining = (events[eventIndex].Offset - absoluteTime).TotalMilliseconds / playbackSpeed;
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(remaining, 1, 8)), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                while (eventIndex < events.Length && events[eventIndex].Offset <= absoluteTime)
                {
                    var item = events[eventIndex++];
                    foreach (var pitch in item.NotesOff)
                    {
                        if (sampled) sampleOutput!.NoteOff(pitch);
                        else output.NoteOff(pitch);
                    }

                    foreach (var note in item.NotesOn)
                    {
                        var velocity = (int)Math.Round(note.Velocity * Volatile.Read(ref _velocityGain));
                        if (sampled) sampleOutput!.NoteOn(instrument, note.Pitch, Math.Clamp(velocity, 1, 127));
                        else output.NoteOn(note.Pitch, Math.Clamp(velocity, 1, 127));
                    }
                }

                var tick = TimeToTick(absoluteTime, score.Timing, plan.DurationTick);
                progress?.Report(new AuditionProgress(
                    tick,
                    playbackEndTick,
                    absoluteTime,
                    endTime,
                    Stopwatch.GetTimestamp()));
            }

            completed = true;

            progress?.Report(new AuditionProgress(
                playbackEndTick,
                playbackEndTick,
                endTime,
                endTime,
                Stopwatch.GetTimestamp()));
        }
        finally
        {
            output.AllNotesOff();
            // Samples have their own natural tail. Only a user interruption should
            // silence them; stopping at the score's last note-off clips that note.
            if (!completed) sampleOutput?.AllNotesOff();
        }
    }

    public async Task PreviewNoteAsync(
        int pitch,
        int instrument,
        int velocity = 96,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        pitch = Math.Clamp(pitch, 0, 127);
        var sampled = AuditionInstrumentIds.IsSampled(instrument) && sampleOutput is not null;
        await PrepareAsync(instrument, cancellationToken).ConfigureAwait(false);
        var adjustedVelocity = (int)Math.Round(
            Math.Clamp(velocity, 1, 127) * Volatile.Read(ref _velocityGain));
        if (sampled) sampleOutput!.NoteOn(instrument, pitch, Math.Clamp(adjustedVelocity, 1, 127));
        else output.NoteOn(pitch, Math.Clamp(adjustedVelocity, 1, 127));
        try
        {
            await Task.Delay(duration ?? TimeSpan.FromMilliseconds(220), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (sampled) sampleOutput!.NoteOff(pitch);
            else output.NoteOff(pitch);
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
