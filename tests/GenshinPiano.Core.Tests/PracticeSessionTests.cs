using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class PracticeSessionTests
{
    [Fact]
    public void FollowMode_ResumesPartialChordAndIgnoresDuplicateHits()
    {
        var session = CreateSession();
        session.Start();
        Assert.Equal(PracticeKeyResult.Hit, session.Accept(GenshinKey.A));
        Assert.True(session.Pause());
        Assert.Equal(PracticeKeyResult.Ignored, session.Accept(GenshinKey.S));

        session.Start();
        Assert.Equal(PracticeKeyResult.Ignored, session.Accept(GenshinKey.A));
        Assert.Equal(PracticeKeyResult.Hit, session.Accept(GenshinKey.S));

        Assert.Equal(480, session.CurrentStep!.Tick);
        Assert.Equal(1, session.Combo);
        Assert.Equal(2, session.Hits);
        Assert.Equal(2, session.Attempts);
    }

    [Fact]
    public void ChangingMode_ClearsPartialChordAndDoesNotChangeSelectedStep()
    {
        var clock = new ManualClock();
        var session = CreateSession(clock);
        session.Start();
        session.Accept(GenshinKey.A);
        session.SetTimedMode(true);
        Assert.False(session.IsRunning);
        Assert.Equal(0, session.Index);

        session.Start();
        clock.Advance(PracticeSession.PreRoll);
        session.Accept(GenshinKey.S);
        Assert.Equal(0, session.Index);
        session.Accept(GenshinKey.A);
        Assert.Equal(1, session.Index);
    }

    [Fact]
    public void Bookmark_ReturnsToSelectedStepWithoutStartingAndIsClearedOnReload()
    {
        var session = CreateSession();
        session.Bookmark = 1;
        session.Start();
        session.Accept(GenshinKey.A);

        Assert.True(session.ReturnToBookmark());
        Assert.False(session.IsRunning);
        Assert.Equal(480, session.CurrentStep!.Tick);
        Assert.Equal(0, session.Attempts);

        session.Start();
        Assert.Equal(PracticeKeyResult.Hit, session.Accept(GenshinKey.D));
        Assert.True(session.IsComplete);

        session.LoadScore(ScoreDocument.CreateEmpty());
        Assert.False(session.ReturnToBookmark());
        Assert.Empty(session.Steps);
    }

    [Fact]
    public void TimedMode_OnlyUnplayedKeysCountAsMissedWhenChordExpires()
    {
        var clock = new ManualClock();
        var session = CreateSession(clock);
        session.SetTimedMode(true);
        session.Start();
        clock.Advance(PracticeSession.PreRoll);
        session.Accept(GenshinKey.A);

        clock.Advance(TimeSpan.FromMilliseconds(181));
        session.AdvanceClock();
        Assert.Equal(1, session.Index);
        Assert.Equal(2, session.Attempts);
        Assert.Equal(1, session.Hits);

        clock.Advance(TimeSpan.FromSeconds(1));
        session.AdvanceClock();
        Assert.True(session.IsComplete);
        Assert.False(session.IsRunning);
        Assert.Equal(3, session.Attempts);
    }

    [Fact]
    public void RhythmGame_SpeedChangePreservesPositionAndAppliesInputDelay()
    {
        var clock = new ManualClock();
        var session = CreateSession(clock);
        session.InputDelayMs = 100;
        session.SetRhythmGame(true);
        session.Start();
        clock.Advance(PracticeSession.PreRoll);

        var position = session.Position;
        session.SetSpeed(.5);
        Assert.Equal(position, session.Position);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(TimeSpan.FromMilliseconds(100), session.Position);
        Assert.Equal(PracticeKeyResult.Hit, session.Accept(GenshinKey.A));
        session.Accept(GenshinKey.S);
        Assert.Equal(1, session.Index);
    }

    private static PracticeSession CreateSession(TimeProvider? timeProvider = null)
    {
        var score = ScoreDocument.CreateEmpty();
        score.Tracks[0].Notes.AddRange(
        [
            new NoteEvent { Pitch = 60, DurationTick = 120 },
            new NoteEvent { Pitch = 62, DurationTick = 120 },
            new NoteEvent { Pitch = 64, StartTick = 480, DurationTick = 120 },
        ]);
        var session = new PracticeSession(timeProvider);
        session.LoadScore(score);
        return session;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
