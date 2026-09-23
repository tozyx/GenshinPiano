using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Playback;

namespace GenshinPiano.App.Controls;

public partial class PracticePage : UserControl
{
    private sealed record Step(long Tick, TimeSpan Offset, IReadOnlyList<GenshinKey> Keys);
    private IReadOnlyList<Step> _steps = [];
    private readonly HashSet<GenshinKey> _matched = [];
    private MainWindowViewModel? _viewModel;
    private int _index, _combo, _hits, _attempts;
    private bool _running, _timed;
    private TimeSpan _timedPosition;
    private TimeSpan _timedOrigin;
    private long _timedStarted;
    private double _playbackSpeed = 1;
    private int _practiceInstrument = AuditionInstrumentIds.WindsongLyre;
    private bool _renderClockAttached;
    private bool _playIconShowsPause;
    private bool _restoringPracticeSettings = true;
    private double _inputDelayMs;
    private int _bookmark = -1;
    private int _navigationVersion;
    private bool _positioning;
    private bool _resumeClock;
    private bool _rhythmGame;
    private readonly HashSet<ComboBox> _expandingPracticeOptions = [];
    private bool _expandingPracticeVolume;
    private bool _isDraggingPracticeVolume;
    private CancellationTokenSource? _practiceVolumeCloseCts;
    private CancellationTokenSource? _scorePlaybackCts;
    private Task? _scorePlaybackTask;
    private int _scorePlaybackGeneration;
    private static readonly TimeSpan TimedPreRoll = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ApproachLeadTime = TimeSpan.FromMilliseconds(1500);
    private bool UsesClock => _timed || _rhythmGame;

    public PracticePage()
    {
        InitializeComponent();
        RestorePracticeSettings();
        Surface.BookmarkChanged += (_, index) =>
        {
            _bookmark = index;
            ReturnBookmarkButton.IsEnabled = index >= 0;
        };
        PreviewMouseRightButtonDown += (_, e) =>
        {
            _bookmark = -1;
            Surface.SetBookmark(-1);
            ReturnBookmarkButton.IsEnabled = false;
            e.Handled = true;
        };

        SetMode(PracticeSurfaceMode.VerticalRoll);
        Loaded += (_, _) =>
        {
            Attach();
            if (IsVisible) InitializeSelectionIndicators();
        };
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true)
            {
                SyncGlobalAuditionVolume();
                Dispatcher.BeginInvoke(InitializeSelectionIndicators);
            }
            else PausePractice();
        };
        Unloaded += (_, _) =>
        {
            CancelPracticeVolumeClose();
            PausePractice();
        };
        DataContextChanged += (_, _) => Attach();
    }

    private void RestorePracticeSettings()
    {
        if (System.Windows.Application.Current is App app)
        {
            var settings = app.UserSettingsService.Current.Practice;
            _inputDelayMs = settings.InputDelayMs;
            PracticeHitSound.SetVolume(settings.RhythmHitVolume);
            PlaybackSpeedBox.SelectedIndex = settings.PlaybackSpeed switch
            {
                0.25 => 0,
                0.5 => 1,
                1.25 => 3,
                _ => 2,
            };
            NoteSpacingBox.SelectedIndex = settings.NoteSpacing switch
            {
                1.25 => 1,
                1.5 => 2,
                2 => 3,
                _ => 0,
            };
            PracticeVolumeSlider.Value = app.UserSettingsService.Current.Editor.AuditionVolume / 100d;
            ApplyGlobalAuditionVolume(PracticeVolumeSlider.Value);
        }

        _restoringPracticeSettings = false;
    }
    private void InitializeSelectionIndicators()
    {
        SelectionIndicatorAnimator.Move(
            ViewTabIndicator,
            Surface.Mode == PracticeSurfaceMode.GameKeys ? GameKeysModeButton : VerticalRollModeButton,
            ViewTabsHost,
            false);
        SelectionIndicatorAnimator.Move(
            PracticeModeTabIndicator,
            _timed ? TimedModeButton : FollowModeButton,
            PracticeModeTabsHost,
            false);
        Surface.Focus();
    }

    private static ComboBox? FindPracticeOptionCombo(object sender)
    {
        if (sender is not Border { Child: StackPanel panel }) return null;
        foreach (UIElement child in panel.Children)
            if (child is Grid { Children.Count: > 0 } reveal && reveal.Children[0] is ComboBox combo)
                return combo;
        return null;
    }

    private Border? FindPracticeOptionHost(ComboBox combo)
    {
        if (ReferenceEquals(combo, PlaybackSpeedBox)) return PlaybackSpeedOption;
        if (ReferenceEquals(combo, NoteSpacingBox)) return NoteSpacingOption;
        if (ReferenceEquals(combo, PracticeInstrumentBox)) return InstrumentOption;
        return null;
    }

    private void PracticeOption_OnMouseEnter(object sender, MouseEventArgs e) =>
        AnimatePracticeOption(sender, true);

    private void PracticeOption_OnMouseLeave(object sender, MouseEventArgs e)
    {
        var combo = FindPracticeOptionCombo(sender);
        if (combo is null) return;
        if (combo.IsDropDownOpen || combo.IsMouseOver || _expandingPracticeOptions.Contains(combo)) return;
        AnimatePracticeOption(sender, false);
    }

    private void PracticeOptionCombo_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox combo && _expandingPracticeOptions.Contains(combo))
            CompletePracticeOptionExpansion(combo);
    }

    private void PracticeOption_OnDropDownClosed(object sender, EventArgs e)
    {
        if (sender is ComboBox combo && FindPracticeOptionHost(combo) is { } host &&
            !host.IsMouseOver && !combo.IsMouseOver)
            AnimatePracticeOption(host, false);
        Surface.Focus();
    }

    private double GetPracticeOptionWidth(ComboBox combo)
    {
        var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
        return ReferenceEquals(combo, PracticeInstrumentBox)
            ? 168
            : english ? 92 : 96;
    }

    private void CompletePracticeOptionExpansion(ComboBox combo)
    {
        if (combo.Parent is not Grid reveal) return;
        var width = GetPracticeOptionWidth(combo);
        combo.Width = width;
        reveal.BeginAnimation(WidthProperty, null);
        reveal.BeginAnimation(OpacityProperty, null);
        reveal.Width = width;
        reveal.Opacity = 1;
        reveal.IsHitTestVisible = true;
        _expandingPracticeOptions.Remove(combo);
        reveal.UpdateLayout();
    }

    private void AnimatePracticeOption(object sender, bool expanded)
    {
        var combo = FindPracticeOptionCombo(sender);
        if (combo?.Parent is not Grid reveal) return;

        var expandedWidth = GetPracticeOptionWidth(combo);
        combo.Width = expandedWidth;
        if (expanded)
        {
            reveal.IsHitTestVisible = true;
            _expandingPracticeOptions.Add(combo);
        }
        else reveal.IsHitTestVisible = false;

        var duration = TimeSpan.FromMilliseconds(170);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var widthAnimation = new DoubleAnimation(expanded ? expandedWidth : 0, duration)
            { EasingFunction = easing };
        var opacityAnimation = new DoubleAnimation(expanded ? 1 : 0, duration) { EasingFunction = easing };
        if (expanded)
        {
            widthAnimation.Completed += (_, _) =>
            {
                _expandingPracticeOptions.Remove(combo);
                reveal.IsHitTestVisible = true;
                if (sender is Border host && !host.IsMouseOver && !combo.IsMouseOver && !combo.IsDropDownOpen)
                    AnimatePracticeOption(host, false);
            };
        }

        reveal.BeginAnimation(WidthProperty, widthAnimation);
        reveal.BeginAnimation(OpacityProperty, opacityAnimation);
    }

    private void PracticeVolumeOption_OnMouseEnter(object sender, MouseEventArgs e)
    {
        CancelPracticeVolumeClose();
        if (!_expandingPracticeVolume && PracticeVolumeReveal.ActualWidth < 169)
            AnimatePracticeVolume(true);
    }

    private void PracticeVolumeOption_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_expandingPracticeVolume || _isDraggingPracticeVolume || PracticeVolumeSlider.IsMouseCaptureWithin)
            return;
        SchedulePracticeVolumeClose();
    }

    private void PracticeVolumeSlider_OnMouseEnter(object sender, MouseEventArgs e) =>
        CancelPracticeVolumeClose();

    private void PracticeVolumeSlider_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPracticeVolume && !PracticeVolumeSlider.IsMouseCaptureWithin)
            SchedulePracticeVolumeClose();
    }

    private void CancelPracticeVolumeClose()
    {
        _practiceVolumeCloseCts?.Cancel();
        _practiceVolumeCloseCts?.Dispose();
        _practiceVolumeCloseCts = null;
    }

    private async void SchedulePracticeVolumeClose()
    {
        CancelPracticeVolumeClose();
        var cancellation = new CancellationTokenSource();
        _practiceVolumeCloseCts = cancellation;
        try
        {
            await Task.Delay(240, cancellation.Token);
            if (!IsPointerNear(PracticeVolumeOption, 8) &&
                !IsPointerNear(PracticeVolumeSlider, 8) &&
                !_isDraggingPracticeVolume && !PracticeVolumeSlider.IsMouseCaptureWithin)
            {
                AnimatePracticeVolume(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_practiceVolumeCloseCts, cancellation))
            {
                _practiceVolumeCloseCts.Dispose();
                _practiceVolumeCloseCts = null;
            }
        }
    }

    private static bool IsPointerNear(FrameworkElement element, double tolerance)
    {
        var point = Mouse.GetPosition(element);
        return point.X >= -tolerance && point.Y >= -tolerance &&
               point.X <= element.ActualWidth + tolerance &&
               point.Y <= element.ActualHeight + tolerance;
    }

    private void AnimatePracticeVolume(bool expanded)
    {
        if (expanded)
        {
            PracticeVolumeReveal.IsHitTestVisible = true;
            _expandingPracticeVolume = true;
        }
        else PracticeVolumeReveal.IsHitTestVisible = false;

        var duration = TimeSpan.FromMilliseconds(170);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var widthAnimation = new DoubleAnimation(expanded ? 170 : 0, duration)
            { EasingFunction = easing };
        if (expanded)
        {
            widthAnimation.Completed += (_, _) =>
            {
                _expandingPracticeVolume = false;
                if (!IsPointerNear(PracticeVolumeOption, 8) && !IsPointerNear(PracticeVolumeSlider, 8))
                    SchedulePracticeVolumeClose();
            };
        }
        PracticeVolumeReveal.BeginAnimation(WidthProperty, widthAnimation);
        PracticeVolumeReveal.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expanded ? 1 : 0, duration) { EasingFunction = easing });
    }

    private void CompletePracticeVolumeExpansion()
    {
        PracticeVolumeReveal.BeginAnimation(WidthProperty, null);
        PracticeVolumeReveal.BeginAnimation(OpacityProperty, null);
        PracticeVolumeReveal.Width = 170;
        PracticeVolumeReveal.Opacity = 1;
        PracticeVolumeReveal.IsHitTestVisible = true;
        _expandingPracticeVolume = false;
        PracticeVolumeReveal.UpdateLayout();
    }

    private void PracticeVolumeSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPracticeVolumeClose();
        if (_expandingPracticeVolume) CompletePracticeVolumeExpansion();
        _isDraggingPracticeVolume = true;
        PracticeVolumeSlider.CaptureMouse();
        UpdatePracticeVolumeFromPointer(e);
        e.Handled = true;
    }

    private void PracticeVolumeSlider_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPracticeVolume) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndPracticeVolumeDrag();
            return;
        }
        UpdatePracticeVolumeFromPointer(e);
        e.Handled = true;
    }

    private void PracticeVolumeSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingPracticeVolume) return;
        UpdatePracticeVolumeFromPointer(e);
        EndPracticeVolumeDrag();
        e.Handled = true;
    }

    private void UpdatePracticeVolumeFromPointer(MouseEventArgs e)
    {
        const double trackMargin = 6;
        var usableWidth = Math.Max(1, PracticeVolumeSlider.ActualWidth - trackMargin * 2);
        var x = e.GetPosition(PracticeVolumeSlider).X;
        PracticeVolumeSlider.Value = Math.Clamp((x - trackMargin) / usableWidth, 0, 1);
    }

    private void EndPracticeVolumeDrag()
    {
        _isDraggingPracticeVolume = false;
        if (PracticeVolumeSlider.IsMouseCaptured) PracticeVolumeSlider.ReleaseMouseCapture();
        if (!IsPointerNear(PracticeVolumeOption, 8) && !IsPointerNear(PracticeVolumeSlider, 8))
            SchedulePracticeVolumeClose();
        Surface.Focus();
    }

    private void PracticeVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyGlobalAuditionVolume(e.NewValue);
        UpdatePracticeVolumeValueTrack();
        if (!_restoringPracticeSettings && System.Windows.Application.Current is App app)
            app.UserSettingsService.SetAuditionVolume((int)Math.Round(e.NewValue * 100));
    }

    private void PracticeVolumeSlider_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdatePracticeVolumeValueTrack();

    private void UpdatePracticeVolumeValueTrack()
    {
        if (PracticeVolumeValueTrack is null || PracticeVolumeSlider is null) return;
        var availableWidth = Math.Max(0, PracticeVolumeSlider.ActualWidth - 12);
        PracticeVolumeValueTrack.Width = availableWidth * PracticeVolumeSlider.Value;
    }

    private static void ApplyGlobalAuditionVolume(double normalizedVolume)
    {
        if (System.Windows.Application.Current is App { AuditionService: { } service })
            service.SetVolume((int)Math.Round(Math.Clamp(normalizedVolume, 0, 1) * 127));
    }

    private void SyncGlobalAuditionVolume()
    {
        if (System.Windows.Application.Current is not App app) return;
        var normalized = app.UserSettingsService.Current.Editor.AuditionVolume / 100d;
        if (Math.Abs(PracticeVolumeSlider.Value - normalized) > 0.001)
            PracticeVolumeSlider.Value = normalized;
        ApplyGlobalAuditionVolume(normalized);
    }

    private void Attach()
    {
        if (ReferenceEquals(_viewModel, DataContext)) return;
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
        Reload();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentScore)) Reload();
    }

    private void Reload()
    {
        _bookmark = -1;
        Surface.SetBookmark(-1);
        ReturnBookmarkButton.IsEnabled = false;
        Reset(false);
        try
        {
            _steps = _viewModel is null ? [] : ScorePlaybackPlanner.Create(_viewModel.CurrentScore).Events
                .Where(x => x.KeysDown.Count > 0).Select(x => new Step(x.Tick, x.Offset, x.KeysDown)).ToArray();
            Status(_steps.Count == 0 ? "Practice_NoNotes" : "Practice_Ready");
        }
        catch (Exception ex) { _steps = []; PracticeStatusText.Text = ex.Message; }
        Refresh();
    }

    private void PracticePage_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible || e.Handled) return;
        var physical = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (Keyboard.FocusedElement is TextBox ||
            Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true }) return;
        if (physical == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (!e.IsRepeat) StartPracticeButton_OnClick(sender, e);
            return;
        }
        if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None || !ResolveKey(e, out var key)) return;
        Surface.SetKeyPressed(key, true);
        Accept(key);
        _ = PreviewAsync(key);
        e.Handled = true;
    }

    public void TryHandlePlaybackShortcut(KeyEventArgs e)
    {
        var physical = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (physical == Key.Space) PracticePage_OnPreviewKeyDown(this, e);
    }

    private void PracticePage_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!ResolveKey(e, out var key)) return;
        Surface.SetKeyPressed(key, false);
        e.Handled = true;
    }

    private static bool ResolveKey(KeyEventArgs e, out GenshinKey key)
    {
        var physical = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key == Key.System ? e.SystemKey : e.Key;
        return Enum.TryParse(physical.ToString(), true, out key) && GenshinKeyMap.TryGetPitch(key, out _);
    }

    private async void Surface_OnPracticeKeyPressed(object? sender, GenshinKey key)
    {
        Accept(key);
        await PreviewAsync(key);
    }

    private async Task PreviewAsync(GenshinKey key)
    {
        if (_rhythmGame)
        {
            PracticeHitSound.Play();
            return;
        }
        if (System.Windows.Application.Current is not GenshinPiano.App.App app ||
            app.AuditionService is not { } auditionService ||
            _viewModel is not { } viewModel ||
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
    private void Accept(GenshinKey key)
    {
        if (!_running || _index >= _steps.Count) return;
        if (UsesClock)
        {
            _timedPosition = CurrentTimedPosition();
            ExpireNotes();
            if (!_running || _index >= _steps.Count) return;
        }
        if (UsesClock && !PracticeTiming.IsHit(JudgmentPosition, _steps[_index].Offset, _playbackSpeed))
        {
            _attempts++;
            _combo = 0;
            Surface.FlashWrongKey(key);
            Stats();
            return;
        }
        if (_matched.Contains(key)) return;
        _attempts++;
        var target = _steps[_index].Keys;
        if (!target.Contains(key))
        {
            _combo = 0;
            Surface.FlashWrongKey(key);
        }
        else
        {
            if (_matched.Add(key)) _hits++;
            if (target.All(_matched.Contains)) { _combo++; Advance(); }
        }
        Stats();
    }

    private void Advance()
    {
        _matched.Clear();
        if (++_index >= _steps.Count)
        {
            _running = false;
            if (_rhythmGame)
            {
                if (_renderClockAttached) CompositionTarget.Rendering -= OnTimedRendering;
                _renderClockAttached = false;
            }
            else
            {
                CancelTimer();
            }
            Status("Practice_Complete");
        }
        Refresh(!UsesClock);
    }

    private async void StartPracticeButton_OnClick(object sender, RoutedEventArgs e)
    {
        Surface.Focus();
        if (_positioning) return;
        if (_steps.Count == 0) { Status("Practice_NoNotes"); return; }
        if (_index >= _steps.Count) Reset(false);
        if (!_running)
        {
            var version = ++_navigationVersion;
            _positioning = true;
            var step = _steps[Math.Min(_index, _steps.Count - 1)];
            Surface.SetPausedSelection(_index, UsesClock);
            Surface.SetRollCursorTick(UsesClock ? GetTickAt(step.Offset - TimedPreRoll) : step.Tick, true);
            await Task.Delay(330);
            if (version != _navigationVersion) return;
            _positioning = false;
            _resumeClock = false;
        }
        var resumed = _resumeClock;
        _running = !_running;
        if (!_running)
        {
            if (UsesClock) _timedPosition = CurrentTimedPosition();
            _resumeClock = UsesClock;
            CancelTimer();
            if (_steps.Count > 0 && _index < _steps.Count)
            {
                _timedPosition = _steps[_index].Offset;
                Surface.SetRollCursorTick(_steps[_index].Tick, true);
            }
            Status("Practice_Paused");
        }
        else if (UsesClock) { Status(_rhythmGame ? "Practice_RhythmGameActive" : "Practice_TimedActive"); StartTimer(); }
        else Status("Practice_FollowActive");
        if (_running && _rhythmGame)
        {
            StartRhythmGamePlayback(resumed);
        }
        Refresh(false);
    }

    private void StopPracticeButton_OnClick(object sender, RoutedEventArgs e)
    {
        Reset(false);
        Status("Practice_Ready");
        Refresh(false);
        Surface.Focus();
    }

    private void FollowModeButton_OnClick(object sender, RoutedEventArgs e) => SetPracticeMode(false);
    private void TimedModeButton_OnClick(object sender, RoutedEventArgs e) => SetPracticeMode(true);

    private void SetPracticeMode(bool timed)
    {
        PausePractice();
        _resumeClock = false;
        _timed = timed;
        _matched.Clear();
        Surface.SetTimedApproach(UsesClock);
        FollowModeButton.IsChecked = !timed;
        TimedModeButton.IsChecked = timed;
        if (IsLoaded)
            SelectionIndicatorAnimator.Move(
                PracticeModeTabIndicator,
                timed ? TimedModeButton : FollowModeButton,
                PracticeModeTabsHost);
        Status("Practice_Ready");
        _ = PositionAtCurrentAsync();
        Surface.Focus();
    }

    private void StartTimer()
    {
        CancelTimer();
        _timedOrigin = _resumeClock ? _timedPosition + TimedPreRoll : _steps[_index].Offset;
        _resumeClock = false;
        _timedStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _timedPosition = _timedOrigin - TimedPreRoll;
        _renderClockAttached = true;
        CompositionTarget.Rendering += OnTimedRendering;
    }

    private void OnTimedRendering(object? sender, EventArgs e)
    {
        if (!_running || !UsesClock || _index >= _steps.Count) return;
        _timedPosition = CurrentTimedPosition();
        Surface.SetRollCursorTick(GetTickAt(_timedPosition), false);
        var remaining = _steps[_index].Offset - _timedPosition;
        var approachWindow = ApproachLeadTime.TotalMilliseconds * _playbackSpeed;
        Surface.SetApproachProgress(
            1 - remaining.TotalMilliseconds / Math.Max(1, approachWindow));

        ExpireNotes();
    }

    private TimeSpan CurrentTimedPosition() => _timedOrigin - TimedPreRoll +
        TimeSpan.FromTicks((long)(System.Diagnostics.Stopwatch.GetElapsedTime(_timedStarted).Ticks * _playbackSpeed));

    private TimeSpan JudgmentPosition => _rhythmGame
        ? PracticeTiming.Compensate(_timedPosition, _inputDelayMs, _playbackSpeed)
        : _timedPosition;

    private void ExpireNotes()
    {
        while (_running && _index < _steps.Count &&
               PracticeTiming.IsMissed(JudgmentPosition, _steps[_index].Offset, _playbackSpeed))
        {
            _attempts += _steps[_index].Keys.Count(key => !_matched.Contains(key));
            _combo = 0;
            Advance();
            Stats();
        }
    }

    private double GetTickAt(TimeSpan offset)
    {
        if (_steps.Count == 0) return 0;
        var upper = 0;
        while (upper < _steps.Count && _steps[upper].Offset < offset) upper++;
        if (upper == 0)
        {
            var slope = _steps.Count > 1
                ? (_steps[1].Tick - _steps[0].Tick) / Math.Max(0.001, (_steps[1].Offset - _steps[0].Offset).TotalSeconds)
                : (_viewModel?.CurrentScore.Timing.Ppq ?? 480) * 2d;
            return _steps[0].Tick + (offset - _steps[0].Offset).TotalSeconds * slope;
        }
        if (upper >= _steps.Count) return _steps[^1].Tick;
        var left = _steps[upper - 1];
        var right = _steps[upper];
        var ratio = (offset - left.Offset).TotalMilliseconds /
                    Math.Max(1, (right.Offset - left.Offset).TotalMilliseconds);
        return left.Tick + (right.Tick - left.Tick) * ratio;
    }

    private void Reset(bool clearSteps)
    {
        CancelTimer();
        _navigationVersion++;
        _positioning = false;
        _running = false;
        _resumeClock = false;
        _index = _combo = _hits = _attempts = 0;
        _matched.Clear();
        if (clearSteps) _steps = [];
    }

    private void CancelTimer()
    {
        if (_renderClockAttached) CompositionTarget.Rendering -= OnTimedRendering;
        _renderClockAttached = false;
        CancelScorePlayback();
    }

    private void PlaybackSpeedBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var wasRunning = _running && UsesClock;
        var currentPosition = wasRunning ? CurrentTimedPosition() : _timedPosition;
        _playbackSpeed = PlaybackSpeedBox.SelectedIndex switch
        {
            0 => .25, 1 => .5, 3 => 1.25, _ => 1,
        };
        if (!_restoringPracticeSettings && System.Windows.Application.Current is App app)
            app.UserSettingsService.SetPracticePlaybackSpeed(_playbackSpeed);
        if (wasRunning)
        {
            _timedOrigin = currentPosition + TimedPreRoll;
            _timedStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_rhythmGame) StartRhythmGamePlayback(resumed: true);
        }
        Surface?.Focus();
    }

    private void NoteSpacingBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var spacing = NoteSpacingBox.SelectedIndex switch
        {
            1 => 1.25, 2 => 1.5, 3 => 2, _ => 1,
        };
        Surface?.SetRollSpacing(spacing);
        if (!_restoringPracticeSettings && System.Windows.Application.Current is App app)
            app.UserSettingsService.SetPracticeNoteSpacing(spacing);
        Surface?.Focus();
    }

    private void PracticeInstrumentBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PracticeInstrumentBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            int.TryParse(tag, out var instrument) &&
            AuditionInstrumentIds.IsSampled(instrument))
        {
            _practiceInstrument = instrument;
            Surface?.SetInstrumentVisual(instrument);
            if (_running && _rhythmGame) StartRhythmGamePlayback(resumed: true);
        }
        Surface?.Focus();
    }

    private void Refresh(bool animateRoll = false)
    {
        Surface.SetStepTicks(_steps.Select(x => x.Tick).ToArray());
        Surface.SetPracticeRunning(_running);
        Surface.SetPracticePosition(_steps.Select(x => x.Keys).ToArray(), _index);
        Surface.SetPausedSelection(Math.Min(_index, _steps.Count - 1), UsesClock);
        if ((!UsesClock || (!_running && !_resumeClock)) && _steps.Count > 0)
        {
            var step = _steps[Math.Min(_index, _steps.Count - 1)];
            Surface.SetRollCursorTick(UsesClock ? GetTickAt(step.Offset - TimedPreRoll) : step.Tick, animateRoll);
        }
        AnimatePracticePlayIcon(_running);
        Stats();
    }

    private void AnimatePracticePlayIcon(bool playing)
    {
        if (_playIconShowsPause == playing) return;
        _playIconShowsPause = playing;
        var duration = TimeSpan.FromMilliseconds(170);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        PracticePlayIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(playing ? 0 : 1, duration));
        PracticePauseIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(playing ? 1 : 0, duration));
        if (PracticePlayIcon.RenderTransform is ScaleTransform playScale)
        {
            playScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(playing ? .65 : 1, duration) { EasingFunction = easing });
            playScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(playing ? .65 : 1, duration) { EasingFunction = easing });
        }
        if (PracticePauseIcon.RenderTransform is ScaleTransform pauseScale)
        {
            pauseScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(playing ? 1 : .65, duration) { EasingFunction = easing });
            pauseScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(playing ? 1 : .65, duration) { EasingFunction = easing });
        }
        StartPracticeButton.ToolTip = FindResource(playing ? "Practice_Pause" : "Practice_Start");
    }

    private void Stats()
    {
        ComboText.Text = _combo.ToString();
        AccuracyText.Text = _attempts == 0 ? "—" : $"{_hits * 100d / _attempts:0}%";
    }

    private void Status(string key) => PracticeStatusText.Text = FindResource(key)?.ToString() ?? key;

    private void GameKeysModeButton_OnClick(object sender, RoutedEventArgs e) =>
        SetMode(PracticeSurfaceMode.GameKeys);

    private void VerticalRollModeButton_OnClick(object sender, RoutedEventArgs e) =>
        SetMode(PracticeSurfaceMode.VerticalRoll);

    private void SetMode(PracticeSurfaceMode mode)
    {
        if (mode != PracticeSurfaceMode.VerticalRoll && _rhythmGame)
            SetRhythmGame(false);
        Surface.Mode = mode;
        RhythmGameToggle.Visibility = mode == PracticeSurfaceMode.VerticalRoll
            ? Visibility.Visible
            : Visibility.Collapsed;
        GameKeysModeButton.IsChecked = mode == PracticeSurfaceMode.GameKeys;
        VerticalRollModeButton.IsChecked = mode == PracticeSurfaceMode.VerticalRoll;
        if (IsLoaded)
            SelectionIndicatorAnimator.Move(
                ViewTabIndicator,
                mode == PracticeSurfaceMode.GameKeys ? GameKeysModeButton : VerticalRollModeButton,
                ViewTabsHost);
        Surface.Focus();
    }

    private void PausePractice()
    {
        _navigationVersion++;
        _positioning = false;
        Surface.ClearPressedKeys();
        CancelScorePlayback();
        if (!_running) return;
        if (UsesClock) _timedPosition = CurrentTimedPosition();
        _running = false;
        _resumeClock = UsesClock;
        CancelTimer();
        if (_steps.Count > 0 && _index < _steps.Count)
        {
            _timedPosition = _steps[_index].Offset;
            Surface.SetRollCursorTick(_steps[_index].Tick, true);
            Surface.SetPausedSelection(_index, UsesClock);
        }
        Surface.SetPracticeRunning(false);
        AnimatePracticePlayIcon(false);
        Status("Practice_Paused");
    }

    private void RhythmGameToggle_OnClick(object sender, RoutedEventArgs e) =>
        SetRhythmGame(RhythmGameToggle.IsChecked == true);

    private void SetRhythmGame(bool enabled)
    {
        PausePractice();
        _rhythmGame = enabled && Surface.Mode == PracticeSurfaceMode.VerticalRoll;
        RhythmGameToggle.IsChecked = _rhythmGame;
        Surface.SetRhythmGame(_rhythmGame);
        Surface.SetTimedApproach(UsesClock);
        AnimateRhythmGameControls();
        Status("Practice_Ready");
        _ = PositionAtCurrentAsync();
        Surface.Focus();
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
        if (generation != _scorePlaybackGeneration || !_running || !_rhythmGame ||
            System.Windows.Application.Current is not App app ||
            app.AuditionService is not { } service ||
            _viewModel?.CurrentScore is not { } score)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _scorePlaybackCts = cancellation;
        try
        {
            if (!resumed)
            {
                var remaining = _steps[Math.Min(_index, _steps.Count - 1)].Offset - CurrentTimedPosition();
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(
                        TimeSpan.FromTicks((long)(remaining.Ticks / _playbackSpeed)),
                        cancellation.Token);
            }
            if (generation != _scorePlaybackGeneration) return;
            var startTick = resumed
                ? (long)Math.Max(0, GetTickAt(_timedPosition))
                : _steps[Math.Min(_index, _steps.Count - 1)].Tick;
            _scorePlaybackTask = service.PlayAsync(
                score,
                startTick,
                _practiceInstrument,
                naturalSustain: true,
                cancellationToken: cancellation.Token,
                playbackSpeed: _playbackSpeed);
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

    private async void ReturnToBookmark_OnClick(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0 || _bookmark < 0) return;
        Reset(false);
        _index = Math.Clamp(_bookmark, 0, _steps.Count - 1);
        await PositionAtCurrentAsync();
    }

    private async Task PositionAtCurrentAsync()
    {
        var version = ++_navigationVersion;
        _positioning = true;
        Refresh(true);
        await Task.Delay(330);
        if (version != _navigationVersion) return;
        _positioning = false;
    }

    private void Calibrate_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_rhythmGame) return;
        PausePractice();
        var dialog = new GenshinPiano.App.Dialogs.PracticeLatencyDialog(_inputDelayMs)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true)
        {
            _inputDelayMs = dialog.DelayMilliseconds;
            if (System.Windows.Application.Current is App app)
                app.UserSettingsService.SetPracticeInputDelay(_inputDelayMs);
        }
        Surface.Focus();
    }

    private void AnimateRhythmGameControls()
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        CalibrationButton.IsEnabled = _rhythmGame;
        RhythmGameSettingsButton.IsEnabled = _rhythmGame;
        CalibrationReveal.BeginAnimation(WidthProperty,
            new DoubleAnimation(_rhythmGame ? 60 : 0, duration) { EasingFunction = easing });
        CalibrationReveal.BeginAnimation(OpacityProperty,
            new DoubleAnimation(_rhythmGame ? 1 : 0, duration) { EasingFunction = easing });
        if (CalibrationButton.RenderTransform is TranslateTransform translate)
            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(_rhythmGame ? 0 : 10, duration) { EasingFunction = easing });
        if (RhythmGameSettingsButton.RenderTransform is TranslateTransform settingsTranslate)
            settingsTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(_rhythmGame ? 0 : 18, duration) { EasingFunction = easing });
        RhythmGameNoteIcon.BeginAnimation(OpacityProperty,
            new DoubleAnimation(_rhythmGame ? 1 : .78, duration) { EasingFunction = easing });
    }

    private void RhythmGameSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_rhythmGame || System.Windows.Application.Current is not App app) return;
        var previousVolume = app.UserSettingsService.Current.Practice.RhythmHitVolume;
        var dialog = new GenshinPiano.App.Dialogs.RhythmGameSettingsDialog(
            previousVolume)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true)
        {
            app.UserSettingsService.SetPracticeRhythmHitVolume(dialog.HitVolume);
            PracticeHitSound.SetVolume(dialog.HitVolume);
        }
        else
        {
            PracticeHitSound.SetVolume(previousVolume);
        }
        Surface.Focus();
    }
}
