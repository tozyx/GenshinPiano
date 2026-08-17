using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreValidatorTests
{
    [Fact]
    public void EmptyScoreCreatedByFactoryIsValid()
    {
        var score = ScoreDocument.CreateEmpty();

        var errors = ScoreValidator.Validate(score);

        Assert.Empty(errors);
    }

    [Fact]
    public void NoteOutsideMidiRangeIsRejected()
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
                        new NoteEvent
                        {
                            Pitch = 128,
                            StartTick = 0,
                            DurationTick = 480,
                        },
                    ],
                },
            ],
        };

        var errors = ScoreValidator.Validate(score);

        Assert.Contains(errors, error => error.Contains("MIDI 音高", StringComparison.Ordinal));
    }
}
