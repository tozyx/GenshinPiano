using GenshinPiano.Core.Playback;

namespace GenshinPiano.Core.Scores;

public static class ScoreEditor
{
    public static ScoreDocument ShiftAllNotesInGenshinRange(ScoreDocument score, int keySteps)
    {
        ArgumentNullException.ThrowIfNull(score);
        if (keySteps == 0)
        {
            return score;
        }

        var notes = score.Tracks.SelectMany(track => track.Notes).ToArray();
        if (notes.Any(note => !GenshinKeyMap.TryShiftPitch(note.Pitch, keySteps, out _)))
        {
            throw new InvalidOperationException("音域平移后将有音符超出原神 21 键范围，或曲谱包含无法映射的半音。" );
        }

        return score with
        {
            Tracks = score.Tracks.Select(track => track with
            {
                Notes = track.Notes.Select(note => note with
                {
                    Pitch = GenshinKeyMap.TryShiftPitch(note.Pitch, keySteps, out var shiftedPitch)
                        ? shiftedPitch
                        : note.Pitch,
                }).ToList(),
            }).ToList(),
        };
    }

    public static ScoreDocument AddNote(ScoreDocument score, NoteEvent note, int trackIndex = 0)
        => AddNotes(score, [note], trackIndex);

    public static ScoreDocument AddNotes(
        ScoreDocument score,
        IReadOnlyCollection<NoteEvent> notes,
        int trackIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(notes);
        var tracks = score.Tracks.ToList();
        if (tracks.Count == 0)
        {
            tracks.Add(new ScoreTrack { Id = "main", Name = "主音轨" });
        }

        if (trackIndex < 0 || trackIndex >= tracks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(trackIndex));
        }

        tracks[trackIndex] = tracks[trackIndex] with
        {
            Notes = [.. tracks[trackIndex].Notes, .. notes],
        };
        return NoteDurationCalculator.ApplyAutoDurations(score with { Tracks = tracks });
    }

    public static ScoreDocument ReplaceNote(ScoreDocument score, NoteEvent replacement)
        => ReplaceNotes(score, [replacement]);

    public static ScoreDocument ReplaceNotes(
        ScoreDocument score,
        IReadOnlyCollection<NoteEvent> replacements)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(replacements);
        var replacementsById = replacements.ToDictionary(note => note.Id);
        var replacedIds = new HashSet<Guid>();
        var tracks = score.Tracks.Select(track => track with
        {
            Notes = track.Notes.Select(note =>
            {
                if (!replacementsById.TryGetValue(note.Id, out var replacement))
                {
                    return note;
                }

                replacedIds.Add(note.Id);
                return replacement;
            }).ToList(),
        }).ToList();

        if (replacedIds.Count != replacementsById.Count)
        {
            throw new ArgumentException("One or more notes do not belong to this score.", nameof(replacements));
        }

        return NoteDurationCalculator.ApplyAutoDurations(score with { Tracks = tracks });
    }

    public static ScoreDocument DeleteNote(ScoreDocument score, Guid noteId)
        => DeleteNotes(score, [noteId]);

    public static ScoreDocument DeleteNotes(ScoreDocument score, IReadOnlyCollection<Guid> noteIds)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(noteIds);
        var ids = noteIds.ToHashSet();
        var tracks = score.Tracks.Select(track => track with
        {
            Notes = track.Notes.Where(note => !ids.Contains(note.Id)).ToList(),
        }).ToList();
        return NoteDurationCalculator.ApplyAutoDurations(score with { Tracks = tracks });
    }
}
