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

    public static ScoreDocument OptimizeAllDurations(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);

        return score with
        {
            Tracks = score.Tracks.Select(track => track with
            {
                Notes = OptimizeTrackDurations(track.Notes, score.Timing.Ppq),
            }).ToList(),
        };
    }

    public static ScoreDocument GenerateShortPressDurations(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);

        return score with
        {
            Tracks = score.Tracks.Select(track => track with
            {
                Notes = GenerateTrackShortPressDurations(track.Notes, score.Timing),
            }).ToList(),
        };
    }

    private static List<NoteEvent> GenerateTrackShortPressDurations(
        IReadOnlyList<NoteEvent> notes,
        TimingDefinition timing)
    {
        var ppq = timing.Ppq;
        var starts = notes.Select(note => note.StartTick).Distinct().Order().ToArray();
        var nextStartByTick = starts
            .Select((start, index) => new
            {
                Start = start,
                Next = index + 1 < starts.Length ? (long?)starts[index + 1] : null,
            })
            .ToDictionary(item => item.Start, item => item.Next);
        var nextSamePitchById = notes.GroupBy(note => note.Pitch)
            .SelectMany(group =>
            {
                var pitchStarts = group.Select(note => note.StartTick).Distinct().Order().ToArray();
                var nextByStart = pitchStarts.Select((start, index) => new
                {
                    Start = start,
                    Next = index + 1 < pitchStarts.Length ? (long?)pitchStarts[index + 1] : null,
                }).ToDictionary(item => item.Start, item => item.Next);
                return group.Select(note => new { note.Id, Next = nextByStart[note.StartTick] });
            })
            .ToDictionary(item => item.Id, item => item.Next);

        return notes.Select(note =>
        {
            var rhythmTick = nextStartByTick[note.StartTick] is { } nextStart
                ? Math.Max(1, nextStart - note.StartTick)
                : note.RhythmTick is > 0 ? note.RhythmTick.Value : ppq;
            var beats = rhythmTick / (double)ppq;
            var holdMilliseconds = beats switch
            {
                <= 0.25 => 20d,
                <= 0.50 => 25d,
                <= 1.00 => 30d,
                _ => 40d,
            };
            var bpm = GetTempoAtTick(timing, note.StartTick);
            var durationTick = Math.Max(
                1,
                (long)Math.Round(
                    holdMilliseconds * bpm * ppq / 60_000d,
                    MidpointRounding.AwayFromZero));

            if (nextSamePitchById[note.Id] is { } nextSamePitch)
            {
                durationTick = Math.Min(
                    durationTick,
                    Math.Max(1, nextSamePitch - note.StartTick));
            }

            return note with
            {
                RhythmTick = rhythmTick,
                DurationTick = durationTick,
                DurationMode = DurationMode.Explicit,
                GateRatio = null,
            };
        }).ToList();
    }

    private static double GetTempoAtTick(TimingDefinition timing, long tick) =>
        timing.TempoMap
            .Where(change => change.Tick <= tick && change.Bpm > 0)
            .OrderBy(change => change.Tick)
            .LastOrDefault()?.Bpm ?? 120d;

    private static List<NoteEvent> OptimizeTrackDurations(
        IReadOnlyList<NoteEvent> notes,
        int ppq)
    {
        var starts = notes.Select(note => note.StartTick).Distinct().Order().ToArray();
        var nextStartByTick = starts
            .Select((start, index) => new
            {
                Start = start,
                Next = index + 1 < starts.Length ? starts[index + 1] : start + ppq,
            })
            .ToDictionary(item => item.Start, item => item.Next);

        return notes.Select(note =>
        {
            var articulation = note.Articulation;
            var gateRatio = note.GateRatio;
            if (articulation == NoteArticulation.Custom &&
                gateRatio is not (>= MinimumGateRatio and <= MaximumGateRatio))
            {
                articulation = NoteArticulation.Natural;
                gateRatio = GetGateRatio(articulation);
            }

            gateRatio ??= GetGateRatio(articulation);
            var rhythmTick = Math.Max(1, nextStartByTick[note.StartTick] - note.StartTick);
            var optimized = note with
            {
                RhythmTick = rhythmTick,
                DurationMode = DurationMode.Auto,
                Articulation = articulation,
                GateRatio = gateRatio,
            };
            return optimized with
            {
                DurationTick = ResolveDuration(optimized, null, ppq),
            };
        }).ToList();
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
