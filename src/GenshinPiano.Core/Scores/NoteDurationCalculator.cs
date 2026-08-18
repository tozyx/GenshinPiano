namespace GenshinPiano.Core.Scores;

public static class NoteDurationCalculator
{
    public const double MinimumGateRatio = 0.10;

    public const double MaximumGateRatio = 0.95;

    public static double GetGateRatio(NoteArticulation articulation) => articulation switch
    {
        NoteArticulation.Legato => 0.95,
        NoteArticulation.Natural => 0.80,
        NoteArticulation.Detached => 0.50,
        NoteArticulation.Staccato => 0.30,
        NoteArticulation.Custom => throw new InvalidOperationException(
            "Custom articulation requires an explicit gate ratio."),
        _ => throw new ArgumentOutOfRangeException(nameof(articulation), articulation, null),
    };

    public static long ResolveDuration(NoteEvent note, long? nextStartTick, int ppq)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (note.DurationMode == DurationMode.Explicit)
        {
            return note.DurationTick;
        }

        var rhythmTick = note.RhythmTick
            ?? (nextStartTick is > 0 && nextStartTick > note.StartTick
                ? nextStartTick.Value - note.StartTick
                : ppq);
        var gateRatio = note.GateRatio ?? GetGateRatio(note.Articulation);
        if (gateRatio is < MinimumGateRatio or > MaximumGateRatio)
        {
            throw new InvalidDataException(
                $"Gate ratio must be between {MinimumGateRatio:P0} and {MaximumGateRatio:P0}.");
        }

        var resolved = Math.Round(
            rhythmTick * gateRatio,
            MidpointRounding.AwayFromZero);
        return Math.Max(1, checked((long)resolved));
    }

    public static ScoreDocument ApplyAutoDurations(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);

        return score with
        {
            Tracks = score.Tracks.Select(track => track with
            {
                Notes = ApplyTrackAutoDurations(track.Notes, score.Timing.Ppq),
            }).ToList(),
        };
    }

    private static List<NoteEvent> ApplyTrackAutoDurations(IReadOnlyList<NoteEvent> notes, int ppq)
    {
        var starts = notes.Select(note => note.StartTick).Distinct().Order().ToArray();
        var nextStartByTick = starts
            .Select((start, index) => new
            {
                Start = start,
                Next = index + 1 < starts.Length ? (long?)starts[index + 1] : null,
            })
            .ToDictionary(item => item.Start, item => item.Next);

        return notes.Select(note => note.DurationMode == DurationMode.Auto
            ? note with
            {
                DurationTick = ResolveDuration(note, nextStartByTick[note.StartTick], ppq),
            }
            : note).ToList();
    }
}
