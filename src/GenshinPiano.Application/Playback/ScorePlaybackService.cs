using System.Diagnostics;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Playback;

public enum PlaybackPhase
{
    WaitingForTarget,
    Countdown,
    Paused,
    Resumed,
    Playing,
    Completed,
}

public enum PlaybackPauseReason
{
    None,
    Manual,
    TargetNotFocused,
}

public sealed record PlaybackProgress(
    PlaybackPhase Phase,
    int CountdownSeconds = 0,
    int ChordIndex = 0,
    int ChordCount = 0,
    int SkippedNoteCount = 0,
    PlaybackPauseReason PauseReason = PlaybackPauseReason.None,
    IReadOnlyList<GenshinKey>? CurrentKeys = null);

public sealed class ScorePlaybackService(
    IKeyboardInput keyboardInput,
    IPlaybackFocusGuard? focusGuard = null)
{
    private static readonly TimeSpan FocusPollingInterval = TimeSpan.FromMilliseconds(50);
    private int _manualPauseRequested;

    public bool IsManuallyPaused => Volatile.Read(ref _manualPauseRequested) != 0;

    public bool TryFocusFirstPlaybackTarget() =>
        focusGuard?.TryFocusFirstPlaybackTarget() == true;

    public void Pause() => Interlocked.Exchange(ref _manualPauseRequested, 1);

    public void Resume() => Interlocked.Exchange(ref _manualPauseRequested, 0);

    public bool PauseIfTargetFocused()
    {
        if (focusGuard is not null && !focusGuard.IsPlaybackTargetFocused())
        {
            return false;
        }

        Pause();
        return true;
    }

    public async Task PlayAsync(
        ScoreDocument score,
        int countdownSeconds = 3,
        IProgress<PlaybackProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (countdownSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(countdownSeconds));
        }

        Resume();
        var plan = ScorePlaybackPlanner.Create(score);
        await WaitForSafeStartAsync(plan, countdownSeconds, progress, cancellationToken);

        var activeKeys = new HashSet<GenshinKey>();
        void ReleaseActiveKeys()
        {
            if (activeKeys.Count == 0)
            {
                return;
            }

            var keys = activeKeys.OrderBy(key => key).ToArray();
            keyboardInput.KeyUp(keys);
            activeKeys.Clear();
        }

        var stopwatch = Stopwatch.StartNew();
        var pausedDuration = TimeSpan.Zero;
        var attackIndex = 0;
        try
        {
            foreach (var keyChange in plan.Events)
            {
                pausedDuration = await DelayUntilPlayableAsync(
                    stopwatch,
                    keyChange.Offset,
                    pausedDuration,
                    plan,
                    progress,
                    ReleaseActiveKeys,
                    cancellationToken);

                var keysToRelease = keyChange.KeysUp.Where(activeKeys.Contains).ToArray();
                if (keysToRelease.Length > 0)
                {
                    keyboardInput.KeyUp(keysToRelease);
                    activeKeys.ExceptWith(keysToRelease);
                }

                if (keyChange.KeysDown.Count == 0)
                {
                    continue;
                }

                attackIndex++;
                progress?.Report(new PlaybackProgress(
                    PlaybackPhase.Playing,
                    ChordIndex: attackIndex,
                    ChordCount: plan.AttackCount,
                    SkippedNoteCount: plan.SkippedNoteCount,
                    CurrentKeys: keyChange.KeysDown));

                try
                {
                    keyboardInput.KeyDown(keyChange.KeysDown);
                    activeKeys.UnionWith(keyChange.KeysDown);
                }
                catch
                {
                    keyboardInput.KeyUp(keyChange.KeysDown);
                    throw;
                }
            }

            progress?.Report(new PlaybackProgress(
                PlaybackPhase.Completed,
                ChordIndex: plan.AttackCount,
                ChordCount: plan.AttackCount,
                SkippedNoteCount: plan.SkippedNoteCount));
        }
        finally
        {
            ReleaseActiveKeys();
        }
    }

    private async Task WaitForSafeStartAsync(
        ScorePlaybackPlan plan,
        int countdownSeconds,
        IProgress<PlaybackProgress>? progress,
        CancellationToken cancellationToken)
    {
        var remaining = countdownSeconds;
        while (true)
        {
            var pauseReason = GetPauseReason();
            if (pauseReason != PlaybackPauseReason.None)
            {
                progress?.Report(new PlaybackProgress(
                    pauseReason == PlaybackPauseReason.TargetNotFocused
                        ? PlaybackPhase.WaitingForTarget
                        : PlaybackPhase.Paused,
                    ChordCount: plan.AttackCount,
                    SkippedNoteCount: plan.SkippedNoteCount,
                    PauseReason: pauseReason));

                do
                {
                    await Task.Delay(FocusPollingInterval, cancellationToken);
                }
                while (GetPauseReason() != PlaybackPauseReason.None);

                remaining = countdownSeconds;
                continue;
            }

            if (remaining == 0)
            {
                return;
            }

            progress?.Report(new PlaybackProgress(
                PlaybackPhase.Countdown,
                CountdownSeconds: remaining,
                ChordCount: plan.AttackCount,
                SkippedNoteCount: plan.SkippedNoteCount));

            if (await RemainsPlayableForAsync(TimeSpan.FromSeconds(1), cancellationToken))
            {
                remaining--;
            }
            else
            {
                remaining = countdownSeconds;
            }
        }
    }

    private async Task<bool> RemainsPlayableForAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            if (GetPauseReason() != PlaybackPauseReason.None)
            {
                return false;
            }

            var remaining = duration - stopwatch.Elapsed;
            await Task.Delay(
                remaining < FocusPollingInterval ? remaining : FocusPollingInterval,
                cancellationToken);
        }

        return true;
    }

    private async Task<TimeSpan> DelayUntilPlayableAsync(
        Stopwatch stopwatch,
        TimeSpan eventOffset,
        TimeSpan pausedDuration,
        ScorePlaybackPlan plan,
        IProgress<PlaybackProgress>? progress,
        Action releaseActiveKeys,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var pauseReason = GetPauseReason();
            if (pauseReason != PlaybackPauseReason.None)
            {
                releaseActiveKeys();
                progress?.Report(new PlaybackProgress(
                    PlaybackPhase.Paused,
                    ChordCount: plan.AttackCount,
                    SkippedNoteCount: plan.SkippedNoteCount,
                    PauseReason: pauseReason));

                var pauseStarted = stopwatch.Elapsed;
                do
                {
                    await Task.Delay(FocusPollingInterval, cancellationToken);
                }
                while (GetPauseReason() != PlaybackPauseReason.None);

                pausedDuration += stopwatch.Elapsed - pauseStarted;
                progress?.Report(new PlaybackProgress(
                    PlaybackPhase.Resumed,
                    ChordCount: plan.AttackCount,
                    SkippedNoteCount: plan.SkippedNoteCount));
                continue;
            }

            var target = eventOffset + pausedDuration;
            if (stopwatch.Elapsed >= target)
            {
                return pausedDuration;
            }

            var remaining = target - stopwatch.Elapsed;
            if (remaining > TimeSpan.FromMilliseconds(2))
            {
                var delay = remaining - TimeSpan.FromMilliseconds(1);
                await Task.Delay(
                    delay < FocusPollingInterval ? delay : FocusPollingInterval,
                    cancellationToken);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.SpinWait(64);
            }
        }
    }

    private PlaybackPauseReason GetPauseReason()
    {
        if (IsManuallyPaused)
        {
            return PlaybackPauseReason.Manual;
        }

        return focusGuard is not null && !focusGuard.IsPlaybackTargetFocused()
            ? PlaybackPauseReason.TargetNotFocused
            : PlaybackPauseReason.None;
    }
}
