using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class AuditionTimelineTests
{
    [Fact]
    public void Interpolation_UsesNewTempoAfterBoundaryAndStopsAtLoopEnd()
    {
        var timeline = new AuditionTimeline(new TimingDefinition
        {
            Ppq = 480,
            TempoMap = [new TempoChange { Bpm = 120 }, new TempoChange { Tick = 480, Bpm = 60 }],
        });
        var progress = new AuditionProgress(384, 720, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(1));

        Assert.Equal((480L, TimeSpan.FromMilliseconds(500)), timeline.Interpolate(progress, TimeSpan.FromMilliseconds(100)));
        Assert.Equal((600L, TimeSpan.FromMilliseconds(750)), timeline.Interpolate(progress, TimeSpan.FromMilliseconds(350)));
        Assert.Equal((720L, TimeSpan.FromSeconds(1)), timeline.Interpolate(progress, TimeSpan.FromSeconds(2)));
    }
}
