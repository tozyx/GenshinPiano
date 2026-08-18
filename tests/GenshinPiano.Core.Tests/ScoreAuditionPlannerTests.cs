using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreAuditionPlannerTests
{
    [Fact]
    public void Create_PreservesMidiPitchAndResolvedDuration()
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
                            Pitch = 61,
                            Velocity = 91,
                            StartTick = 240,
                            DurationTick = 1,
                            RhythmTick = 480,
                            DurationMode = DurationMode.Auto,
                            Articulation = NoteArticulation.Natural,
                        },
                    ],
                },
            ],
        };

        var plan = ScoreAuditionPlanner.Create(score);

        Assert.Equal(2, plan.Events.Count);
        Assert.Equal(new MidiNoteValue(61, 91), Assert.Single(plan.Events[0].NotesOn));
        Assert.Equal(624, plan.DurationTick);
        Assert.Equal(61, Assert.Single(plan.Events[1].NotesOff));
        Assert.Equal(TimeSpan.FromMilliseconds(650), plan.Duration);
    }

    [Fact]
    public void Create_UsesTempoMapForPlaybackOffsets()
    {
        var score = ScoreDocument.CreateEmpty() with
        {
            Timing = new TimingDefinition
            {
                Ppq = 480,
                TempoMap = [new TempoChange { Tick = 0, Bpm = 60 }],
            },
            Tracks =
            [
                new ScoreTrack
                {
                    Notes = [new NoteEvent { Pitch = 60, DurationTick = 480 }],
                },
            ],
        };

        var plan = ScoreAuditionPlanner.Create(score);

        Assert.Equal(TimeSpan.FromSeconds(1), plan.Duration);
    }
}
