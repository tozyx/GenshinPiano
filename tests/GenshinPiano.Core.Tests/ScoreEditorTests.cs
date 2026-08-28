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

    [Fact]
    public void ShiftAllNotesInGenshinRange_PreservesTimingAndIdentity()
    {
        var note = new NoteEvent
        {
            Pitch = 60,
            StartTick = 240,
            DurationTick = 360,
            RhythmTick = 480,
        };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks = [new ScoreTrack { Id = "main", Notes = [note] }],
        };

        var transposed = ScoreEditor.ShiftAllNotesInGenshinRange(score, 7);
        var result = Assert.Single(transposed.Tracks[0].Notes);

        Assert.Equal(72, result.Pitch);
        Assert.Equal(note.Id, result.Id);
        Assert.Equal(note.StartTick, result.StartTick);
        Assert.Equal(note.DurationTick, result.DurationTick);
        Assert.Equal(note.RhythmTick, result.RhythmTick);
    }

    [Fact]
    public void ShiftAllNotesInGenshinRange_RejectsUnmappedOrOutOfRangePitch()
    {
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes = [new NoteEvent { Pitch = 127, DurationTick = 480 }],
                },
            ],
        };

        Assert.Throws<InvalidOperationException>(
            () => ScoreEditor.ShiftAllNotesInGenshinRange(score, 1));
    }

    [Fact]
    public void QualityAnalyzer_ReportsMappingDuplicatesOverlapsAndShortNotes()
    {
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes =
                    [
                        new NoteEvent { Pitch = 61, DurationTick = 480, RhythmTick = 480 },
                        new NoteEvent { Pitch = 61, DurationTick = 480, RhythmTick = 480 },
                        new NoteEvent { Pitch = 61, StartTick = 240, DurationTick = 480, RhythmTick = 480 },
                        new NoteEvent { Pitch = 60, StartTick = 1000, DurationTick = 30, RhythmTick = 30 },
                    ],
                },
            ],
        };

        var report = ScoreQualityAnalyzer.Analyze(score);

        Assert.Equal(4, report.TotalNotes);
        Assert.Equal(3, report.UnmappedNotes);
        Assert.Equal(1, report.DuplicateNotes);
        Assert.Equal(1, report.OverlappingNotes);
        Assert.Equal(1, report.VeryShortNotes);
        Assert.False(report.CanShiftKeySteps(1));
    }
}
