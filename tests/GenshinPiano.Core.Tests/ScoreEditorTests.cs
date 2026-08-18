using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreEditorTests
{
    [Fact]
    public void AddNote_RecalculatesAdjacentAutomaticDurations()
    {
        var first = new NoteEvent
        {
            Pitch = 60,
            DurationTick = 384,
            DurationMode = DurationMode.Auto,
            Articulation = NoteArticulation.Natural,
        };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack { Id = "main", Notes = [first] },
            ],
        };
        var second = new NoteEvent
        {
            Pitch = 62,
            StartTick = 240,
            DurationTick = 384,
            DurationMode = DurationMode.Auto,
            Articulation = NoteArticulation.Natural,
        };

        var edited = ScoreEditor.AddNote(score, second);

        Assert.Equal(192, edited.Tracks[0].Notes[0].DurationTick);
        Assert.Equal(384, edited.Tracks[0].Notes[1].DurationTick);
    }

    [Fact]
    public void ReplaceNote_PreservesIdentityAndAppliesResize()
    {
        var original = new NoteEvent { Pitch = 60, DurationTick = 480 };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks = [new ScoreTrack { Id = "main", Notes = [original] }],
        };

        var edited = ScoreEditor.ReplaceNote(score, original with
        {
            DurationTick = 240,
            DurationMode = DurationMode.Explicit,
        });

        Assert.Equal(original.Id, edited.Tracks[0].Notes[0].Id);
        Assert.Equal(240, edited.Tracks[0].Notes[0].DurationTick);
        Assert.Equal(DurationMode.Explicit, edited.Tracks[0].Notes[0].DurationMode);
    }

    [Fact]
    public void DeleteNote_RemovesOnlySelectedIdentity()
    {
        var selected = new NoteEvent { Pitch = 60, DurationTick = 480 };
        var remaining = new NoteEvent { Pitch = 64, DurationTick = 480 };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks = [new ScoreTrack { Id = "main", Notes = [selected, remaining] }],
        };

        var edited = ScoreEditor.DeleteNote(score, selected.Id);

        Assert.Equal(remaining.Id, Assert.Single(edited.Tracks[0].Notes).Id);
    }

    [Fact]
    public void ReplaceNotes_UpdatesSelectionAsSingleEdit()
    {
        var first = new NoteEvent { Pitch = 60, DurationTick = 480 };
        var second = new NoteEvent { Pitch = 64, StartTick = 480, DurationTick = 480 };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks = [new ScoreTrack { Id = "main", Notes = [first, second] }],
        };

        var edited = ScoreEditor.ReplaceNotes(score,
        [
            first with { StartTick = 240 },
            second with { StartTick = 720 },
        ]);

        Assert.Equal([240L, 720L], edited.Tracks[0].Notes.Select(note => note.StartTick));
    }

    [Fact]
    public void AddAndDeleteNotes_HandleAGroup()
    {
        var score = ScoreDocument.CreateEmpty();
        var notes = new[]
        {
            new NoteEvent { Pitch = 60, DurationTick = 480 },
            new NoteEvent { Pitch = 64, DurationTick = 480 },
        };

        var added = ScoreEditor.AddNotes(score, notes);
        var deleted = ScoreEditor.DeleteNotes(added, notes.Select(note => note.Id).ToArray());

        Assert.Equal(2, added.Tracks[0].Notes.Count);
        Assert.Empty(deleted.Tracks[0].Notes);
    }
}
