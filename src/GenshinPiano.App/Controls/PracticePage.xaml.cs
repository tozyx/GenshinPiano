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
    private readonly PracticeSession _session = new();
    private IReadOnlyList<IReadOnlyList<GenshinKey>> _practiceSequence = [];
    private MainWindowViewModel? _viewModel;
    private int _practiceInstrument = AuditionInstrumentIds.WindsongLyre;
    private bool _renderClockAttached;
    private bool _playIconShowsPause;
    private bool _restoringPracticeSettings = true;
    private int _navigationVersion;
    private bool _positioning;

    public PracticePage()
    {
        InitializeComponent();
        RestorePracticeSettings();
        Surface.BookmarkChanged += (_, index) =>
        {
            _session.Bookmark = index;
            ReturnBookmarkButton.IsEnabled = index >= 0;
        };
        PreviewMouseRightButtonDown += (_, e) =>
        {
            _session.Bookmark = -1;
            Surface.SetBookmark(-1);
            ReturnBookmarkButton.IsEnabled = false;
            e.Handled = true;
        };

        SetMode(PracticeSurfaceMode.VerticalRoll);
        Loaded += (_, _) =>
        {
            Attach();
            if (IsVisible)
            {
                InitializeSelectionIndicators();
                _ = WarmPracticeAudioAsync();
            }
        };
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true)
            {
                SyncGlobalAuditionVolume();
                Dispatcher.BeginInvoke(InitializeSelectionIndicators);
                _ = WarmPracticeAudioAsync();
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
            _session.InputDelayMs = settings.InputDelayMs;
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
            _session.IsTimed ? TimedModeButton : FollowModeButton,
            PracticeModeTabsHost,
            false);
        Surface.Focus();
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
        Reset();
        Surface.SetBookmark(-1);
        ReturnBookmarkButton.IsEnabled = false;
        try
        {
            _session.LoadScore(_viewModel?.CurrentScore);
            Status(_session.Steps.Count == 0 ? "Practice_NoNotes" : "Practice_Ready");
        }
        catch (Exception exception)
        {
            PracticeStatusText.Text = exception.Message;
        }
        Surface.SetStepTicks(_session.Steps.Select(step => step.Tick).ToArray());
        _practiceSequence = _session.Steps.Select(step => step.KeysDown).ToArray();
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

    private void Accept(GenshinKey key)
    {
        var previousIndex = _session.Index;
        var result = _session.Accept(key);
        if (result == PracticeKeyResult.Wrong) Surface.FlashWrongKey(key);
        if (!RefreshAfterProgress(previousIndex) && result != PracticeKeyResult.Ignored) Stats();
    }

    private bool RefreshAfterProgress(int previousIndex)
    {
        if (_session.Index == previousIndex) return false;
        if (_session.IsComplete)
        {
            DetachRenderClock();
            // Rhythm mode still plays the score's audio tail after the final judgment.
            if (!_session.IsRhythmGame) CancelScorePlayback();
            Status("Practice_Complete");
        }
        Refresh(!_session.UsesClock);
        return true;
    }

    private async void StartPracticeButton_OnClick(object sender, RoutedEventArgs e)
    {
        Surface.Focus();
        if (_positioning) return;
        if (_session.IsRunning)
        {
            PausePractice();
            return;
        }
        if (_session.IsComplete) Reset();
        if (_session.CurrentStep is not { } step)
        {
            Status("Practice_NoNotes");
            return;
        }

        var version = ++_navigationVersion;
        _positioning = true;
        try
        {
            if (_session.IsRhythmGame &&
                System.Windows.Application.Current is App { AuditionService: { } service })
                await service.PrepareAsync(_practiceInstrument);

            if (version != _navigationVersion) return;
            Surface.SetPausedSelection(_session.Index, _session.UsesClock);
            var startTick = _session.UsesClock ? _session.GetTickAt(step.Offset - PracticeSession.PreRoll) : step.Tick;
            Surface.SetRollCursorTick(startTick, true);
            await Task.Delay(330);
            if (version != _navigationVersion) return;
        }
        catch (Exception exception)
        {
            if (version != _navigationVersion) return;
            AppLogger.Warning($"Practice audio preparation failed: {exception}");
            PracticeStatusText.Text = exception.Message;
            return;
        }
        finally
        {
            if (version == _navigationVersion) _positioning = false;
        }

        _session.Start();
        if (_session.UsesClock)
        {
            CancelTimer();
            CompositionTarget.Rendering += OnTimedRendering;
            _renderClockAttached = true;
        }
        Status(_session.IsRhythmGame ? "Practice_RhythmGameActive" :
            _session.IsTimed ? "Practice_TimedActive" : "Practice_FollowActive");
        if (_session.IsRhythmGame) StartRhythmGamePlayback(resumed: false);
        Refresh();
    }

    private void StopPracticeButton_OnClick(object sender, RoutedEventArgs e)
    {
        Reset();
        Status("Practice_Ready");
        Refresh(false);
        Surface.Focus();
    }

    private void FollowModeButton_OnClick(object sender, RoutedEventArgs e) => SetPracticeMode(false);
    private void TimedModeButton_OnClick(object sender, RoutedEventArgs e) => SetPracticeMode(true);

    private void SetPracticeMode(bool timed)
    {
        PausePractice();
        _session.SetTimedMode(timed);
        Surface.SetTimedApproach(_session.UsesClock);
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

    private void OnTimedRendering(object? sender, EventArgs e)
    {
        if (!_session.IsRunning || !_session.UsesClock) return;
        Surface.SetRollCursorTick(_session.GetTickAt(_session.Position), false);
        Surface.SetApproachProgress(_session.ApproachProgress);

        var previousIndex = _session.Index;
        _session.AdvanceClock();
        RefreshAfterProgress(previousIndex);
    }

    private void Reset()
    {
        CancelTimer();
        _navigationVersion++;
        _positioning = false;
        _session.Reset();
    }

    private void DetachRenderClock()
    {
        if (_renderClockAttached) CompositionTarget.Rendering -= OnTimedRendering;
        _renderClockAttached = false;
    }

    private void CancelTimer()
    {
        DetachRenderClock();
        CancelScorePlayback();
    }

    private void PlaybackSpeedBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _session.SetSpeed(PlaybackSpeedBox.SelectedIndex switch
        {
            0 => .25, 1 => .5, 3 => 1.25, _ => 1,
        });
        if (!_restoringPracticeSettings && System.Windows.Application.Current is App app)
            app.UserSettingsService.SetPracticePlaybackSpeed(_session.Speed);
        if (_session.IsRunning && _session.IsRhythmGame) StartRhythmGamePlayback(resumed: true);
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
            if (_session.IsRunning && _session.IsRhythmGame) StartRhythmGamePlayback(resumed: true);
            else _ = WarmPracticeAudioAsync();
        }
        Surface?.Focus();
    }

    private void Refresh(bool animateRoll = false)
    {
        Surface.SetPracticeRunning(_session.IsRunning);
        Surface.SetPracticePosition(_practiceSequence, _session.Index);
        var selectedIndex = Math.Min(_session.Index, _session.Steps.Count - 1);
        Surface.SetPausedSelection(selectedIndex, _session.UsesClock);
        if (selectedIndex >= 0 && (!_session.UsesClock || !_session.IsRunning))
        {
            var step = _session.Steps[selectedIndex];
            var tick = _session.UsesClock && !_session.IsPaused
                ? _session.GetTickAt(step.Offset - PracticeSession.PreRoll)
                : step.Tick;
            Surface.SetRollCursorTick(tick, animateRoll);
        }
        AnimatePracticePlayIcon(_session.IsRunning);
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
        ComboText.Text = _session.Combo.ToString();
        AccuracyText.Text = _session.Attempts == 0 ? "—" : $"{_session.Hits * 100d / _session.Attempts:0}%";
    }

    private void Status(string key) => PracticeStatusText.Text = FindResource(key)?.ToString() ?? key;

    private void GameKeysModeButton_OnClick(object sender, RoutedEventArgs e) =>
        SetMode(PracticeSurfaceMode.GameKeys);

    private void VerticalRollModeButton_OnClick(object sender, RoutedEventArgs e) =>
        SetMode(PracticeSurfaceMode.VerticalRoll);

    private void SetMode(PracticeSurfaceMode mode)
    {
        if (mode != PracticeSurfaceMode.VerticalRoll && _session.IsRhythmGame)
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
        CancelTimer();
        if (!_session.Pause()) return;
        Refresh(animateRoll: true);
        Status("Practice_Paused");
    }

    private void RhythmGameToggle_OnClick(object sender, RoutedEventArgs e) =>
        SetRhythmGame(RhythmGameToggle.IsChecked == true);

    private void SetRhythmGame(bool enabled)
    {
        PausePractice();
        _session.SetRhythmGame(enabled && Surface.Mode == PracticeSurfaceMode.VerticalRoll);
        RhythmGameToggle.IsChecked = _session.IsRhythmGame;
        Surface.SetRhythmGame(_session.IsRhythmGame);
        Surface.SetTimedApproach(_session.UsesClock);
        AnimateRhythmGameControls();
        Status("Practice_Ready");
        _ = PositionAtCurrentAsync();
        Surface.Focus();
    }

    private async void ReturnToBookmark_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_session.ReturnToBookmark()) return;
        CancelTimer();
        Surface.ClearPressedKeys();
        Status("Practice_Paused");
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
        if (!_session.IsRhythmGame) return;
        PausePractice();
        var dialog = new GenshinPiano.App.Dialogs.PracticeLatencyDialog(_session.InputDelayMs)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true)
        {
            _session.InputDelayMs = dialog.DelayMilliseconds;
            if (System.Windows.Application.Current is App app)
                app.UserSettingsService.SetPracticeInputDelay(_session.InputDelayMs);
        }
        Surface.Focus();
    }

    private void AnimateRhythmGameControls()
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        CalibrationButton.IsEnabled = _session.IsRhythmGame;
        RhythmGameSettingsButton.IsEnabled = _session.IsRhythmGame;
        CalibrationReveal.BeginAnimation(WidthProperty,
            new DoubleAnimation(_session.IsRhythmGame ? 60 : 0, duration) { EasingFunction = easing });
        CalibrationReveal.BeginAnimation(OpacityProperty,
            new DoubleAnimation(_session.IsRhythmGame ? 1 : 0, duration) { EasingFunction = easing });
        if (CalibrationButton.RenderTransform is TranslateTransform translate)
            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(_session.IsRhythmGame ? 0 : 10, duration) { EasingFunction = easing });
        if (RhythmGameSettingsButton.RenderTransform is TranslateTransform settingsTranslate)
            settingsTranslate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(_session.IsRhythmGame ? 0 : 18, duration) { EasingFunction = easing });
        RhythmGameNoteIcon.BeginAnimation(OpacityProperty,
            new DoubleAnimation(_session.IsRhythmGame ? 1 : .78, duration) { EasingFunction = easing });
    }

    private void RhythmGameSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_session.IsRhythmGame || System.Windows.Application.Current is not App app) return;
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
