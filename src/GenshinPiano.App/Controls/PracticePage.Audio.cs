using GenshinPiano.App.Services;
using GenshinPiano.Core.Playback;

namespace GenshinPiano.App.Controls;

public partial class PracticePage
{
    private CancellationTokenSource? _scorePlaybackCts;
    private Task? _scorePlaybackTask;
    private int _scorePlaybackGeneration;

    private async Task PreviewAsync(GenshinKey key)
    {
        if (_session.IsRhythmGame)
        {
            PracticeHitSound.Play();
            return;
        }
        if (System.Windows.Application.Current is not GenshinPiano.App.App app ||
            app.AuditionService is not { } auditionService ||
            _viewModel is null ||
            !GenshinKeyMap.TryGetPitch(key, out var pitch))
        {
            return;
        }

        try
        {
            await auditionService.PreviewNoteAsync(
                pitch,
                _practiceInstrument,
                duration: TimeSpan.FromMilliseconds(220));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Practice key preview failed: {exception.Message}");
        }
    }

    private async Task WarmPracticeAudioAsync()
    {
        if (!IsLoaded || !IsVisible ||
            System.Windows.Application.Current is not App { AuditionService: { } service }) return;
        try { await service.PrepareAsync(_practiceInstrument); }
        catch (Exception exception)
        {
            AppLogger.Warning($"Practice audio warmup failed: {exception.Message}");
        }
    }

    private async void StartRhythmGamePlayback(bool resumed)
    {
        CancelScorePlayback();
        var generation = _scorePlaybackGeneration;
        var previous = _scorePlaybackTask;
        if (previous is not null)
        {
            try { await previous; }
            catch (OperationCanceledException) { }
            catch (Exception exception) { AppLogger.Warning($"Previous practice playback failed: {exception.Message}"); }
        }
        if (generation != _scorePlaybackGeneration || !_session.IsRunning || !_session.IsRhythmGame ||
            System.Windows.Application.Current is not App app ||
            app.AuditionService is not { } service ||
            _viewModel?.CurrentScore is not { } score ||
            _session.CurrentStep is not { } startStep)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _scorePlaybackCts = cancellation;
        try
        {
            if (!resumed)
            {
                var remaining = startStep.Offset - _session.Position;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(
                        TimeSpan.FromTicks((long)(remaining.Ticks / _session.Speed)),
                        cancellation.Token);
            }
            if (generation != _scorePlaybackGeneration) return;
            var startTick = resumed
                ? (long)Math.Max(0, _session.GetTickAt(_session.Position))
                : startStep.Tick;
            _scorePlaybackTask = service.PlayAsync(
                score,
                startTick,
                _practiceInstrument,
                naturalSustain: true,
                cancellationToken: cancellation.Token,
                playbackSpeed: _session.Speed);
            await _scorePlaybackTask;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLogger.Warning($"Rhythm game playback failed: {exception}");
        }
        finally
        {
            if (ReferenceEquals(_scorePlaybackCts, cancellation))
                _scorePlaybackCts = null;
            cancellation.Dispose();
        }
    }

    private void CancelScorePlayback()
    {
        _scorePlaybackGeneration++;
        _scorePlaybackCts?.Cancel();
        _scorePlaybackCts = null;
    }
}
