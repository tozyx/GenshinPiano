using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class NoteDurationCalculatorTests
{
    [Theory]
    [InlineData(NoteArticulation.Legato, 456)]
    [InlineData(NoteArticulation.Natural, 384)]
    [InlineData(NoteArticulation.Detached, 240)]
    [InlineData(NoteArticulation.Staccato, 144)]
    public void ResolveDuration_AppliesArticulationGate(
        NoteArticulation articulation,
        long expectedDuration)
    {
        var note = new NoteEvent
        {
            DurationTick = 1,
            RhythmTick = 480,
            DurationMode = DurationMode.Auto,
            Articulation = articulation,
        };

        var duration = NoteDurationCalculator.ResolveDuration(note, null, 480);

        Assert.Equal(expectedDuration, duration);
    }

    [Fact]
    public void ResolveDuration_InfersRhythmFromNextOnset()
    {
        var note = new NoteEvent
        {
            StartTick = 120,
            DurationTick = 1,
            DurationMode = DurationMode.Auto,
            Articulation = NoteArticulation.Natural,
        };

        Assert.Equal(288, NoteDurationCalculator.ResolveDuration(note, 480, 480));
    }

    [Fact]
    public void ResolveDuration_PreservesExplicitDurationForOlderFiles()
    {
        var note = new NoteEvent { DurationTick = 137 };

        Assert.Equal(137, NoteDurationCalculator.ResolveDuration(note, 480, 480));
    }

    [Fact]
    public void ResolveDuration_AppliesBoundedCustomGateRatio()
    {
        var note = new NoteEvent
        {
            DurationTick = 1,
            RhythmTick = 480,
            DurationMode = DurationMode.Auto,
            Articulation = NoteArticulation.Custom,
            GateRatio = 0.67,
        };

        Assert.Equal(322, NoteDurationCalculator.ResolveDuration(note, null, 480));
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0.96)]
    public void Validator_RejectsGateRatioOutsideEditorLimits(double gateRatio)
    {
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Notes =
                    [
                        new NoteEvent
                        {
                            DurationTick = 480,
                            RhythmTick = 480,
                            DurationMode = DurationMode.Auto,
                            Articulation = NoteArticulation.Custom,
                            GateRatio = gateRatio,
                        },
                    ],
                },
            ],
        };

        Assert.NotEmpty(ScoreValidator.Validate(score));
    }
}
