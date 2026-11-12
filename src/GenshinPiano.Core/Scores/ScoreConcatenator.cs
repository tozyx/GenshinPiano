namespace GenshinPiano.Core.Scores;

public sealed record ScoreAppendResult(ScoreDocument Score, long StartTick, int AddedNotes);

/// <summary>Appends voices in track order using the destination's timing and playback settings.</summary>
public static class ScoreConcatenator
{
    public static ScoreAppendResult Append(ScoreDocument current, ScoreDocument incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        Validate(current);
        Validate(incoming);
        var offset = EndTick(current);
        var count = incoming.Tracks.Sum(track => track.Notes.Count);
        if (count == 0) return new(current, offset, 0);

        var tracks = current.Tracks.ToList();
        var ids = tracks.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
        long Scale(long tick) => checked((long)decimal.Round(
            (decimal)tick * current.Timing.Ppq / incoming.Timing.Ppq, 0, MidpointRounding.AwayFromZero));

        for (var index = 0; index < incoming.Tracks.Count; index++)
        {
            var source = incoming.Tracks[index];
            var notes = source.Notes.Select(note =>
            {
                var start = Scale(note.StartTick);
                long Span(long duration)
                {
                    var span = checked(Scale(checked(note.StartTick + duration)) - start);
                    if (span <= 0) throw new InvalidDataException("A note is too short for the destination PPQ.");
                    _ = checked(offset + start + span);
                    return span;
                }
                return note with
                {
                    Id = Guid.NewGuid(),
                    StartTick = checked(offset + start),
                    DurationTick = Span(note.DurationTick),
                    RhythmTick = note.RhythmTick is { } rhythm ? Span(rhythm) : null,
                };
            }).OrderBy(note => note.StartTick).ThenBy(note => note.Pitch).ToList();
            if (index < tracks.Count)
            {
                var target = tracks[index];
                // Keep the old final auto note from stretching across a leading rest.
                var lastStart = target.Notes.Count == 0 ? -1 : target.Notes.Max(note => note.StartTick);
                var existing = target.Notes.Select(note => note.StartTick == lastStart &&
                    note.DurationMode == DurationMode.Auto && note.RhythmTick is null
                        ? note with { RhythmTick = current.Timing.Ppq } : note);
                tracks[index] = target with { Notes = [.. existing, .. notes] };
            }
            else
            {
                var id = source.Id;
                while (!ids.Add(id)) id = Guid.NewGuid().ToString("N");
                tracks.Add(source with { Id = id, Notes = notes });
            }
        }

        return new(current with { Tracks = tracks }, offset, count);
    }

    private static long EndTick(ScoreDocument score)
    {
        var end = 0L;
        foreach (var track in score.Tracks)
        {
            var groups = track.Notes.GroupBy(note => note.StartTick).OrderBy(group => group.Key).ToArray();
            for (var index = 0; index < groups.Length; index++)
            {
                long? nextStart = index + 1 < groups.Length ? groups[index + 1].Key : null;
                foreach (var note in groups[index])
                {
                    var rhythm = note.RhythmTick ?? (note.DurationMode == DurationMode.Auto
                        ? (nextStart - note.StartTick) ?? score.Timing.Ppq : note.DurationTick);
                    var held = NoteDurationCalculator.ResolveDuration(note, nextStart, score.Timing.Ppq);
                    end = Math.Max(end, checked(note.StartTick + Math.Max(rhythm, held)));
                }
            }
        }
        return end;
    }

    private static void Validate(ScoreDocument score)
    {
        var errors = ScoreValidator.Validate(score);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }
}
