using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Editing;

public sealed class NoteSelection
{
    private readonly HashSet<Guid> _ids = [];

    public IReadOnlySet<Guid> Ids => _ids;

    public Guid? PrimaryId { get; private set; }

    public int Count => _ids.Count;

    public bool Contains(Guid id) => _ids.Contains(id);

    public bool Add(Guid id, bool makePrimary = false)
    {
        var changed = _ids.Add(id);
        if (makePrimary || PrimaryId is null)
        {
            PrimaryId = id;
        }

        return changed;
    }

    public bool Remove(Guid id)
    {
        var changed = _ids.Remove(id);
        if (PrimaryId == id)
        {
            PrimaryId = _ids.Count > 0 ? _ids.First() : null;
        }

        return changed;
    }

    public bool MakePrimary(Guid id)
    {
        if (!_ids.Contains(id) || PrimaryId == id)
        {
            return false;
        }

        PrimaryId = id;
        return true;
    }

    public bool Clear()
    {
        if (_ids.Count == 0 && PrimaryId is null)
        {
            return false;
        }

        _ids.Clear();
        PrimaryId = null;
        return true;
    }

    public void SetSingle(Guid id)
    {
        _ids.Clear();
        _ids.Add(id);
        PrimaryId = id;
    }

    public void ReplaceWith(IEnumerable<Guid> ids, Guid? primaryId = null)
    {
        _ids.Clear();
        _ids.UnionWith(ids);
        PrimaryId = primaryId is { } primary && _ids.Contains(primary)
            ? primary
            : _ids.Count > 0 ? _ids.First() : null;
    }

    public bool Reconcile(ScoreDocument score)
    {
        var existingIds = score.Tracks
            .SelectMany(track => track.Notes)
            .Select(note => note.Id)
            .ToHashSet();
        var changed = _ids.RemoveWhere(id => !existingIds.Contains(id)) > 0;
        if (PrimaryId is { } primary && !existingIds.Contains(primary))
        {
            PrimaryId = _ids.Count > 0 ? _ids.First() : null;
            changed = true;
        }

        return changed;
    }
}
