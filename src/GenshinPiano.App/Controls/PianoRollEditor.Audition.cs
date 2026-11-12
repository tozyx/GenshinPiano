using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenshinPiano.Application.Playback;

namespace GenshinPiano.App.Controls;

public partial class PianoRollEditor
{
    private ScoreAuditionService? _auditionService;
    private CancellationTokenSource? _auditionCancellation;
    private CancellationTokenSource? _notePreviewCancellation;
    private Task _notePreviewTask = Task.CompletedTask;
    private bool _auditionIsPlaying;
    private bool _auditionPauseRequested;
    private long _auditionTick;
    private bool _continuousFollowActive;
    private bool _playbackScrollAnimationActive;
    private long _lastFollowTick = -1;
    private string _frameRateMode = "VSync";
    private AuditionProgress? _latestAuditionProgress;
    private long _auditionProgressTimestamp;
    private long _nextAuditionFrameTimestamp;
    private long _lastAuditionTextTimestamp;
    private bool _auditionRenderingAttached;
    private AuditionTimeline? _auditionTimeline;

    public void SetFrameRateMode(string mode)
    {
        if (mode is not ("30" or "60" or "VSync"))
        {
            return;
        }

        _frameRateMode = mode;
        _nextAuditionFrameTimestamp = 0;
    }

    private void Surface_OnPlaybackSeekRequested(object? sender, PlaybackSeekRequestedEventArgs e)
    {
        if (_auditionIsPlaying)
        {
            PauseAudition();
        }

        _auditionTick = e.Tick;
        UpdatePlaybackCursor(e.Tick);
        var position = Score is null
            ? TimeSpan.Zero
            : GenshinPiano.Core.Playback.ScorePlaybackPlanner.TickToTime(e.Tick, Score.Timing);
        UpdateAuditionPosition(e.Tick, position);
    }

    private async void Surface_OnNoteCreated(object? sender, NoteCreatedEventArgs e)
    {
        if (_auditionService is null)
        {
            return;
        }

        _notePreviewCancellation?.Cancel();
        try
        {
            await _notePreviewTask;
        }
        catch (OperationCanceledException)
        {
        }

        _notePreviewCancellation?.Dispose();
        _notePreviewCancellation = new CancellationTokenSource();
        var instrument = AuditionInstrumentComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
                         int.TryParse(tag, out var parsedInstrument)
            ? parsedInstrument
            : 0;
        _notePreviewTask = _auditionService.PreviewNoteAsync(
            e.Pitch,
            instrument,
            cancellationToken: _notePreviewCancellation.Token);
        try
        {
            await _notePreviewTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void AuditionPlayButton_OnClick(object sender, RoutedEventArgs e) =>
        await ToggleAuditionAsync();

    private async Task ToggleAuditionAsync()
    {
        if (_auditionIsPlaying)
        {
            PauseAudition();
            return;
        }

        if (_auditionService is null || Score is null)
        {
            return;
        }

        var plan = GenshinPiano.Core.Playback.ScoreAuditionPlanner.Create(Score);
        if (plan.DurationTick <= 0)
        {
            return;
        }

        long loopStartTick = 0;
        long loopEndTick = 0;
        var loopSelection = SelectionLoopToggle.IsChecked == true &&
                            Surface.TryGetSelectedTickRange(out loopStartTick, out loopEndTick);
        if (SelectionLoopToggle.IsChecked == true && !loopSelection)
        {
            SelectionLoopToggle.IsChecked = false;
        }

        if (loopSelection)
        {
            _auditionTick = loopStartTick;
        }
        else if (_auditionTick >= plan.DurationTick)
        {
            _auditionTick = 0;
        }

        _continuousFollowActive = false;
        _lastFollowTick = _auditionTick;
        AuditionPlayButton.IsEnabled = false;
        try
        {
            await EnsurePlaybackHeadVisibleAsync(_auditionTick);
        }
        finally
        {
            AuditionPlayButton.IsEnabled = _auditionService is not null;
        }

        var instrument = AuditionInstrumentComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
                         int.TryParse(tag, out var program)
            ? program
            : 0;
        var cancellation = new CancellationTokenSource();
        _auditionCancellation = cancellation;
        _auditionPauseRequested = false;
        SetAuditionPlayingState(true);
        NoteEditorPopup.IsOpen = false;
        AnimateAuditionPlayIcon(true);
        var playbackEndTick = loopSelection ? loopEndTick : plan.DurationTick;
        var playbackStartPosition = GenshinPiano.Core.Playback.ScorePlaybackPlanner.TickToTime(
            _auditionTick,
            Score.Timing);
        var playbackEndPosition = GenshinPiano.Core.Playback.ScorePlaybackPlanner.TickToTime(
            playbackEndTick,
            Score.Timing);
        try
        {
            await _auditionService.PrepareAsync(instrument, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var initialSampleTimestamp = Stopwatch.GetTimestamp();
            _latestAuditionProgress = new AuditionProgress(
                _auditionTick,
                playbackEndTick,
                playbackStartPosition,
                playbackEndPosition,
                initialSampleTimestamp);
            _auditionProgressTimestamp = initialSampleTimestamp;
            _auditionTimeline = new AuditionTimeline(Score.Timing);
            StartAuditionRendering();
            var progress = new Progress<AuditionProgress>(item =>
            {
                _latestAuditionProgress = item;
                _auditionProgressTimestamp = item.SampleTimestamp > 0
                    ? item.SampleTimestamp
                    : Stopwatch.GetTimestamp();
            });

            do
            {
                await _auditionService.PlayAsync(
                    Score,
                    loopSelection ? loopStartTick : _auditionTick,
                    instrument,
                    NaturalSustainCheckBox.IsChecked == true,
                    progress,
                    cancellation.Token,
                    loopSelection ? loopEndTick : null);
                _auditionTick = loopSelection ? loopStartTick : plan.DurationTick;
            }
            while (loopSelection && !cancellation.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            // Pause and stop both cancel the active local-audition pass.
        }
        finally
        {
            StopAuditionRendering(renderFinalFrame: true);
            if (ReferenceEquals(_auditionCancellation, cancellation))
            {
                _auditionCancellation = null;
                SetAuditionPlayingState(false);
                AnimateAuditionPlayIcon(false);
                if (!_auditionPauseRequested)
                {
                    _auditionTick = 0;
                    Surface.PlaybackTick = 0;
                    UpdatePlaybackCursor(0);
                    UpdateAuditionPosition(0, TimeSpan.Zero);
                }
            }

            cancellation.Dispose();
        }
    }

    private void StartAuditionRendering()
    {
        if (_auditionRenderingAttached)
        {
            return;
        }

        _nextAuditionFrameTimestamp = 0;
        _lastAuditionTextTimestamp = 0;
        CompositionTarget.Rendering += CompositionTarget_OnRendering;
        _auditionRenderingAttached = true;
    }

    private void StopAuditionRendering(bool renderFinalFrame)
    {
        if (renderFinalFrame)
        {
            RenderAuditionFrame(force: true);
        }

        if (_auditionRenderingAttached)
        {
            CompositionTarget.Rendering -= CompositionTarget_OnRendering;
            _auditionRenderingAttached = false;
        }

        _latestAuditionProgress = null;
        _auditionTimeline = null;
    }

    private void CompositionTarget_OnRendering(object? sender, EventArgs e) =>
        RenderAuditionFrame(force: false);

    private void RenderAuditionFrame(bool force)
    {
        if (!_auditionIsPlaying || Score is null || _latestAuditionProgress is not { } progress ||
            _auditionTimeline is not { } timeline)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var minimumFrameTicks = _frameRateMode switch
        {
            "30" => Stopwatch.Frequency / 30,
            "60" => Stopwatch.Frequency / 60,
            _ => 0,
        };
        var schedulingTolerance = minimumFrameTicks / 10;
        if (!force && minimumFrameTicks > 0 &&
            _nextAuditionFrameTimestamp > 0 &&
            now + schedulingTolerance < _nextAuditionFrameTimestamp)
        {
            return;
        }

        if (minimumFrameTicks > 0)
        {
            _nextAuditionFrameTimestamp = _nextAuditionFrameTimestamp <= 0
                ? now + minimumFrameTicks
                : _nextAuditionFrameTimestamp + minimumFrameTicks;
            while (_nextAuditionFrameTimestamp <= now)
            {
                _nextAuditionFrameTimestamp += minimumFrameTicks;
            }
        }
        else
        {
            _nextAuditionFrameTimestamp = now;
        }
        var elapsed = Stopwatch.GetElapsedTime(_auditionProgressTimestamp, now);
        var (tick, interpolatedPosition) = timeline.Interpolate(progress, elapsed);
        _auditionTick = tick;
        Surface.PlaybackTick = tick;
        UpdatePlaybackCursor(tick);
        FollowPlaybackHead(tick);

        // Text layout is substantially more expensive than moving the GPU-composited
        // cursor. Thirty updates per second remain visually continuous for the clock.
        if (force || now - _lastAuditionTextTimestamp >= Stopwatch.Frequency / 30)
        {
            _lastAuditionTextTimestamp = now;
            UpdateAuditionPosition(tick, interpolatedPosition);
        }
    }

    private void AuditionStopButton_OnClick(object sender, RoutedEventArgs e)
    {
        _auditionPauseRequested = false;
        _auditionCancellation?.Cancel();
        _auditionTick = 0;
        Surface.PlaybackTick = 0;
        UpdatePlaybackCursor(0);
        UpdateAuditionPosition(0, TimeSpan.Zero);
    }

    private void PauseAudition()
    {
        _auditionPauseRequested = true;
        _auditionCancellation?.Cancel();
    }

    private void SetAuditionPlayingState(bool isPlaying)
    {
        if (_auditionIsPlaying == isPlaying)
        {
            return;
        }

        _auditionIsPlaying = isPlaying;
        Surface.UsePlaybackCache = isPlaying;
        BpmTextBox.IsEnabled = !isPlaying;
        BpmDisplayButton.IsEnabled = !isPlaying;
        AuditionInstrumentComboBox.IsEnabled = !isPlaying;
        NaturalSustainCheckBox.IsEnabled = !isPlaying;
        SelectionLoopToggle.IsEnabled = !isPlaying;
        EditingToolbar.IsEnabled = !isPlaying;
        Surface.IsEditingEnabled = !isPlaying;
        AuditionStateChanged?.Invoke(this, new AuditionStateChangedEventArgs(isPlaying));
    }

    private void NaturalSustainCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_loadingSettings)
        {
            _settingsService?.SetNaturalSustain(NaturalSustainCheckBox.IsChecked == true);
        }
    }

    private void AuditionInstrumentComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (AuditionInstrumentComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !int.TryParse(tag, out var instrument))
        {
            return;
        }

        if (!_loadingSettings)
        {
            _settingsService?.SetAuditionInstrument(instrument);
        }
    }

    private void UpdateSampleInstrumentAvailability(PianoRollPitchLayoutMode mode)
    {
        if (WindsongLyreInstrumentItem is null || FloralZitherInstrumentItem is null) return;
        var visibility = mode == PianoRollPitchLayoutMode.Genshin21 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in new[]
                 {
                     WindsongLyreInstrumentItem, FloralZitherInstrumentItem,
                     OldFloralZitherInstrumentItem, VintageLyreInstrumentItem,
                     UkuleleInstrumentItem, LingeringEuphoniaInstrumentItem,
                     LeapingSpiritPianoInstrumentItem,
                 })
        {
            item.Visibility = visibility;
        }
        if (visibility == Visibility.Collapsed &&
            AuditionInstrumentComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out var instrument) && AuditionInstrumentIds.IsSampled(instrument))
        {
            SelectComboItem(AuditionInstrumentComboBox, "0");
        }
    }

    private void SelectionLoopToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (SelectionLoopIcon is null ||
            SelectionLoopIcon.RenderTransform is not System.Windows.Media.RotateTransform transform)
        {
            return;
        }

        transform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        var enabled = SelectionLoopToggle.IsChecked == true;
        transform.Angle = enabled ? 0 : 360;
        transform.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                enabled ? 0 : 360,
                enabled ? 360 : 0,
                TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
    }

    private void AnimateAuditionPlayIcon(bool playing)
    {
        var duration = TimeSpan.FromMilliseconds(170);
        AuditionPlayIcon.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(playing ? 0 : 1, duration));
        AuditionPauseIcon.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(playing ? 1 : 0, duration));

        if (AuditionPlayIcon.RenderTransform is System.Windows.Media.ScaleTransform playScale)
        {
            playScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(playing ? 0.65 : 1, duration));
            playScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(playing ? 0.65 : 1, duration));
        }

        if (AuditionPauseIcon.RenderTransform is System.Windows.Media.ScaleTransform pauseScale)
        {
            pauseScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(playing ? 1 : 0.65, duration));
            pauseScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(playing ? 1 : 0.65, duration));
        }
    }

    private void UpdateAuditionPosition(long tick, TimeSpan position)
    {
        var ppq = Score?.Timing.Ppq ?? 480;
        var bar = tick / (ppq * 4L) + 1;
        var beat = tick % (ppq * 4L) / ppq + 1;
        var totalMinutes = (long)position.TotalMinutes;
        AuditionPositionText.Text =
            $"{bar}.{beat}  {totalMinutes:00}:{position.Seconds:00}.{position.Milliseconds:000}";
    }

    private void UpdatePlaybackCursor(long tick)
    {
        if (Score is null || PlaybackCursor.RenderTransform is not System.Windows.Media.TranslateTransform transform)
        {
            return;
        }

        transform.X = tick * Surface.PixelsPerBeat / Score.Timing.Ppq - PlaybackCursor.Width / 2;
        PlaybackCursor.Visibility = Visibility.Visible;
    }

    private void FollowPlaybackHead(long tick)
    {
        if (Score is null || _playbackScrollAnimationActive)
        {
            return;
        }

        var x = tick * Surface.PixelsPerBeat / Score.Timing.Ppq;
        if (_lastFollowTick >= 0 && tick < _lastFollowTick)
        {
            _continuousFollowActive = false;
        }
        _lastFollowTick = tick;

        const double anchorRatio = 0.72;
        var anchor = EditorScrollViewer.ViewportWidth * anchorRatio;
        if (!_continuousFollowActive)
        {
            var activationEdge = EditorScrollViewer.HorizontalOffset + anchor;
            if (x <= activationEdge)
            {
                return;
            }

            _continuousFollowActive = true;
        }

        var target = Math.Max(0, x - anchor);
        if (Math.Abs(target - EditorScrollViewer.HorizontalOffset) < 0.1) return;
        EditorScrollViewer.ScrollToHorizontalOffset(target);
    }

    private async Task EnsurePlaybackHeadVisibleAsync(long tick)
    {
        if (Score is null || EditorScrollViewer.ViewportWidth <= 0)
        {
            return;
        }

        var x = tick * Surface.PixelsPerBeat / Score.Timing.Ppq;
        var viewportLeft = EditorScrollViewer.HorizontalOffset;
        var viewportRight = viewportLeft + EditorScrollViewer.ViewportWidth;
        var margin = Math.Min(48, EditorScrollViewer.ViewportWidth * 0.12);
        if (x >= viewportLeft + margin && x <= viewportRight - margin)
        {
            return;
        }

        var anchor = EditorScrollViewer.ViewportWidth * 0.20;
        await AnimateHorizontalOffsetAsync(Math.Max(0, x - anchor));
    }

    private Task AnimateHorizontalOffsetAsync(double targetOffset)
    {
        var currentOffset = EditorScrollViewer.HorizontalOffset;
        if (Math.Abs(targetOffset - currentOffset) < 1)
        {
            EditorScrollViewer.ScrollToHorizontalOffset(targetOffset);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = Stopwatch.GetTimestamp();
        var duration = TimeSpan.FromMilliseconds(220);
        _playbackScrollAnimationActive = true;
        EventHandler? renderingHandler = null;
        renderingHandler = (_, _) =>
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var progress = Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var easedProgress = 1 - Math.Pow(1 - progress, 3);
            var offset = currentOffset + (targetOffset - currentOffset) * easedProgress;
            EditorScrollViewer.ScrollToHorizontalOffset(offset);
            if (progress < 1)
            {
                return;
            }

            CompositionTarget.Rendering -= renderingHandler;
            EditorScrollViewer.ScrollToHorizontalOffset(targetOffset);
            _playbackScrollAnimationActive = false;
            completion.TrySetResult();
        };
        CompositionTarget.Rendering += renderingHandler;
        return completion.Task;
    }
}

public sealed class AuditionStateChangedEventArgs(bool isPlaying) : EventArgs
{
    public bool IsPlaying { get; } = isPlaying;
}
