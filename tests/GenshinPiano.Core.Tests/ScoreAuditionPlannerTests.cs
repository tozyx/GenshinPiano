using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreAuditionPlannerTests
{
    [Fact]
    public void SetVolume_UsesMidiAsReferenceAndAttenuatesSamples()
    {
        var midi = new DelayedMidiOutput();
        var sample = new RecordingSampleOutput();
        var service = new ScoreAuditionService(midi, sample);

        service.SetVolume(127);
        Assert.Equal(127, midi.LastVolume);
        Assert.Equal(64, sample.LastVolume);

        service.SetVolume(0);
        Assert.Equal(0, midi.LastVolume);
        Assert.Equal(0, sample.LastVolume);
    }

    [Fact]
    public async Task PlayAsync_WaitsForMidiDeviceBeforeStartingNotes()
    {
        var output = new DelayedMidiOutput();
        var service = new ScoreAuditionService(output);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks = [new ScoreTrack { Notes = [new NoteEvent { Pitch = 60, DurationTick = 48 }] }],
        };

        var playback = service.PlayAsync(score, 0, 0, naturalSustain: false);
        Assert.False(playback.IsCompleted);
        Assert.Equal(0, output.NoteOnCount);

        output.FinishPreparation();
        await playback;

        Assert.Equal(1, output.NoteOnCount);
    }

    [Fact]
    public async Task PlayAsync_DispatchesFinalShortNoteAndPreservesSampleTail()
    {
        var midi = new DelayedMidiOutput();
        var sample = new RecordingSampleOutput();
        var service = new ScoreAuditionService(midi, sample);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks = [new ScoreTrack
            {
                Notes =
                [
                    new NoteEvent { Pitch = 60, StartTick = 0, DurationTick = 12 },
                    new NoteEvent { Pitch = 62, StartTick = 24, DurationTick = 12 },
                ],
            }],
        };

        await service.PlayAsync(score, 0, AuditionInstrumentIds.WindsongLyre, naturalSustain: false);

        Assert.Equal([60, 62], sample.Pitches);
        Assert.Equal(0, sample.AllNotesOffCount);
        Assert.Equal(1, sample.PrepareCount);
    }

    [Fact]
    public async Task PlayAsync_DenseRepeatedSampleNotesDispatchEveryAttack()
    {
        var sample = new RecordingSampleOutput();
        var service = new ScoreAuditionService(new DelayedMidiOutput(), sample);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks = [new ScoreTrack
            {
                Notes = Enumerable.Range(0, 80)
                    .Select(index => new NoteEvent
                    {
                        Pitch = 60,
                        StartTick = index * 2,
                        DurationTick = 1,
                    }).ToList(),
            }],
        };

        await service.PlayAsync(score, 0, AuditionInstrumentIds.WindsongLyre, naturalSustain: false);

        Assert.Equal(80, sample.Pitches.Count);
        Assert.Equal(1, sample.PrepareCount);
    }

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

    private sealed class DelayedMidiOutput : IMidiOutput
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int NoteOnCount { get; private set; }
        public int LastVolume { get; private set; }

        public Task PrepareAsync(int program, CancellationToken cancellationToken = default) =>
            _ready.Task.WaitAsync(cancellationToken);

        public void FinishPreparation() => _ready.SetResult();
        public void SetInstrument(int program) { }
        public void SetVolume(int volume) => LastVolume = volume;
        public void NoteOn(int pitch, int velocity) => NoteOnCount++;
        public void NoteOff(int pitch) { }
        public void AllNotesOff() { }
        public void Dispose() { }
    }

    private sealed class RecordingSampleOutput : ISampleAuditionOutput
    {
        public List<int> Pitches { get; } = [];
        public int AllNotesOffCount { get; private set; }
        public int PrepareCount { get; private set; }
        public int LastVolume { get; private set; }
        public Task PrepareAsync(int instrument, CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            return Task.CompletedTask;
        }
        public void SetVolume(int volume) => LastVolume = volume;
        public void NoteOn(int instrument, int pitch, int velocity) => Pitches.Add(pitch);
        public void NoteOff(int pitch) { }
        public void AllNotesOff() => AllNotesOffCount++;
    }
}
