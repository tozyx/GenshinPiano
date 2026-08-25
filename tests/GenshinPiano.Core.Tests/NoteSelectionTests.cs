using GenshinPiano.Application.Editing;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class NoteSelectionTests
{
    [Fact]
    public void Reconcile_RemovesMissingNotesAndKeepsAValidPrimaryNote()
    {
        var first = new NoteEvent();
        var removed = new NoteEvent();
        var selection = new NoteSelection();
        selection.ReplaceWith([first.Id, removed.Id], removed.Id);
        var score = ScoreDocument.CreateEmpty("Test") with
        {
            Tracks = [new ScoreTrack { Id = "main", Notes = [first] }],
        };

        Assert.True(selection.Reconcile(score));
        Assert.Single(selection.Ids);
        Assert.Equal(first.Id, selection.PrimaryId);
    }
}
