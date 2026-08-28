using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreCleanerTests
{
    [Fact]
    public void Clean_AppliesSelectedRepairsAndReportsCounts()
    {
        var retained = new NoteEvent
        {
            Pitch = 60,
            DurationTick = 480,
            RhythmTick = 480,
            DurationMode = DurationMode.Explicit,
        };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes =
                    [
                        retained,
                        retained with { Id = Guid.NewGuid(), DurationTick = 240 },
                        new NoteEvent
                        {
                            Pitch = 60,
                            StartTick = 240,
                            DurationTick = 480,
                            RhythmTick = 480,
                        },
                        new NoteEvent
                        {
                            Pitch = 62,
                            StartTick = 960,
                            DurationTick = 30,
                            RhythmTick = 30,
                        },
                    ],
                },
            ],
        };

        var result = ScoreCleaner.Clean(
            score,
            ScoreCleanupOptions.RemoveExactDuplicates |
            ScoreCleanupOptions.TrimSamePitchOverlaps |
            ScoreCleanupOptions.RemoveVeryShortNotes);

        Assert.Equal(1, result.RemovedDuplicates);
        Assert.Equal(1, result.TrimmedOverlaps);
        Assert.Equal(1, result.RemovedVeryShortNotes);
        Assert.Equal(3, result.TotalChanges);
        Assert.Equal(2, result.Score.Tracks[0].Notes.Count);
        var first = result.Score.Tracks[0].Notes[0];
        Assert.Equal(retained.Id, first.Id);
        Assert.Equal(240, first.RhythmTick);
        Assert.Equal(240, first.DurationTick);
    }

    [Fact]
    public void Clean_TrimOverlap_RecalculatesAutomaticHoldFromTrimmedRhythm()
    {
        var first = new NoteEvent
        {
            Pitch = 60,
            DurationTick = 384,
            RhythmTick = 480,
            DurationMode = DurationMode.Auto,
            Articulation = NoteArticulation.Natural,
        };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes =
                    [
                        first,
                        new NoteEvent
                        {
                            Pitch = 60,
                            StartTick = 300,
                            DurationTick = 384,
                            RhythmTick = 480,
                        },
                    ],
                },
            ],
        };

        var result = ScoreCleaner.Clean(score, ScoreCleanupOptions.TrimSamePitchOverlaps);

        Assert.Equal(1, result.TrimmedOverlaps);
        Assert.Equal(300, result.Score.Tracks[0].Notes[0].RhythmTick);
        Assert.Equal(240, result.Score.Tracks[0].Notes[0].DurationTick);
    }

    [Fact]
    public void Clean_NonePreservesOriginalDocument()
    {
        var score = ScoreDocument.CreateEmpty();

        var result = ScoreCleaner.Clean(score, ScoreCleanupOptions.None);

        Assert.Same(score, result.Score);
        Assert.Equal(0, result.TotalChanges);
    }
}
