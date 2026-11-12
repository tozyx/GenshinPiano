using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Playback;

public sealed class AuditionTimeline(TimingDefinition timing)
{
    private readonly TempoSegment[] _segments = CreateTempoSegments(timing);

    public (long Tick, TimeSpan Position) Interpolate(AuditionProgress progress, TimeSpan elapsed)
    {
        var position = progress.Position + elapsed;
        if (position > progress.Duration) position = progress.Duration;
        return (TimeToTick(position, _segments, progress.DurationTick), position);
    }

    private readonly record struct TempoSegment(long StartTick, double StartSeconds, double TicksPerSecond);


    private static TempoSegment[] CreateTempoSegments(TimingDefinition timing)
    {
        var changes = timing.TempoMap.OrderBy(change => change.Tick).ToArray();
        if (changes.Length == 0) return [];
        var segments = new TempoSegment[changes.Length];
        double seconds = 0;
        for (var index = 0; index < changes.Length; index++)
        {
            if (index > 0)
            {
                seconds += (changes[index].Tick - changes[index - 1].Tick) /
                           segments[index - 1].TicksPerSecond;
            }
            segments[index] = new TempoSegment(changes[index].Tick, seconds,
                changes[index].Bpm * timing.Ppq / 60d);
        }
        return segments;
    }

    private static long TimeToTick(TimeSpan time, TempoSegment[] segments, long maximumTick)
    {
        if (segments.Length == 0) return 0;
        var seconds = time.TotalSeconds;
        var left = 0;
        var right = segments.Length;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (segments[middle].StartSeconds <= seconds) left = middle + 1;
            else right = middle;
        }
        var segment = segments[Math.Max(0, left - 1)];
        var tick = segment.StartTick + (long)Math.Floor(
            Math.Max(0, seconds - segment.StartSeconds) * segment.TicksPerSecond + 1e-7);
        return Math.Clamp(tick, 0, maximumTick);
    }
}
