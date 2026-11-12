using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Playback;

public enum PracticeKeyResult { Ignored, Hit, Wrong }

public sealed class PracticeSession(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan PreRoll = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ApproachLeadTime = TimeSpan.FromMilliseconds(1500);

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly HashSet<GenshinKey> _matched = [];
    private ScheduledKeyChange[] _steps = [];
    private int _ppq = 480;
    private long _startedAt;
    private TimeSpan _origin;

    public IReadOnlyList<ScheduledKeyChange> Steps => _steps;
    public int Index { get; private set; }
    public int Bookmark { get; set; } = -1;
    public int Combo { get; private set; }
    public int Hits { get; private set; }
    public int Attempts { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsTimed { get; private set; }
    public bool IsRhythmGame { get; private set; }
    public bool UsesClock => IsTimed || IsRhythmGame;
    public bool IsComplete => _steps.Length > 0 && Index >= _steps.Length;
    public double Speed { get; private set; } = 1;
    public double InputDelayMs { get; set; }
    public ScheduledKeyChange? CurrentStep => Index < _steps.Length ? _steps[Index] : null;
    public TimeSpan Position => IsRunning && UsesClock
        ? _origin + TimeSpan.FromTicks((long)(_clock.GetElapsedTime(_startedAt).Ticks * Speed))
        : CurrentStep?.Offset ?? TimeSpan.Zero;
    public double ApproachProgress => CurrentStep is { } step
        ? 1 - (step.Offset - Position).TotalMilliseconds / Math.Max(1, ApproachLeadTime.TotalMilliseconds * Speed)
        : 1;

    public void LoadScore(ScoreDocument? score)
    {
        Reset();
        Bookmark = -1;
        _steps = [];
        _ppq = score?.Timing.Ppq ?? 480;
        if (score is not null)
            _steps = ScorePlaybackPlanner.Create(score).Events.Where(step => step.KeysDown.Count > 0).ToArray();
    }

    public void Start()
    {
        if (IsRunning || CurrentStep is not { } step) return;
        _origin = step.Offset - PreRoll;
        _startedAt = _clock.GetTimestamp();
        IsPaused = false;
        IsRunning = true;
    }

    public bool Pause()
    {
        if (!IsRunning) return false;
        IsRunning = false;
        IsPaused = true;
        return true;
    }

    public void Reset()
    {
        IsRunning = IsPaused = false;
        Index = Combo = Hits = Attempts = 0;
        _matched.Clear();
    }

    public bool ReturnToBookmark()
    {
        if (_steps.Length == 0 || Bookmark < 0) return false;
        Reset();
        Index = Math.Min(Bookmark, _steps.Length - 1);
        return true;
    }

    public void SetTimedMode(bool timed)
    {
        Pause();
        IsTimed = timed;
        IsPaused = false;
        _matched.Clear();
    }

    public void SetRhythmGame(bool enabled)
    {
        Pause();
        IsRhythmGame = enabled;
    }

    public void SetSpeed(double speed)
    {
        if (IsRunning && UsesClock)
        {
            _origin = Position;
            _startedAt = _clock.GetTimestamp();
        }
        Speed = speed;
    }

    public PracticeKeyResult Accept(GenshinKey key)
    {
        if (!IsRunning) return PracticeKeyResult.Ignored;
        var judgmentPosition = UsesClock ? JudgmentPosition() : TimeSpan.Zero;
        if (UsesClock) ExpireNotes(judgmentPosition);
        if (CurrentStep is not { } step) return PracticeKeyResult.Ignored;

        if (UsesClock && !PracticeTiming.IsHit(judgmentPosition, step.Offset, Speed))
            return WrongKey();
        if (_matched.Contains(key)) return PracticeKeyResult.Ignored;
        if (!step.KeysDown.Contains(key)) return WrongKey();

        Attempts++;
        Hits++;
        _matched.Add(key);
        if (_matched.Count == step.KeysDown.Count)
        {
            Combo++;
            Advance();
        }
        return PracticeKeyResult.Hit;
    }

    public void AdvanceClock()
    {
        if (IsRunning && UsesClock) ExpireNotes(JudgmentPosition());
    }

    public double GetTickAt(TimeSpan offset)
    {
        if (_steps.Length == 0) return 0;
        var lower = 0;
        var upper = _steps.Length;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (_steps[middle].Offset < offset) lower = middle + 1;
            else upper = middle;
        }

        if (upper == 0)
        {
            var slope = _steps.Length > 1
                ? (_steps[1].Tick - _steps[0].Tick) / Math.Max(0.001, (_steps[1].Offset - _steps[0].Offset).TotalSeconds)
                : _ppq * 2d;
            return _steps[0].Tick + (offset - _steps[0].Offset).TotalSeconds * slope;
        }
        if (upper == _steps.Length) return _steps[^1].Tick;
        var left = _steps[upper - 1];
        var right = _steps[upper];
        var ratio = (offset - left.Offset).TotalMilliseconds /
                    Math.Max(1, (right.Offset - left.Offset).TotalMilliseconds);
        return left.Tick + (right.Tick - left.Tick) * ratio;
    }

    private TimeSpan JudgmentPosition() => IsRhythmGame
        ? PracticeTiming.Compensate(Position, InputDelayMs, Speed)
        : Position;

    private void ExpireNotes(TimeSpan judgmentPosition)
    {
        while (CurrentStep is { } step && PracticeTiming.IsMissed(judgmentPosition, step.Offset, Speed))
        {
            Attempts += step.KeysDown.Count - _matched.Count;
            Combo = 0;
            Advance();
        }
    }

    private PracticeKeyResult WrongKey()
    {
        Attempts++;
        Combo = 0;
        return PracticeKeyResult.Wrong;
    }

    private void Advance()
    {
        _matched.Clear();
        Index++;
        if (IsComplete) IsRunning = false;
    }
}
