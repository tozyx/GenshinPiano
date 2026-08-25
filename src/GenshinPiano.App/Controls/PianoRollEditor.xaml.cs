using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public partial class PianoRollEditor : UserControl
{
    private static readonly double[] RhythmFactors = [4, 2, 1, 0.5, 0.25, 0.125];

    private bool _updatingArticulation;
    private bool _updatingNoteEditor;
    private bool _loadingSettings;
    private IUserSettingsService? _settingsService;
    private ScoreAuditionService? _auditionService;
    private CancellationTokenSource? _auditionCancellation;
    private bool _auditionIsPlaying;
    private bool _auditionPauseRequested;
    private bool _isDraggingAuditionVolume;
    private bool _isClosingAuditionVolume;
    private bool _isClosingBpmEditor;
    private Window? _ownerWindow;
    private long _auditionTick;
    private bool _continuousFollowActive;
    private long _lastFollowTick = -1;

    public PianoRollViewModel EditorViewModel { get; } = new();

    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score),
        typeof(ScoreDocument),
        typeof(PianoRollEditor),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnScoreChanged));

    public PianoRollEditor()
    {
        InitializeComponent();
        Surface.AttachViewModel(EditorViewModel);
        Surface.SelectedNoteChanged += Surface_OnSelectedNoteChanged;
        Surface.NoteEditRequested += Surface_OnNoteEditRequested;
        Surface.PlaybackSeekRequested += Surface_OnPlaybackSeekRequested;
        Loaded += PianoRollEditor_OnLoaded;
        Unloaded += PianoRollEditor_OnUnloaded;
    }

    public ScoreDocument? Score
    {
        get => (ScoreDocument?)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public event EventHandler<AuditionStateChangedEventArgs>? AuditionStateChanged;

    public int OptimizeAllNoteDurations() => Surface.OptimizeAllNoteDurations();

    public int GenerateShortPressDurations() => Surface.GenerateShortPressDurations();

    public void SetGamePlaybackActive(bool isActive)
    {
        Surface.IsEditingEnabled = !isActive;
        PlaybackCursor.Visibility = Visibility.Collapsed;
    }

    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomIn();

    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomOut();

    private void VerticalZoomIn_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomRowsIn();

    private void VerticalZoomOut_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomRowsOut();

    private void Undo_OnClick(object sender, RoutedEventArgs e) => Surface.UndoEdit();

    private void Redo_OnClick(object sender, RoutedEventArgs e) => Surface.RedoEdit();

    private void SnapComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Surface is null || SnapComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !int.TryParse(tag, out var division))
        {
            return;
        }

        Surface.SnapDivision = division;
        if (!_loadingSettings)
        {
            _settingsService?.SetSnapDivision(division);
        }
    }

    private void EditorScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        Surface.SetViewport(
            e.HorizontalOffset,
            e.VerticalOffset,
            e.ViewportWidth,
            e.ViewportHeight);
        if (e.VerticalChange != 0)
        {
            KeyboardScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        }
    }

    private void ArticulationComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingArticulation || _loadingSettings || Surface is null ||
            ArticulationComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<NoteArticulation>(tag, out var articulation))
        {
            return;
        }

        Surface.DefaultArticulation = articulation;
        _settingsService?.SetDefaultArticulation(articulation.ToString());
        Surface.SetSelectedArticulation(articulation);
    }

    private void Surface_OnSelectedNoteChanged(object? sender, EventArgs e)
    {
        if (Surface.SelectedNoteCount == 0)
        {
            SelectionLoopToggle.IsChecked = false;
            return;
        }

        var articulation = Surface.SelectedArticulation;
        if (articulation is null)
        {
            return;
        }

        _updatingArticulation = true;
        var matchingItem = ArticulationComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag as string,
                articulation.Value.ToString(),
                StringComparison.Ordinal));
        if (matchingItem is not null)
        {
            ArticulationComboBox.SelectedItem = matchingItem;
        }
        _updatingArticulation = false;

        if (NoteEditorPopup.IsOpen && Surface.SelectedNote is { } note && Score is { } score)
        {
            SelectRhythmOption(note.RhythmTick ?? note.DurationTick, score.Timing.Ppq);
        }
    }

    private void PitchLabelComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyboardLabels is null ||
            PitchLabelComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<PitchLabelMode>(tag, out var mode))
        {
            return;
        }

        KeyboardLabels.LabelMode = mode;
        if (!_loadingSettings)
        {
            _settingsService?.SetPitchLabelMode(mode.ToString());
        }
    }

    private void PianoRollEditor_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ownerWindow is null && Window.GetWindow(this) is { } ownerWindow)
        {
            _ownerWindow = ownerWindow;
            _ownerWindow.PreviewMouseDown += OwnerWindow_OnPreviewMouseDown;
            _ownerWindow.Deactivated += OwnerWindow_OnDeactivated;
        }

        if (_settingsService is not null ||
            System.Windows.Application.Current is not GenshinPiano.App.App app)
        {
            return;
        }

        _settingsService = app.UserSettingsService;
        _auditionService = app.AuditionService;
        AuditionPlayButton.IsEnabled = _auditionService is not null;
        AuditionStopButton.IsEnabled = _auditionService is not null;
        AuditionVolumeButton.IsEnabled = _auditionService is not null;
        SetAuditionVolume(AuditionVolumeSlider.Value * 100);
        var editor = _settingsService.Current.Editor;
        _loadingSettings = true;
        try
        {
            SelectComboItem(SnapComboBox, editor.SnapDivision.ToString());
            Surface.SnapDivision = editor.SnapDivision;

            SelectComboItem(ArticulationComboBox, editor.DefaultArticulation);
            if (Enum.TryParse<NoteArticulation>(editor.DefaultArticulation, out var articulation))
            {
                Surface.DefaultArticulation = articulation;
            }

            SelectComboItem(PitchLabelComboBox, editor.PitchLabelMode);
            if (Enum.TryParse<PitchLabelMode>(editor.PitchLabelMode, out var pitchLabelMode))
            {
                KeyboardLabels.LabelMode = pitchLabelMode;
            }

            NaturalSustainCheckBox.IsChecked = editor.NaturalSustain;
            SelectComboItem(AuditionInstrumentComboBox, editor.AuditionInstrument.ToString());
            AuditionVolumeSlider.Value = editor.AuditionVolume / 100d;
            SetAuditionVolume(editor.AuditionVolume);
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void PianoRollEditor_OnUnloaded(object sender, RoutedEventArgs e)
    {
        _auditionPauseRequested = false;
        _auditionCancellation?.Cancel();
        AuditionVolumePopup.IsOpen = false;
        if (_ownerWindow is not null)
        {
            _ownerWindow.PreviewMouseDown -= OwnerWindow_OnPreviewMouseDown;
            _ownerWindow.Deactivated -= OwnerWindow_OnDeactivated;
            _ownerWindow = null;
        }
    }

    private static void OnScoreChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var editor = (PianoRollEditor)dependencyObject;
        if (e.NewValue is ScoreDocument score && editor.BpmTextBox is not null)
        {
            var bpm = score.Timing.TempoMap.OrderBy(change => change.Tick).FirstOrDefault()?.Bpm ?? 120;
            editor.BpmTextBox.Text = bpm.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        }
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
        var progress = new Progress<AuditionProgress>(item =>
        {
            _auditionTick = item.Tick;
            Surface.PlaybackTick = item.Tick;
            UpdatePlaybackCursor(item.Tick);
            UpdateAuditionPosition(item.Tick, item.Position);
            FollowPlaybackHead(item.Tick);
        });

        try
        {
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

    private void AuditionVolumeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AuditionVolumePopup.IsOpen)
        {
            BeginCloseAuditionVolumePopup();
            return;
        }

        _isClosingAuditionVolume = false;
        AuditionVolumePopup.IsOpen = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            AnimateOpenAuditionVolumePopup);
    }

    private void OwnerWindow_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (BpmEditPanel.Visibility == Visibility.Visible &&
            !BpmControlHost.IsMouseOver)
        {
            Keyboard.ClearFocus();
            ApplyBpm();
            BeginCloseBpmEditor();
        }

        if (AuditionVolumePopup.IsOpen &&
            !AuditionVolumeButton.IsMouseOver &&
            !IsPointerOverAuditionVolumePopup())
        {
            BeginCloseAuditionVolumePopup();
        }
    }

    private void OwnerWindow_OnDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsPointerOverAuditionVolumePopup() &&
                !AuditionVolumeSlider.IsMouseCaptureWithin &&
                !_isDraggingAuditionVolume)
            {
                BeginCloseAuditionVolumePopup();
            }
        });
    }

    private void AnimateOpenAuditionVolumePopup()
    {
        if (AuditionVolumePopupContent.RenderTransform is not System.Windows.Media.ScaleTransform transform)
        {
            return;
        }

        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        transform.ScaleX = 0.08;
        transform.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.08,
                1,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
    }

    private void BeginCloseAuditionVolumePopup()
    {
        if (!AuditionVolumePopup.IsOpen || _isClosingAuditionVolume)
        {
            return;
        }

        _isClosingAuditionVolume = true;
        if (AuditionVolumePopupContent.RenderTransform is not System.Windows.Media.ScaleTransform transform)
        {
            AuditionVolumePopup.IsOpen = false;
            _isClosingAuditionVolume = false;
            return;
        }

        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            transform.ScaleX,
            0.08,
            TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn,
            },
        };
        animation.Completed += (_, _) =>
        {
            AuditionVolumePopup.IsOpen = false;
            _isClosingAuditionVolume = false;
        };
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
    }

    private bool IsPointerOverAuditionVolumePopup()
    {
        if (AuditionVolumePopup.Child is not FrameworkElement
            {
                IsVisible: true,
                ActualWidth: > 0,
                ActualHeight: > 0,
            } popupContent)
        {
            return false;
        }

        var pointer = Mouse.GetPosition(popupContent);
        return pointer.X >= 0 && pointer.X <= popupContent.ActualWidth &&
               pointer.Y >= 0 && pointer.Y <= popupContent.ActualHeight;
    }

    private void AuditionVolumeSlider_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _isDraggingAuditionVolume = true;
        AuditionVolumeSlider.CaptureMouse();
        UpdateAuditionVolumeFromPointer(e);
        e.Handled = true;
    }

    private void AuditionVolumeSlider_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingAuditionVolume)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndAuditionVolumeDrag();
            return;
        }

        UpdateAuditionVolumeFromPointer(e);
        e.Handled = true;
    }

    private void AuditionVolumeSlider_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDraggingAuditionVolume)
        {
            return;
        }

        UpdateAuditionVolumeFromPointer(e);
        EndAuditionVolumeDrag();
        e.Handled = true;
    }

    private void UpdateAuditionVolumeFromPointer(MouseEventArgs e)
    {
        const double trackMargin = 8;
        var usableWidth = Math.Max(1, AuditionVolumeSlider.ActualWidth - trackMargin * 2);
        var x = e.GetPosition(AuditionVolumeSlider).X;
        var normalized = (x - trackMargin) / usableWidth;
        AuditionVolumeSlider.Value = Math.Clamp(normalized, 0, 1);
    }

    private void EndAuditionVolumeDrag()
    {
        _isDraggingAuditionVolume = false;
        _settingsService?.SetAuditionVolume((int)Math.Round(AuditionVolumeSlider.Value * 100));
        if (AuditionVolumeSlider.IsMouseCaptured)
        {
            AuditionVolumeSlider.ReleaseMouseCapture();
        }
    }

    private void AuditionVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetAuditionVolume(e.NewValue * 100);
        UpdateAuditionVolumeValueTrack();
    }

    private void AuditionVolumeSlider_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateAuditionVolumeValueTrack();

    private void UpdateAuditionVolumeValueTrack()
    {
        if (AuditionVolumeValueTrack is null || AuditionVolumeSlider is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, AuditionVolumeSlider.ActualWidth - 12);
        AuditionVolumeValueTrack.Width = availableWidth * AuditionVolumeSlider.Value;
    }

    private void SetAuditionVolume(double percentage)
    {
        _auditionService?.SetVolume((int)Math.Round(Math.Clamp(percentage, 0, 100) * 1.27));
    }

    private static FrameworkElement CreatePauseIcon()
    {
        var panel = new Grid
        {
            Width = 11,
            Height = 13,
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        panel.ColumnDefinitions.Add(new ColumnDefinition());

        var leftBar = new Border
        {
            Width = 3,
            Height = 13,
            CornerRadius = new CornerRadius(0.7),
        };
        var rightBar = new Border
        {
            Width = 3,
            Height = 13,
            CornerRadius = new CornerRadius(0.7),
        };
        leftBar.SetResourceReference(Border.BackgroundProperty, "PrimaryTextBrush");
        rightBar.SetResourceReference(Border.BackgroundProperty, "PrimaryTextBrush");
        Grid.SetColumn(leftBar, 0);
        Grid.SetColumn(rightBar, 2);
        panel.Children.Add(leftBar);
        panel.Children.Add(rightBar);
        return panel;
    }

    private void BpmDisplayButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isClosingBpmEditor = false;
        if (BpmEditLabel.RenderTransform is System.Windows.Media.ScaleTransform labelScale)
        {
            labelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            labelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            labelScale.ScaleX = 1.1;
            labelScale.ScaleY = 1.1;
        }

        BpmDisplayButton.Visibility = Visibility.Collapsed;
        BpmEditPanel.Visibility = Visibility.Visible;
        BpmEditPanel.BeginAnimation(OpacityProperty, null);
        BpmEditPanel.Opacity = 0;
        BpmEditPanel.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0,
                1,
                TimeSpan.FromMilliseconds(150)));
        BpmControlHost.BeginAnimation(WidthProperty, null);
        BpmControlHost.Width = 68;
        BpmControlHost.BeginAnimation(
            WidthProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                68,
                108,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
        if (BpmTextBox.RenderTransform is System.Windows.Media.ScaleTransform transform)
        {
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            transform.ScaleX = 0.08;
            transform.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0.08,
                    1,
                    TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                    },
                });
        }

        BpmTextBox.Focus();
        BpmTextBox.SelectAll();
    }

    private void BpmTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        ApplyBpm();
        BeginCloseBpmEditor();
    }

    private void BeginCloseBpmEditor()
    {
        if (BpmEditPanel.Visibility != Visibility.Visible || _isClosingBpmEditor)
        {
            return;
        }

        _isClosingBpmEditor = true;
        if (BpmTextBox.RenderTransform is not System.Windows.Media.ScaleTransform transform)
        {
            BpmEditPanel.Visibility = Visibility.Collapsed;
            BpmDisplayButton.Visibility = Visibility.Visible;
            _isClosingBpmEditor = false;
            return;
        }

        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            transform.ScaleX,
            0.08,
            TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn,
            },
        };
        BpmControlHost.BeginAnimation(
            WidthProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                BpmControlHost.ActualWidth,
                68,
                TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut,
                },
            });
        if (BpmEditLabel.RenderTransform is System.Windows.Media.ScaleTransform labelScale)
        {
            labelScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.1, 1, TimeSpan.FromMilliseconds(150)));
            labelScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.1, 1, TimeSpan.FromMilliseconds(150)));
        }
        animation.Completed += (_, _) =>
        {
            BpmEditPanel.Visibility = Visibility.Collapsed;
            BpmDisplayButton.Visibility = Visibility.Visible;
            BpmControlHost.BeginAnimation(WidthProperty, null);
            BpmControlHost.Width = 68;
            _isClosingBpmEditor = false;
        };
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
    }

    private void BpmClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        BpmTextBox.Clear();
        BpmTextBox.Focus();
        e.Handled = true;
    }

    private void BpmTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyBpm();
            Surface.Focus();
            e.Handled = true;
        }
    }

    private void ApplyBpm()
    {
        if (Score is null || !double.TryParse(
                BpmTextBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture,
                out var bpm))
        {
            return;
        }

        bpm = Math.Clamp(bpm, 20, 300);
        BpmTextBox.Text = bpm.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        var tempoMap = Score.Timing.TempoMap
            .Where(change => change.Tick != 0)
            .Prepend(new TempoChange { Tick = 0, Bpm = bpm })
            .OrderBy(change => change.Tick)
            .ToList();
        SetCurrentValue(ScoreProperty, Score with
        {
            Timing = Score.Timing with { TempoMap = tempoMap },
        });
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
        if (Score is null)
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

        EditorScrollViewer.ScrollToHorizontalOffset(Math.Max(0, x - anchor));
    }

    private static void SelectComboItem(ComboBox comboBox, string tag)
    {
        var item = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Tag as string,
                tag,
                StringComparison.Ordinal));
        if (item is not null)
        {
            comboBox.SelectedItem = item;
        }
    }

    private void Root_OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (!EditorScrollViewer.IsMouseOver && !KeyboardScrollViewer.IsMouseOver)
        {
            return;
        }

        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        if ((modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) ==
            (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
        {
            if (e.Delta > 0)
            {
                Surface.ZoomRowsIn();
            }
            else
            {
                Surface.ZoomRowsOut();
            }

            e.Handled = true;
            return;
        }

        if ((modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 &&
            (modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
        {
            EditorScrollViewer.ScrollToHorizontalOffset(
                Math.Max(0, EditorScrollViewer.HorizontalOffset - e.Delta));
            e.Handled = true;
        }
    }

    private void Surface_OnNoteEditRequested(object? sender, NoteEditRequestedEventArgs e)
    {
        var score = Score;
        if (score is null)
        {
            return;
        }

        _updatingNoteEditor = true;
        try
        {
            var ppq = score.Timing.Ppq;
            var rhythmTick = e.Note.RhythmTick ?? e.Note.DurationTick;
            SelectRhythmOption(rhythmTick, ppq);

            var gateRatio = e.Note.GateRatio ?? (e.Note.DurationMode == DurationMode.Auto
                ? NoteDurationCalculator.GetGateRatio(e.Note.Articulation)
                : e.Note.DurationTick / (double)Math.Max(1, rhythmTick));
            GateRatioSlider.Value = Math.Clamp(
                gateRatio * 100,
                NoteDurationCalculator.MinimumGateRatio * 100,
                NoteDurationCalculator.MaximumGateRatio * 100);
        }
        finally
        {
            _updatingNoteEditor = false;
        }

        NoteEditorPopup.HorizontalOffset = e.Anchor.X;
        NoteEditorPopup.VerticalOffset = e.Anchor.Y;
        NoteEditorPopup.IsOpen = true;
        UpdateGateRatioDescription(GateRatioSlider.Value);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => GateRatioSlider.Focus());
    }

    private void GateRatioSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GateRatioText is null || GateRatioDescription is null)
        {
            return;
        }

        UpdateGateRatioDescription(e.NewValue);
        ApplyNoteEditorValue();
    }

    private void UpdateGateRatioDescription(double value)
    {
        var percentage = (int)Math.Round(value);
        GateRatioText.Text = $"{percentage}%";
        var descriptionKey = percentage switch
        {
            <= 35 => "Editor_GateHintStaccato",
            <= 60 => "Editor_GateHintDetached",
            <= 87 => "Editor_GateHintNatural",
            _ => "Editor_GateHintLegato",
        };
        GateRatioDescription.Text = System.Windows.Application.Current.TryFindResource(descriptionKey) as string
            ?? string.Empty;
    }

    private void GatePreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            double.TryParse(tag, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var percentage))
        {
            GateRatioSlider.Value = percentage;
        }
    }

    private void RhythmLengthListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyNoteEditorValue();

    private void ApplyNoteEditorValue()
    {
        if (_updatingNoteEditor || !NoteEditorPopup.IsOpen ||
            Score is null || RhythmLengthListBox.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        var rhythmTick = GetRhythmTick(item, Score.Timing.Ppq);
        var gateRatio = GateRatioSlider.Value / 100d;
        Surface.UpdateSelectedDuration(rhythmTick, gateRatio);
    }

    private void NoteEditorPopup_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ShortcutKeyResolver.Resolve(e) == Key.Escape)
        {
            NoteEditorPopup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void Root_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return;
        }

        var key = ShortcutKeyResolver.Resolve(e);
        if (key == Key.Space &&
            Keyboard.FocusedElement is not TextBox &&
            Keyboard.FocusedElement is not ComboBox &&
            _auditionService is not null)
        {
            e.Handled = true;
            _ = ToggleAuditionAsync();
            return;
        }

        if (_auditionIsPlaying)
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox or ComboBox)
        {
            return;
        }

        var step = ShortcutKeyResolver.ResolveBracketStep(e);
        if (step == 0)
        {
            return;
        }

        ApplyRhythmShortcut(step);
        e.Handled = true;
    }

    private void Root_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_auditionIsPlaying)
        {
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return;
        }

        var step = e.Text switch
        {
            "[" or "［" or "【" => 1,
            "]" or "］" or "】" => -1,
            _ => 0,
        };
        if (step == 0)
        {
            return;
        }

        ApplyRhythmShortcut(step);
        e.Handled = true;
    }

    private void ApplyRhythmShortcut(int step)
    {
        if (Surface.SelectedNote is { } note && Score is { } score)
        {
            var currentTick = note.RhythmTick ?? note.DurationTick;
            var currentIndex = FindClosestRhythmIndex(currentTick, score.Timing.Ppq);
            var nextIndex = Math.Clamp(currentIndex + step, 0, RhythmFactors.Length - 1);
            var gateRatio = note.GateRatio ?? (note.DurationMode == DurationMode.Auto
                ? NoteDurationCalculator.GetGateRatio(note.Articulation)
                : note.DurationTick / (double)Math.Max(1, currentTick));
            gateRatio = Math.Clamp(
                gateRatio,
                NoteDurationCalculator.MinimumGateRatio,
                NoteDurationCalculator.MaximumGateRatio);

            if (nextIndex != currentIndex)
            {
                Surface.UpdateSelectedDuration(
                    FactorToTick(RhythmFactors[nextIndex], score.Timing.Ppq),
                    gateRatio);
                SelectRhythmOption(nextIndex);
            }
        }
        else
        {
            var currentIndex = SnapComboBox.SelectedIndex;
            var nextIndex = Math.Clamp(currentIndex + step, 0, SnapComboBox.Items.Count - 1);
            SnapComboBox.SelectedIndex = nextIndex;
        }
    }

    private void SelectRhythmOption(long rhythmTick, int ppq) =>
        SelectRhythmOption(FindClosestRhythmIndex(rhythmTick, ppq));

    private void SelectRhythmOption(int index)
    {
        _updatingNoteEditor = true;
        RhythmLengthListBox.SelectedIndex = index;
        _updatingNoteEditor = false;
    }

    private static int FindClosestRhythmIndex(long rhythmTick, int ppq) =>
        Enumerable.Range(0, RhythmFactors.Length)
            .MinBy(index => Math.Abs(FactorToTick(RhythmFactors[index], ppq) - rhythmTick));

    private static long GetRhythmTick(FrameworkElement item, int ppq)
    {
        var factorText = item.Tag as string ?? "1";
        var factor = double.Parse(factorText, System.Globalization.CultureInfo.InvariantCulture);
        return FactorToTick(factor, ppq);
    }

    private static long FactorToTick(double factor, int ppq) =>
        Math.Max(1, checked((long)Math.Round(ppq * factor)));
}

public sealed class AuditionStateChangedEventArgs(bool isPlaying) : EventArgs
{
    public bool IsPlaying { get; } = isPlaying;
}
