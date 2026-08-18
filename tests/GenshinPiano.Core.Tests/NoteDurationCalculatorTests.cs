using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class NoteDurationCalculatorTests
{
    [Fact]
    public void GenerateShortPressDurations_UsesRhythmDensityAndTempo()
    {
        var score = ScoreDocument.CreateEmpty("Short press") with
        {
            Timing = new TimingDefinition
            {
                Ppq = 500,
                TempoMap = [new TempoChange { Tick = 0, Bpm = 120 }],
            },
            Tracks =
            [
                new ScoreTrack
                {
                    Notes =
                    [
                        new NoteEvent { Pitch = 60, StartTick = 0, DurationTick = 400 },
                        new NoteEvent { Pitch = 62, StartTick = 125, DurationTick = 400 },
                        new NoteEvent { Pitch = 64, StartTick = 375, DurationTick = 400 },
                        new NoteEvent { Pitch = 65, StartTick = 875, DurationTick = 400 },
                    ],
                },
            ],
        };

        var shortened = NoteDurationCalculator.GenerateShortPressDurations(score);
        var notes = shortened.Tracks[0].Notes;

        Assert.Equal([20L, 25L, 30L, 30L], notes.Select(note => note.DurationTick));
        Assert.All(notes, note => Assert.Equal(DurationMode.Explicit, note.DurationMode));
        Assert.Equal(400, score.Tracks[0].Notes[0].DurationTick);
    }

    [Fact]
    public void OptimizeAllDurations_InfersRhythmFromNextOnsetAndPreservesArticulation()
    {
        var score = ScoreDocument.CreateEmpty("MIDI") with
        {
            Timing = new TimingDefinition { Ppq = 500 },
            Tracks =
            [
                new ScoreTrack
                {
                    Notes =
                    [
                        new NoteEvent
                        {
                            StartTick = 0,
                            DurationTick = 30,
                            DurationMode = DurationMode.Explicit,
                            Articulation = NoteArticulation.Natural,
                        },
                        new NoteEvent
                        {
                            StartTick = 250,
                            DurationTick = 20,
                            DurationMode = DurationMode.Explicit,
                            Articulation = NoteArticulation.Staccato,
                        },
                    ],
                },
            ],
        };

        var optimized = NoteDurationCalculator.OptimizeAllDurations(score);

        Assert.Collection(
            optimized.Tracks[0].Notes,
            note =>
            {
                Assert.Equal(250, note.RhythmTick);
                Assert.Equal(200, note.DurationTick);
                Assert.Equal(DurationMode.Auto, note.DurationMode);
            },
            note =>
            {
                Assert.Equal(500, note.RhythmTick);
                Assert.Equal(150, note.DurationTick);
                Assert.Equal(NoteArticulation.Staccato, note.Articulation);
            });
    }

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
