using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Editing;

public sealed class ScoreEditHistory
{
    private readonly Stack<ScoreDocument> _undo = new();
    private readonly Stack<ScoreDocument> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public bool TryCommit(
        ScoreDocument current,
        ScoreDocument updated,
        out ScoreDocument result)
    {
        if (updated == current)
        {
            result = current;
            return false;
        }

        _undo.Push(current);
        _redo.Clear();
        result = updated;
        return true;
    }

    public bool TryUndo(ScoreDocument current, out ScoreDocument result)
    {
        if (!_undo.TryPop(out result!))
        {
            result = current;
            return false;
        }

        _redo.Push(current);
        return true;
    }

    public bool TryRedo(ScoreDocument current, out ScoreDocument result)
    {
        if (!_redo.TryPop(out result!))
        {
            result = current;
            return false;
        }

        _undo.Push(current);
        return true;
    }
}
