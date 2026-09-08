using GenshinPiano.Core.Playback;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class PracticeTimingTests
{
    [Theory]
    [InlineData(.25)]
    [InlineData(.5)]
    [InlineData(1)]
    [InlineData(1.25)]
    public void EarlyAndLateBoundariesUseRealTimeAtEverySpeed(double speed)
    {
        var onset = TimeSpan.FromSeconds(2);
        foreach (var direction in new[] { -1, 1 })
        {
            Assert.True(PracticeTiming.IsHit(onset + TimeSpan.FromMilliseconds(direction * 180 * speed), onset, speed));
            Assert.False(PracticeTiming.IsHit(onset + TimeSpan.FromMilliseconds(direction * 181 * speed), onset, speed));
        }
        Assert.False(PracticeTiming.IsMissed(onset + TimeSpan.FromMilliseconds(180 * speed), onset, speed));
        Assert.True(PracticeTiming.IsMissed(onset + TimeSpan.FromMilliseconds(181 * speed), onset, speed));
    }

    [Theory]
    [InlineData(120, .25)]
    [InlineData(120, 1.25)]
    [InlineData(-120, 1)]
    public void CompensationCorrectsInputWithoutPrematureMiss(double offset, double speed)
    {
        var onset = TimeSpan.FromSeconds(3);
        var input = onset + TimeSpan.FromMilliseconds(offset * speed);
        var corrected = PracticeTiming.Compensate(input, offset, speed);
        Assert.Equal(onset, corrected);
        Assert.True(PracticeTiming.IsHit(corrected, onset, speed));
        Assert.False(PracticeTiming.IsMissed(corrected, onset, speed));
    }
}
