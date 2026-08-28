namespace GenshinPiano.Core.Scores;

[Flags]
public enum ScoreCleanupOptions
{
    None = 0,
    RemoveExactDuplicates = 1,
    TrimSamePitchOverlaps = 2,
    RemoveVeryShortNotes = 4,
}

public sealed record ScoreCleanupResult(
    ScoreDocument Score,
    int RemovedDuplicates,
    int TrimmedOverlaps,
    int RemovedVeryShortNotes)
{
    public int TotalChanges => RemovedDuplicates + TrimmedOverlaps + RemovedVeryShortNotes;
}

public static class ScoreCleaner
{
    public static ScoreCleanupResult Clean(ScoreDocument score, ScoreCleanupOptions options)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (options == ScoreCleanupOptions.None)
        {
            return new ScoreCleanupResult(score, 0, 0, 0);
        }

        var removedDuplicates = 0;
        var trimmedOverlaps = 0;
        var removedVeryShort = 0;
        var veryShortThreshold = Math.Max(1, score.Timing.Ppq / 8L);
        var tracks = score.Tracks.Select(track =>
        {
            var notes = track.Notes.ToList();
            if (options.HasFlag(ScoreCleanupOptions.RemoveExactDuplicates))
            {
                var duplicateIndices = notes
                    .Select((note, index) => (Note: note, Index: index))
                    .GroupBy(item => (item.Note.Pitch, item.Note.StartTick))
                    .SelectMany(group => group.Skip(1))
                    .Select(item => item.Index)
                    .ToHashSet();
                removedDuplicates += duplicateIndices.Count;
                notes = notes.Where((_, index) => !duplicateIndices.Contains(index)).ToList();
            }

            if (options.HasFlag(ScoreCleanupOptions.RemoveVeryShortNotes))
            {
                var before = notes.Count;
                notes = notes.Where(note =>
                    Math.Max(1, note.RhythmTick ?? note.DurationTick) >= veryShortThreshold).ToList();
                removedVeryShort += before - notes.Count;
            }

            if (options.HasFlag(ScoreCleanupOptions.TrimSamePitchOverlaps))
            {
                var indexed = notes.Select((note, index) => (Note: note, Index: index)).ToArray();
                foreach (var pitchGroup in indexed.GroupBy(item => item.Note.Pitch))
                {
                    var ordered = pitchGroup
                        .OrderBy(item => item.Note.StartTick)
                        .ThenBy(item => item.Index)
                        .ToArray();
                    for (var index = 1; index < ordered.Length; index++)
                    {
                        var previousIndex = ordered[index - 1].Index;
                        var current = ordered[index].Note;
                        var previous = notes[previousIndex];
                        if (current.StartTick <= previous.StartTick)
                        {
                            continue;
                        }

                        var previousRhythm = Math.Max(1, previous.RhythmTick ?? previous.DurationTick);
                        var available = current.StartTick - previous.StartTick;
                        if (available >= previousRhythm)
                        {
                            continue;
                        }

                        var trimmed = previous with { RhythmTick = available };
                        trimmed = trimmed.DurationMode == DurationMode.Auto
                            ? trimmed with
                            {
                                DurationTick = NoteDurationCalculator.ResolveDuration(
                                    trimmed,
                                    null,
                                    score.Timing.Ppq),
                            }
                            : trimmed with
                            {
                                DurationTick = Math.Max(1, Math.Min(trimmed.DurationTick, available)),
                            };
                        notes[previousIndex] = trimmed;
                        trimmedOverlaps++;
                    }
                }
            }

            return track with { Notes = notes };
        }).ToList();

        var cleaned = removedDuplicates + trimmedOverlaps + removedVeryShort > 0
            ? score with { Tracks = tracks }
            : score;
        return new ScoreCleanupResult(
            cleaned,
            removedDuplicates,
            trimmedOverlaps,
            removedVeryShort);
    }
}
