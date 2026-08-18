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

    [Fact]
    public void Create_NaturalSustainExtendsShortNotesWithoutChangingScore()
    {
        var shortNote = new NoteEvent { Pitch = 60, StartTick = 0, DurationTick = 24 };
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Notes =
                    [
                        shortNote,
                        new NoteEvent { Pitch = 62, StartTick = 240, DurationTick = 24 },
                    ],
                },
            ],
        };

        var plan = ScoreAuditionPlanner.Create(score, naturalSustain: true);

        Assert.Contains(plan.Events, item => item.Tick == 192 && item.NotesOff.Contains(60));
        Assert.Equal(24, shortNote.DurationTick);
    }

    [Fact]
    public void Create_NaturalSustainStopsBeforeRepeatedPitch()
    {
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Notes =
                    [
                        new NoteEvent { Pitch = 60, StartTick = 0, DurationTick = 960 },
                        new NoteEvent { Pitch = 60, StartTick = 120, DurationTick = 24 },
                    ],
                },
            ],
        };

        var plan = ScoreAuditionPlanner.Create(score, naturalSustain: true);

        var repeatedStart = Assert.Single(plan.Events, item => item.Tick == 120);
        Assert.Contains(60, repeatedStart.NotesOff);
        Assert.Contains(repeatedStart.NotesOn, note => note.Pitch == 60);
    }
}
