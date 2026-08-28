using GenshinPiano.Application.Editing;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.ViewModels;

public sealed class PianoRollViewModel
{
    public ScoreEditHistory History { get; } = new();

    public NoteSelection Selection { get; } = new();

    public ScoreDocument? Score { get; private set; }

    public bool CanUndo => History.CanUndo;

    public bool CanRedo => History.CanRedo;

    public bool LoadScore(ScoreDocument? score)
    {
        Score = score;
        History.Clear();
        if (score is null)
        {
            return Selection.Clear();
        }

        return Selection.Reconcile(score);
    }

    public bool SynchronizeScore(ScoreDocument score)
    {
        Score = score;
        return Selection.Reconcile(score);
    }

    public bool Commit(ScoreDocument updatedScore)
    {
        if (Score is null || !History.TryCommit(Score, updatedScore, out var result))
        {
            return false;
        }

        Score = result;
        Selection.Reconcile(result);
        return true;
    }

    public bool Undo()
    {
        if (Score is null || !History.TryUndo(Score, out var result))
        {
            return false;
        }

        Score = result;
        Selection.Reconcile(result);
        return true;
    }

    public bool Redo()
    {
        if (Score is null || !History.TryRedo(Score, out var result))
        {
            return false;
        }

        Score = result;
        Selection.Reconcile(result);
        return true;
    }

    public IReadOnlyList<NoteEvent> GetSelectedNotes() => Score?.Tracks
        .SelectMany(track => track.Notes)
        .Where(note => Selection.Contains(note.Id))
        .ToArray() ?? [];

    public bool AddNote(NoteEvent note) =>
        Score is not null && Commit(ScoreEditor.AddNote(Score, note));

    public bool AddNotes(IReadOnlyCollection<NoteEvent> notes) =>
        Score is not null && Commit(ScoreEditor.AddNotes(Score, notes));

    public bool ReplaceNotes(IReadOnlyCollection<NoteEvent> replacements) =>
        Score is not null && Commit(ScoreEditor.ReplaceNotes(Score, replacements));

    public bool DeleteSelectedNotes()
    {
        if (Score is null || Selection.Count == 0)
        {
            return false;
        }

        var selectedIds = Selection.Ids.ToArray();
        Selection.Clear();
        return Commit(ScoreEditor.DeleteNotes(Score, selectedIds));
    }

    public bool SetSelectedArticulation(NoteArticulation articulation)
    {
        var notes = GetSelectedNotes();
        return notes.Count > 0 && ReplaceNotes(notes.Select(note => note with
        {
            DurationMode = DurationMode.Auto,
            Articulation = articulation,
            GateRatio = NoteDurationCalculator.GetGateRatio(articulation),
        }).ToArray());
    }

    public bool UpdateSelectedRhythm(long rhythmTick)
    {
        var notes = GetSelectedNotes();
        return notes.Count > 0 && ReplaceNotes(notes.Select(note => note with
        {
            RhythmTick = rhythmTick,
            DurationMode = DurationMode.Auto,
            DurationTick = Math.Max(1, (long)Math.Round(rhythmTick * ResolveGateRatio(note))),
        }).ToArray());
    }

    public bool UpdateSelectedGateRatio(double gateRatio, NoteArticulation articulation)
    {
        var notes = GetSelectedNotes();
        return notes.Count > 0 && ReplaceNotes(notes.Select(note =>
        {
            var rhythmTick = Math.Max(1, note.RhythmTick ?? note.DurationTick);
            return note with
            {
                RhythmTick = rhythmTick,
                DurationTick = Math.Max(1, (long)Math.Round(rhythmTick * gateRatio)),
                DurationMode = DurationMode.Auto,
                Articulation = articulation,
                GateRatio = gateRatio,
            };
        }).ToArray());
    }

    public bool ShiftSelectedRhythms(int step, IReadOnlyList<double> rhythmFactors)
    {
        if (Score is null || step == 0 || rhythmFactors.Count == 0)
        {
            return false;
        }

        var ppq = Score.Timing.Ppq;
        var changed = false;
        var replacements = GetSelectedNotes().Select(note =>
        {
            var currentTick = Math.Max(1, note.RhythmTick ?? note.DurationTick);
            var currentIndex = Enumerable.Range(0, rhythmFactors.Count)
                .MinBy(index => Math.Abs(FactorToTick(rhythmFactors[index], ppq) - currentTick));
            var nextIndex = Math.Clamp(currentIndex + step, 0, rhythmFactors.Count - 1);
            if (nextIndex == currentIndex)
            {
                return note;
            }

            changed = true;
            var rhythmTick = FactorToTick(rhythmFactors[nextIndex], ppq);
            var gateRatio = ResolveGateRatio(note);
            return note with
            {
                RhythmTick = rhythmTick,
                DurationTick = Math.Max(1, (long)Math.Round(rhythmTick * gateRatio)),
                DurationMode = DurationMode.Auto,
                GateRatio = gateRatio,
            };
        }).ToArray();
        return changed && ReplaceNotes(replacements);
    }

    private static double ResolveGateRatio(NoteEvent note)
    {
        if (note.GateRatio is double ratio)
        {
            return Math.Clamp(
                ratio,
                NoteDurationCalculator.MinimumGateRatio,
                NoteDurationCalculator.MaximumGateRatio);
        }

        var rhythmTick = Math.Max(1, note.RhythmTick ?? note.DurationTick);
        return Math.Clamp(
            note.DurationTick / (double)rhythmTick,
            NoteDurationCalculator.MinimumGateRatio,
            NoteDurationCalculator.MaximumGateRatio);
    }

    private static long FactorToTick(double factor, int ppq) =>
        Math.Max(1, checked((long)Math.Round(ppq * factor)));

    public int OptimizeAllNoteDurations()
    {
        if (Score is null)
        {
            return 0;
        }

        var noteCount = Score.Tracks.Sum(track => track.Notes.Count);
        return noteCount > 0 && Commit(NoteDurationCalculator.OptimizeAllDurations(Score))
            ? noteCount
            : 0;
    }

    public int GenerateShortPressDurations()
    {
        if (Score is null)
        {
            return 0;
        }

        var noteCount = Score.Tracks.Sum(track => track.Notes.Count);
        return noteCount > 0 && Commit(NoteDurationCalculator.GenerateShortPressDurations(Score))
            ? noteCount
            : 0;
    }

    public int ShiftAllNotesInGenshinRange(int keySteps)
    {
        if (Score is null || keySteps == 0)
        {
            return 0;
        }

        var noteCount = Score.Tracks.Sum(track => track.Notes.Count);
        return noteCount > 0 && Commit(ScoreEditor.ShiftAllNotesInGenshinRange(Score, keySteps))
            ? noteCount
            : 0;
    }

    public ScoreCleanupResult? ApplyScoreCleanup(ScoreCleanupOptions options)
    {
        if (Score is null || options == ScoreCleanupOptions.None)
        {
            return null;
        }

        var result = ScoreCleaner.Clean(Score, options);
        return result.TotalChanges > 0 && Commit(result.Score) ? result : null;
    }
}
