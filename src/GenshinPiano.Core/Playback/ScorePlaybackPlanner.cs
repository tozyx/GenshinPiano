using GenshinPiano.Core.Scores;

namespace GenshinPiano.Core.Playback;

public sealed record ScheduledKeyChange(
    long Tick,
    TimeSpan Offset,
    IReadOnlyList<GenshinKey> KeysUp,
    IReadOnlyList<GenshinKey> KeysDown);

public sealed record ScorePlaybackPlan(
    IReadOnlyList<ScheduledKeyChange> Events,
    TimeSpan Duration,
    int SkippedNoteCount,
    int AttackCount);

public static class ScorePlaybackPlanner
{
    public static ScorePlaybackPlan Create(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var validationErrors = ScoreValidator.Validate(score);
        if (validationErrors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, validationErrors));
        }

        if (!string.Equals(score.Playback.Mapping, "genshin-21-key", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unsupported playback mapping: {score.Playback.Mapping}");
        }

        var skippedNotes = 0;
        var mappedNotes = new List<MappedNote>();
        foreach (var track in score.Tracks.Where(track => !track.IsMuted))
        {
            var starts = track.Notes.Select(note => note.StartTick).Distinct().Order().ToArray();
            var nextStartByTick = starts
                .Select((start, index) => new
                {
                    Start = start,
                    Next = index + 1 < starts.Length ? (long?)starts[index + 1] : null,
                })
                .ToDictionary(item => item.Start, item => item.Next);

            foreach (var note in track.Notes)
            {
                if (!GenshinKeyMap.TryMapPitch(
                        note.Pitch,
                        score.Playback.Transpose,
                        score.Playback.OutOfRangePolicy,
                        out var key))
                {
                    if (score.Playback.OutOfRangePolicy == OutOfRangePolicy.Reject)
                    {
                        throw new InvalidDataException(
                            $"MIDI pitch {note.Pitch} cannot be played on the 21-key lyre.");
                    }

                    skippedNotes++;
                    continue;
                }

                var resolvedDuration = NoteDurationCalculator.ResolveDuration(
                    note,
                    nextStartByTick[note.StartTick],
                    score.Timing.Ppq);
                mappedNotes.Add(new MappedNote(
                    key,
                    note.StartTick,
                    checked(note.StartTick + resolvedDuration)));
            }
        }

        var changes = new Dictionary<long, KeyChangeBuilder>();
        foreach (var keyGroup in mappedNotes.GroupBy(note => note.Key))
        {
            var intervals = keyGroup
                .GroupBy(note => note.StartTick)
                .Select(group => new KeyInterval(group.Key, group.Max(note => note.EndTick)))
                .OrderBy(interval => interval.StartTick)
                .ToArray();

            for (var index = 0; index < intervals.Length; index++)
            {
                var interval = intervals[index];
                var endTick = index + 1 < intervals.Length
                    ? Math.Min(interval.EndTick, intervals[index + 1].StartTick)
                    : interval.EndTick;

                GetChange(changes, interval.StartTick).KeysDown.Add(keyGroup.Key);
                GetChange(changes, endTick).KeysUp.Add(keyGroup.Key);
            }
        }

        var events = changes
            .OrderBy(pair => pair.Key)
            .Select(pair => new ScheduledKeyChange(
                pair.Key,
                TickToTime(pair.Key, score.Timing),
                pair.Value.KeysUp.OrderBy(key => key).ToArray(),
                pair.Value.KeysDown.OrderBy(key => key).ToArray()))
            .ToArray();
        var planDuration = events.Length == 0 ? TimeSpan.Zero : events[^1].Offset;
        var attackCount = events.Count(item => item.KeysDown.Count > 0);
        return new ScorePlaybackPlan(events, planDuration, skippedNotes, attackCount);
    }

    public static TimeSpan TickToTime(long targetTick, TimingDefinition timing)
    {
        if (targetTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTick));
        }

        if (timing.Ppq <= 0)
        {
            throw new InvalidDataException("PPQ must be greater than zero.");
        }

        var tempoMap = timing.TempoMap.OrderBy(change => change.Tick).ToArray();
        if (tempoMap.Length == 0 || tempoMap[0].Tick != 0)
        {
            throw new InvalidDataException("The tempo map must begin at tick 0.");
        }

        double elapsedMilliseconds = 0;
        for (var index = 0; index < tempoMap.Length; index++)
        {
            var current = tempoMap[index];
            if (current.Tick >= targetTick)
            {
                break;
            }

            var segmentEnd = index + 1 < tempoMap.Length
                ? Math.Min(targetTick, tempoMap[index + 1].Tick)
                : targetTick;
            var tickCount = segmentEnd - current.Tick;
            elapsedMilliseconds += tickCount * 60_000d / (current.Bpm * timing.Ppq);

            if (segmentEnd == targetTick)
            {
                break;
            }
        }

        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    private static KeyChangeBuilder GetChange(IDictionary<long, KeyChangeBuilder> changes, long tick)
    {
        if (!changes.TryGetValue(tick, out var change))
        {
            change = new KeyChangeBuilder();
            changes.Add(tick, change);
        }

        return change;
    }

    private sealed record MappedNote(GenshinKey Key, long StartTick, long EndTick);

    private sealed record KeyInterval(long StartTick, long EndTick);

    private sealed class KeyChangeBuilder
    {
        public HashSet<GenshinKey> KeysUp { get; } = [];

        public HashSet<GenshinKey> KeysDown { get; } = [];
    }
}
