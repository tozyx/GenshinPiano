using GenshinPiano.App.ViewModels;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreConcatenatorTests
{
    private static ScoreDocument Score(int ppq = 480, params NoteEvent[] notes) => ScoreDocument.CreateEmpty("Original") with
    {
        Timing = new TimingDefinition { Ppq = ppq },
        Tracks = [new ScoreTrack { Id = "main", Name = "Melody", Notes = notes.ToList() }],
    };

    private static NoteEvent Note(long start = 0, long duration = 480, long? rhythm = null) => new()
    {
        Pitch = 60, StartTick = start, DurationTick = duration, RhythmTick = rhythm,
    };

    [Fact]
    public void AppendPreservesDestinationSettingsAndScalesIncomingPpq()
    {
        var original = Note(duration: 144, rhythm: 480) with { DurationMode = DurationMode.Auto, Articulation = NoteArticulation.Staccato };
        var current = Score(480, original) with
        {
            Metadata = new ScoreMetadata { Title = "My corrected score", Author = "Author" },
            Playback = new PlaybackSettings { Transpose = 2 },
            Timing = new TimingDefinition
            {
                Ppq = 480,
                TempoMap = [new() { Bpm = 80 }, new() { Tick = 240, Bpm = 90 }],
                TimeSignatures = [new() { Numerator = 3, Denominator = 4 }],
            },
        };
        var input = Note(240, 720, 960) with { Pitch = 67, Velocity = 111 };
        var incoming = Score(960, input);

        var result = ScoreConcatenator.Append(current, incoming);
        Assert.Equal(480, result.StartTick);
        Assert.Equal(1, result.AddedNotes);
        Assert.Same(current.Metadata, result.Score.Metadata);
        Assert.Same(current.Timing, result.Score.Timing);
        Assert.Same(current.Playback, result.Score.Playback);
        Assert.Same(original, result.Score.Tracks[0].Notes[0]);
        var added = result.Score.Tracks[0].Notes[1];
        Assert.Equal(600, added.StartTick); // Preserve the incoming leading rest.
        Assert.Equal(360, added.DurationTick);
        Assert.Equal(480, added.RhythmTick);
        Assert.Equal(67, added.Pitch);
        Assert.Equal(111, added.Velocity);
        Assert.NotEqual(input.Id, added.Id);
        Assert.Single(current.Tracks[0].Notes);
        Assert.Equal(240, incoming.Tracks[0].Notes[0].StartTick);
        Assert.Empty(ScoreValidator.Validate(result.Score));
    }

    [Theory]
    [InlineData(144L, 480L, 480L)]
    [InlineData(600L, 480L, 600L)]
    [InlineData(120L, null, 120L)]
    public void AppendUsesFullRhythmOrLongerHeldDuration(long duration, long? rhythm, long expected)
    {
        var result = ScoreConcatenator.Append(Score(480, Note(duration: duration, rhythm: rhythm)), Score(480, Note()));
        Assert.Equal(expected, result.StartTick);
        Assert.Equal(expected, result.Score.Tracks[0].Notes[1].StartTick);
    }

    [Fact]
    public void AppendUsesLastEndAcrossAllTracks_AndAddsOnlyExtraVoices()
    {
        var current = Score(480, Note()) with
        {
            Tracks = [new() { Id = "main", Notes = [Note()] }, new() { Id = "bass", IsMuted = true, Notes = [Note(960)] }],
        };
        var incoming = Score() with
        {
            Tracks = [new() { Id = "top", Notes = [Note()] }, new() { Id = "middle", Notes = [Note(240)] },
                new() { Id = "main", Notes = [Note(480)] }],
        };
        var result = ScoreConcatenator.Append(current, incoming);
        Assert.Equal(1440, result.StartTick);
        Assert.Equal(3, result.Score.Tracks.Count);
        Assert.Equal(2, result.Score.Tracks[0].Notes.Count);
        Assert.Equal(2, result.Score.Tracks[1].Notes.Count);
        Assert.True(result.Score.Tracks[1].IsMuted);
        Assert.NotEqual("main", result.Score.Tracks[2].Id);
        var second = ScoreConcatenator.Append(result.Score, incoming);
        Assert.Equal(3, second.Score.Tracks.Count);
        var ids = second.Score.Tracks.SelectMany(track => track.Notes).Select(note => note.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Empty(ScoreValidator.Validate(second.Score));
    }

    [Fact]
    public void LastImplicitAutoNoteDoesNotStretchAcrossIncomingLeadingRest()
    {
        var original = Note(duration: 384) with { DurationMode = DurationMode.Auto };
        var result = ScoreConcatenator.Append(Score(480, original), Score(480, Note(240)));
        Assert.Equal(480, result.StartTick);
        Assert.Equal(720, result.Score.Tracks[0].Notes[1].StartTick);
        var previous = result.Score.Tracks[0].Notes[0];
        Assert.Equal(480, previous.RhythmTick);
        Assert.Equal(384, NoteDurationCalculator.ResolveDuration(previous, 720, 480));
        Assert.Null(original.RhythmTick);
    }

    [Fact]
    public void EarlierImplicitAutoNoteUsesItsNextOnsetForEndCalculation()
    {
        var current = Score(480, Note(duration: 384) with { DurationMode = DurationMode.Auto }, Note(100, 1));
        Assert.Equal(101, ScoreConcatenator.Append(current, Score(480, Note())).StartTick);
    }

    [Fact]
    public void RoundingSharedBoundariesPreservesContiguousNotes()
    {
        var result = ScoreConcatenator.Append(Score(96), Score(480, Note(1, 4), Note(5, 5)));
        var notes = result.Score.Tracks[0].Notes;
        Assert.Equal(0, notes[0].StartTick);
        Assert.Equal(1, notes[0].DurationTick);
        Assert.Equal(notes[0].StartTick + notes[0].DurationTick, notes[1].StartTick);
        Assert.Equal(1, notes[1].DurationTick);
    }

    [Fact]
    public void EmptyDestinationStartsAtZero_EmptyIncomingIsNoOp()
    {
        var empty = Score();
        var incoming = Score(480, Note(240));
        var result = ScoreConcatenator.Append(empty, incoming);
        Assert.Equal(0, result.StartTick);
        Assert.Equal(240, result.Score.Tracks[0].Notes[0].StartTick);
        var unchanged = ScoreConcatenator.Append(incoming, empty);
        Assert.Same(incoming, unchanged.Score);
        Assert.Equal(0, unchanged.AddedNotes);
    }

    [Fact]
    public void InvalidUnrepresentableAndOverflowingInputCannotMutateCurrentScore()
    {
        var current = Score(96, Note());
        var original = current.Tracks[0].Notes[0];
        Assert.Throws<InvalidDataException>(() => ScoreConcatenator.Append(current, Score(480, Note(4, 1))));
        Assert.Throws<InvalidDataException>(() => ScoreConcatenator.Append(current, Score(0, Note())));
        Assert.Throws<OverflowException>(() => ScoreConcatenator.Append(Score(480, Note(long.MaxValue - 5, 5)), Score(480, Note())));
        Assert.Same(original, Assert.Single(current.Tracks[0].Notes));
    }

    [Fact]
    public void AppendIsOneUndoableEdit_AndEmptyInputDoesNotCreateHistory()
    {
        var original = Score(480, Note());
        var editor = new PianoRollViewModel();
        editor.LoadScore(original);
        Assert.Null(editor.AppendScore(Score()));
        Assert.False(editor.CanUndo);
        var result = editor.AppendScore(Score(480, Note()));
        Assert.NotNull(result);
        Assert.Same(result.Score, editor.Score);
        Assert.True(editor.Undo());
        Assert.Same(original, editor.Score);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(result.Score, editor.Score);
    }
}
