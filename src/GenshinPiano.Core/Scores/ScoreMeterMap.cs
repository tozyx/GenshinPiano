namespace GenshinPiano.Core.Scores;

/// <summary>Maps absolute quarter-note ticks to bars and denominator-note beats.</summary>
public sealed class ScoreMeterMap
{
    private readonly Segment[] _segments;

    public ScoreMeterMap(TimingDefinition timing)
    {
        var ppq = timing.Ppq > 0 ? timing.Ppq : 480;
        var changes = new[] { new TimeSignatureChange() }
            .Concat((timing.TimeSignatures ?? []).Where(change => change is not null && change.Tick >= 0 &&
                IsValidSignature(change.Numerator, change.Denominator)))
            .GroupBy(change => change.Tick).Select(group => group.Last()).OrderBy(change => change.Tick).ToArray();
        _segments = new Segment[changes.Length];
        for (var index = 0; index < changes.Length; index++)
        {
            var change = changes[index];
            var firstMeasure = 1L;
            if (index > 0)
            {
                var previous = _segments[index - 1];
                // A change inside a bar closes that partial bar; it never moves notes.
                firstMeasure = ToMeasureNumber(previous.FirstMeasure +
                    decimal.Ceiling((change.Tick - previous.Signature.Tick) / previous.MeasureLength));
            }
            var beatLength = ppq * 4m / change.Denominator;
            _segments[index] = new Segment(change, firstMeasure, beatLength, beatLength * change.Numerator);
        }
    }

    public static bool IsValidSignature(int numerator, int denominator) =>
        numerator > 0 && denominator > 0 && (denominator & (denominator - 1)) == 0;

    public TimeSignatureChange GetSignature(long tick) => _segments[FindSegment(tick)].Signature;

    public decimal GetMeasureLength(long tick) => _segments[FindSegment(tick)].MeasureLength;

    public MeterPosition GetPosition(long tick)
    {
        var segment = _segments[FindSegment(tick)];
        var offset = (decimal)Math.Max(0, tick) - segment.Signature.Tick;
        return new MeterPosition(
            ToMeasureNumber(segment.FirstMeasure + decimal.Floor(offset / segment.MeasureLength)),
            (int)decimal.Floor(offset % segment.MeasureLength / segment.BeatLength) + 1);
    }

    public IEnumerable<MeterLine> EnumerateMeasures(long fromTick, long toTick, decimal minimumSpacing = 0) =>
        EnumerateLines(fromTick, toTick, minimumSpacing, measuresOnly: true);

    public IEnumerable<MeterLine> EnumerateBeats(long fromTick, long toTick, decimal minimumSpacing = 0) =>
        EnumerateLines(fromTick, toTick, minimumSpacing, measuresOnly: false);

    private IEnumerable<MeterLine> EnumerateLines(long fromTick, long toTick, decimal minimumSpacing, bool measuresOnly)
    {
        fromTick = Math.Max(0, fromTick);
        if (toTick < fromTick) yield break;
        for (var index = FindSegment(fromTick); index < _segments.Length; index++)
        {
            var segment = _segments[index];
            var start = segment.Signature.Tick;
            if (start > toTick) yield break;
            var end = index + 1 < _segments.Length ? _segments[index + 1].Signature.Tick : (decimal)long.MaxValue + 1;
            var unit = measuresOnly ? segment.MeasureLength : segment.BeatLength;
            // Bound the amount of work for very small meters and distant viewports.
            var stride = Math.Max(1, decimal.Ceiling(minimumSpacing / unit));
            var first = decimal.Ceiling((Math.Max(fromTick, start) - (decimal)start) / unit / stride) * stride;
            for (var number = first; ; number += stride)
            {
                var tick = start + number * unit;
                if (tick > toTick || tick >= end) break;
                var measureOffset = measuresOnly ? number : decimal.Floor(number / segment.Signature.Numerator);
                yield return new MeterLine(tick, ToMeasureNumber(segment.FirstMeasure + measureOffset),
                    measuresOnly || number % segment.Signature.Numerator == 0,
                    start > 0 && number == 0, segment.Signature.Numerator, segment.Signature.Denominator);
            }
        }
    }

    private int FindSegment(long tick)
    {
        var left = 0;
        var right = _segments.Length - 1;
        while (left < right)
        {
            var middle = left + (right - left + 1) / 2;
            if (_segments[middle].Signature.Tick <= tick) left = middle;
            else right = middle - 1;
        }
        return left;
    }

    private static long ToMeasureNumber(decimal value) => (long)Math.Min(long.MaxValue, value);

    private sealed record Segment(TimeSignatureChange Signature, long FirstMeasure, decimal BeatLength, decimal MeasureLength);
}

public readonly record struct MeterPosition(long Measure, int Beat);

public readonly record struct MeterLine(decimal Tick, long Measure, bool IsMeasureStart, bool IsSignatureChange,
    int Numerator, int Denominator);
