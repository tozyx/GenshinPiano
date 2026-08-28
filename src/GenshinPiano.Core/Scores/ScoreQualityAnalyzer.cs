using GenshinPiano.Core.Playback;

namespace GenshinPiano.Core.Scores;

public sealed record ScoreQualityReport(
    int TotalNotes,
    int UnmappedNotes,
    int DuplicateNotes,
    int OverlappingNotes,
    int VeryShortNotes,
    int? MinimumKeyIndex,
    int? MaximumKeyIndex)
{
    public bool CanShiftKeySteps(int keySteps) =>
        TotalNotes > 0 &&
        UnmappedNotes == 0 &&
        MinimumKeyIndex + keySteps is >= 0 &&
        MaximumKeyIndex + keySteps is < 21;
}

public static class ScoreQualityAnalyzer
{
    public static ScoreQualityReport Analyze(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);
        var allNotes = score.Tracks.SelectMany(track => track.Notes).ToArray();
        var unmapped = allNotes.Count(note => !GenshinKeyMap.TryMapPitch(
            note.Pitch,
            score.Playback.Transpose,
            OutOfRangePolicy.Reject,
            out _));
        var duplicateCount = 0;
        var overlappingCount = 0;
        foreach (var track in score.Tracks)
        {
            duplicateCount += track.Notes
                .GroupBy(note => (note.Pitch, note.StartTick))
                .Sum(group => Math.Max(0, group.Count() - 1));

            foreach (var pitchGroup in track.Notes.GroupBy(note => note.Pitch))
            {
                long previousEnd = -1;
                long? previousStart = null;
                foreach (var note in pitchGroup.OrderBy(note => note.StartTick))
                {
                    if (previousStart != note.StartTick && note.StartTick < previousEnd)
                    {
                        overlappingCount++;
                    }

                    previousStart = note.StartTick;
                    var duration = Math.Max(1, note.RhythmTick ?? note.DurationTick);
                    var noteEnd = note.StartTick > long.MaxValue - duration
                        ? long.MaxValue
                        : note.StartTick + duration;
                    previousEnd = Math.Max(previousEnd, noteEnd);
                }
            }
        }

        var veryShortThreshold = Math.Max(1, score.Timing.Ppq / 8L);
        var veryShort = allNotes.Count(note =>
            Math.Max(1, note.RhythmTick ?? note.DurationTick) < veryShortThreshold);
        var keyIndices = allNotes
            .Select(note => GenshinKeyMap.TryGetKeyIndex(note.Pitch, out var index)
                ? (int?)index
                : null)
            .Where(index => index.HasValue)
            .Select(index => index!.Value)
            .ToArray();
        return new ScoreQualityReport(
            allNotes.Length,
            unmapped,
            duplicateCount,
            overlappingCount,
            veryShort,
            keyIndices.Length == 0 ? null : keyIndices.Min(),
            keyIndices.Length == 0 ? null : keyIndices.Max());
    }
}
