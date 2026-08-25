using GenshinPiano.Application.Editing;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreEditHistoryTests
{
    [Fact]
    public void CommitUndoRedo_RestoresDocumentsInOrder()
    {
        var history = new ScoreEditHistory();
        var original = ScoreDocument.CreateEmpty("Original");
        var edited = original with { Metadata = original.Metadata with { Title = "Edited" } };

        Assert.True(history.TryCommit(original, edited, out var current));
        Assert.True(history.CanUndo);
        Assert.True(history.TryUndo(current, out current));
        Assert.Equal("Original", current.Metadata.Title);
        Assert.True(history.TryRedo(current, out current));
        Assert.Equal("Edited", current.Metadata.Title);
    }

    [Fact]
    public void NewCommit_ClearsRedoBranch()
    {
        var history = new ScoreEditHistory();
        var original = ScoreDocument.CreateEmpty("Original");
        var first = original with { Metadata = original.Metadata with { Title = "First" } };
        var second = original with { Metadata = original.Metadata with { Title = "Second" } };

        history.TryCommit(original, first, out var current);
        history.TryUndo(current, out current);
        history.TryCommit(current, second, out _);

        Assert.False(history.CanRedo);
    }
}
