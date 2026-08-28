using System.Collections.Concurrent;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScorePlaybackPlannerTests
{
    [Fact]
    public void Create_GroupsChordAndMapsPitches()
    {
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes =
                    [
                        new NoteEvent { Pitch = 60, StartTick = 0, DurationTick = 480 },
                        new NoteEvent { Pitch = 64, StartTick = 0, DurationTick = 480 },
                        new NoteEvent { Pitch = 72, StartTick = 480, DurationTick = 480 },
                    ],
                },
            ],
        };

        var plan = ScorePlaybackPlanner.Create(score);

        Assert.Equal(3, plan.Events.Count);
        Assert.Equal([GenshinKey.A, GenshinKey.D], plan.Events[0].KeysDown);
        Assert.Equal([GenshinKey.A, GenshinKey.D], plan.Events[1].KeysUp);
        Assert.Equal(GenshinKey.Q, Assert.Single(plan.Events[1].KeysDown));
        Assert.Equal(TimeSpan.FromMilliseconds(500), plan.Events[1].Offset);
        Assert.Equal(960, plan.Events[2].Tick);
        Assert.Equal(GenshinKey.Q, Assert.Single(plan.Events[2].KeysUp));
    }

    [Fact]
    public void TickToTime_IntegratesTempoChanges()
    {
        var timing = new TimingDefinition
        {
            Ppq = 480,
            TempoMap =
            [
                new TempoChange { Tick = 0, Bpm = 120 },
                new TempoChange { Tick = 480, Bpm = 60 },
            ],
        };

        var result = ScorePlaybackPlanner.TickToTime(960, timing);

        Assert.Equal(TimeSpan.FromMilliseconds(1500), result);
    }

    [Fact]
    public void Create_OctaveFoldsOutOfRangeNaturalPitch()
    {
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes = [new NoteEvent { Pitch = 36, DurationTick = 480 }],
                },
            ],
        };

        var plan = ScorePlaybackPlanner.Create(score);

        Assert.Equal(GenshinKey.Z, Assert.Single(plan.Events[0].KeysDown));
    }

    [Fact]
    public async Task PlaybackService_PressesAndReleasesPlannedChord()
    {
        var keyboard = new RecordingKeyboardInput();
        var service = new ScorePlaybackService(keyboard);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes =
                    [
                        new NoteEvent { Pitch = 60, DurationTick = 24 },
                        new NoteEvent { Pitch = 64, DurationTick = 24 },
                    ],
                },
            ],
        };

        await service.PlayAsync(score, countdownSeconds: 0);

        Assert.Equal(2, keyboard.Events.Count);
        Assert.Equal("down:A,D", keyboard.Events[0]);
        Assert.Equal("up:A,D", keyboard.Events[1]);
    }

    [Fact]
    public async Task PlaybackService_AlwaysInvokesKeyboardSafetyReleaseAfterCompletion()
    {
        var keyboard = new SafetyRecordingKeyboardInput();
        var service = new ScorePlaybackService(keyboard);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes = [new NoteEvent { Pitch = 60, DurationTick = 24 }],
                },
            ],
        };

        await service.PlayAsync(score, countdownSeconds: 0);

        Assert.Equal(1, keyboard.SafetyReleaseCount);
        Assert.Empty(keyboard.PressedKeys);
    }

    [Fact]
    public async Task PlaybackService_InvokesKeyboardSafetyReleaseWhenInputThrows()
    {
        var keyboard = new SafetyRecordingKeyboardInput { ThrowOnKeyDown = true };
        var service = new ScorePlaybackService(keyboard);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes = [new NoteEvent { Pitch = 60, DurationTick = 24 }],
                },
            ],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PlayAsync(score, countdownSeconds: 0));

        Assert.Equal(1, keyboard.SafetyReleaseCount);
        Assert.Empty(keyboard.PressedKeys);
    }

    [Fact]
    public async Task PlaybackService_WaitsWhileTargetProcessIsNotFocused()
    {
        var keyboard = new RecordingKeyboardInput();
        var focusGuard = new MutableFocusGuard();
        var service = new ScorePlaybackService(keyboard, focusGuard);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes = [new NoteEvent { Pitch = 60, DurationTick = 24 }],
                },
            ],
        };

        var playback = service.PlayAsync(score, countdownSeconds: 0);
        await Task.Delay(80);
        Assert.Empty(keyboard.Events);

        focusGuard.IsFocused = true;
        await playback.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(["down:A", "up:A"], keyboard.Events);
    }

    [Fact]
    public async Task PlaybackService_ManualPauseFreezesTimeline()
    {
        var keyboard = new RecordingKeyboardInput();
        var service = new ScorePlaybackService(keyboard);
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes = [new NoteEvent { Pitch = 60, StartTick = 96, DurationTick = 24 }],
                },
            ],
        };

        var playback = service.PlayAsync(score, countdownSeconds: 0);
        service.Pause();
        await Task.Delay(160);
        Assert.Empty(keyboard.Events);

        service.Resume();
        await playback.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(["down:A", "up:A"], keyboard.Events);
    }

    [Fact]
    public async Task PlaybackService_PauseReleasesSustainedKeyImmediately()
    {
        var keyboard = new RecordingKeyboardInput();
        var focusGuard = new MutableFocusGuard { IsFocused = true };
        var service = new ScorePlaybackService(keyboard, focusGuard);
        using var cancellation = new CancellationTokenSource();
        var score = ScoreDocument.CreateEmpty() with
        {
            Tracks =
            [
                new ScoreTrack
                {
                    Id = "main",
                    Notes = [new NoteEvent { Pitch = 60, DurationTick = 480 }],
                },
            ],
        };

        var playback = service.PlayAsync(
            score,
            countdownSeconds: 0,
            cancellationToken: cancellation.Token);
        await Task.Delay(20);
        Assert.True(service.PauseIfTargetFocused());
        await Task.Delay(80);

        Assert.Equal(["down:A", "up:A"], keyboard.Events);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);
        service.Resume();
    }

    [Fact]
    public async Task PlaybackService_StartsCountdownOnlyAfterTargetIsFocused()
    {
        var keyboard = new RecordingKeyboardInput();
        var focusGuard = new MutableFocusGuard();
        var progress = new RecordingProgress();
        var service = new ScorePlaybackService(keyboard, focusGuard);
        using var cancellation = new CancellationTokenSource();

        var playback = service.PlayAsync(
            ScoreDocument.CreateEmpty(),
            countdownSeconds: 3,
            progress,
            cancellation.Token);
        await Task.Delay(80);

        Assert.Contains(progress.Events, item => item.Phase == PlaybackPhase.WaitingForTarget);
        Assert.DoesNotContain(progress.Events, item => item.Phase == PlaybackPhase.Countdown);

        focusGuard.IsFocused = true;
        await Task.Delay(80);

        Assert.Contains(
            progress.Events,
            item => item.Phase == PlaybackPhase.Countdown && item.CountdownSeconds == 3);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);
    }

    [Fact]
    public void PlaybackService_EscapePauseRequiresFocusedTarget()
    {
        var focusGuard = new MutableFocusGuard();
        var service = new ScorePlaybackService(new RecordingKeyboardInput(), focusGuard);

        Assert.False(service.PauseIfTargetFocused());
        Assert.False(service.IsManuallyPaused);

        focusGuard.IsFocused = true;
        Assert.True(service.PauseIfTargetFocused());
        Assert.True(service.IsManuallyPaused);
    }

    [Fact]
    public async Task PlaybackService_FocusLossRestartsSafetyCountdown()
    {
        var focusGuard = new MutableFocusGuard { IsFocused = true };
        var progress = new RecordingProgress();
        var service = new ScorePlaybackService(new RecordingKeyboardInput(), focusGuard);
        using var cancellation = new CancellationTokenSource();

        var playback = service.PlayAsync(
            ScoreDocument.CreateEmpty(),
            countdownSeconds: 3,
            progress,
            cancellation.Token);
        await Task.Delay(80);
        focusGuard.IsFocused = false;
        await Task.Delay(80);
        focusGuard.IsFocused = true;
        await Task.Delay(80);

        Assert.True(progress.Events.Count(item =>
            item.Phase == PlaybackPhase.Countdown && item.CountdownSeconds == 3) >= 2);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playback);
    }

    private sealed class RecordingKeyboardInput : IKeyboardInput
    {
        public List<string> Events { get; } = [];

        public void KeyDown(IReadOnlyList<GenshinKey> keys) =>
            Events.Add($"down:{string.Join(',', keys)}");

        public void KeyUp(IReadOnlyList<GenshinKey> keys) =>
            Events.Add($"up:{string.Join(',', keys)}");
    }

    private sealed class SafetyRecordingKeyboardInput : IKeyboardInput, IKeyboardSafetyController
    {
        public HashSet<GenshinKey> PressedKeys { get; } = [];

        public bool ThrowOnKeyDown { get; init; }

        public int SafetyReleaseCount { get; private set; }

        public void KeyDown(IReadOnlyList<GenshinKey> keys)
        {
            PressedKeys.UnionWith(keys);
            if (ThrowOnKeyDown)
            {
                throw new InvalidOperationException("Simulated input failure.");
            }
        }

        public void KeyUp(IReadOnlyList<GenshinKey> keys) => PressedKeys.ExceptWith(keys);

        public void ReleasePressedKeys()
        {
            SafetyReleaseCount++;
            PressedKeys.Clear();
        }

        public void EmergencyReleaseAllKeys() => PressedKeys.Clear();
    }

    private sealed class MutableFocusGuard : IPlaybackFocusGuard
    {
        private volatile bool _isFocused;

        public bool IsFocused
        {
            get => _isFocused;
            set => _isFocused = value;
        }

        public bool IsPlaybackTargetFocused() => IsFocused;
    }

    private sealed class RecordingProgress : IProgress<PlaybackProgress>
    {
        public ConcurrentQueue<PlaybackProgress> Events { get; } = new();

        public void Report(PlaybackProgress value) => Events.Enqueue(value);
    }
}
