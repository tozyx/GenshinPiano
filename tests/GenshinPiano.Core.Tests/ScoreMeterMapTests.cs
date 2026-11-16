using GenshinPiano.App.ViewModels;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreMeterMapTests
{
    [Theory]
    [InlineData(4, 4, 0, 1, 1)]
    [InlineData(4, 4, 479, 1, 1)]
    [InlineData(4, 4, 480, 1, 2)]
    [InlineData(4, 4, 1919, 1, 4)]
    [InlineData(4, 4, 1920, 2, 1)]
    [InlineData(3, 4, 1440, 2, 1)]
    [InlineData(6, 8, 240, 1, 2)]
    [InlineData(6, 8, 1200, 1, 6)]
    [InlineData(6, 8, 1440, 2, 1)]
    [InlineData(3, 8, 720, 2, 1)]
    public void Positions_UseDenominatorBeatsAndCorrectMeasureLengths(int numerator, int denominator, long tick, long measure, int beat)
    {
        var map = Map(new TimeSignatureChange { Numerator = numerator, Denominator = denominator });
        Assert.Equal(new MeterPosition(measure, beat), map.GetPosition(tick));
    }

    [Fact]
    public void MeterChanges_RenumberBarsContinuouslyAndOwnTheirBoundary()
    {
        var map = Map(new(), new() { Tick = 3840, Numerator = 3, Denominator = 4 },
            new() { Tick = 6720, Numerator = 6, Denominator = 8 });
        Assert.Equal(new MeterPosition(2, 4), map.GetPosition(3839));
        Assert.Equal(new MeterPosition(3, 1), map.GetPosition(3840));
        Assert.Equal(new MeterPosition(4, 1), map.GetPosition(5280));
        Assert.Equal(new MeterPosition(5, 1), map.GetPosition(6720));
        Assert.Equal(new MeterPosition(5, 2), map.GetPosition(6960));
        var lines = map.EnumerateMeasures(1920, 8160).ToArray();
        Assert.Equal(new decimal[] { 1920, 3840, 5280, 6720, 8160 }, lines.Select(line => line.Tick));
        Assert.Equal(new long[] { 2, 3, 4, 5, 6 }, lines.Select(line => line.Measure));
        Assert.Equal(new decimal[] { 3840, 6720 }, lines.Where(line => line.IsSignatureChange).Select(line => line.Tick));
        Assert.Equal(new MeterPosition(5, 1), map.GetPosition(6720));
    }

    [Fact]
    public void ChangeInsideMeasure_StartsNewBarWithoutDuplicatingBoundary()
    {
        var map = Map(new TimeSignatureChange { Tick = 480, Numerator = 3, Denominator = 4 });
        Assert.Equal(new MeterPosition(1, 1), map.GetPosition(479));
        Assert.Equal(new MeterPosition(2, 1), map.GetPosition(480));
        Assert.Equal(new MeterPosition(2, 3), map.GetPosition(1919));
        Assert.Equal(new MeterPosition(3, 1), map.GetPosition(1920));
        Assert.Equal(new decimal[] { 0, 480, 1920 }, map.EnumerateMeasures(0, 1920).Select(line => line.Tick));
        Assert.Single(map.EnumerateBeats(480, 480));
    }

    [Fact]
    public void MissingAndInvalidMeters_DefaultToFourFour_AndDuplicateTicksUseLastValue()
    {
        var empty = Map();
        Assert.Equal(1920m, empty.GetMeasureLength(0));
        Assert.Equal(new MeterPosition(1, 1), empty.GetPosition(-1));
        var map = Map(new() { Tick = 1920, Numerator = 3, Denominator = 4 },
            new() { Numerator = 3, Denominator = 4 }, new() { Numerator = 6, Denominator = 8 },
            new() { Tick = 0, Numerator = 0, Denominator = 4 }, new() { Tick = 1, Numerator = 3, Denominator = 3 });
        Assert.Equal(6, map.GetSignature(0).Numerator);
        Assert.Equal(8, map.GetSignature(0).Denominator);
        Assert.Equal(3, map.GetSignature(1920).Numerator);
    }

    [Fact]
    public void FractionalTickBeatLengths_DoNotAccumulateRoundingErrors()
    {
        var map = new ScoreMeterMap(new TimingDefinition
        {
            Ppq = 25, TimeSignatures = [new() { Numerator = 3, Denominator = 8 }],
        });
        Assert.Equal(new decimal[] { 0, 37.5m, 75 }, map.EnumerateMeasures(0, 100).Select(line => line.Tick));
        Assert.Equal(new MeterPosition(1, 3), map.GetPosition(37));
        Assert.Equal(new MeterPosition(2, 1), map.GetPosition(38));
        Assert.Equal(375000m, map.EnumerateMeasures(375000, 375000).Single().Tick);
    }

    [Fact]
    public void DistantViewportsAndTinyMeters_EnumerateOnlyVisibleLinesAtRequestedSpacing()
    {
        var map = Map(new TimeSignatureChange { Numerator = 1, Denominator = 1 << 30 });
        var lines = map.EnumerateMeasures(1_000_000_000, 1_000_001_000, 10).ToArray();
        Assert.InRange(lines.Length, 99, 101);
        Assert.All(lines, line => Assert.InRange(line.Tick, 1_000_000_000m, 1_000_001_000m));
        Assert.Empty(map.EnumerateMeasures(10, 9));
    }

    [Fact]
    public void EditingInitialMeter_PreservesNotesTempoAndLaterChanges_AndSupportsUndoRedo()
    {
        var score = ScoreDocument.CreateEmpty();
        score.Tracks[0].Notes.Add(new() { Pitch = 60, StartTick = 123, DurationTick = 456 });
        var laterChange = new TimeSignatureChange { Tick = 1920, Numerator = 5, Denominator = 4 };
        score = score with { Timing = score.Timing with { TimeSignatures = [new(), laterChange] } };
        var viewModel = new PianoRollViewModel();
        viewModel.LoadScore(score);
        Assert.True(viewModel.SetInitialTimeSignature(6, 8));
        var edited = viewModel.Score!;
        Assert.Same(score.Tracks, edited.Tracks);
        Assert.Same(score.Timing.TempoMap, edited.Timing.TempoMap);
        Assert.Equal(new TimeSignatureChange { Numerator = 6, Denominator = 8 }, edited.Timing.TimeSignatures[0]);
        Assert.Same(laterChange, edited.Timing.TimeSignatures[1]);
        Assert.False(viewModel.SetInitialTimeSignature(6, 8));
        Assert.False(viewModel.SetInitialTimeSignature(3, 3));
        Assert.True(viewModel.Undo());
        Assert.Same(score, viewModel.Score);
        Assert.True(viewModel.Redo());
        Assert.Same(edited, viewModel.Score);
    }

    private static ScoreMeterMap Map(params TimeSignatureChange[] signatures) =>
        new(new TimingDefinition { TimeSignatures = signatures.ToList() });
}
