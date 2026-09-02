using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public partial class PianoRollEditor : UserControl
{
    private static readonly double[] RhythmFactors = [4, 2, 1, 0.5, 0.25, 0.125];

    private bool _updatingNewNoteLength;
    private bool _updatingNoteEditor;
    private bool _isDraggingGateRatioPreview;
    private double _gateRatioPercentage = 80;
    private bool _loadingSettings;
    private IUserSettingsService? _settingsService;
    private IThemeService? _themeService;
    private double _themeHorizontalOffset;
    private double _themeVerticalOffset;
    private ScoreAuditionService? _auditionService;
    private CancellationTokenSource? _auditionCancellation;
    private CancellationTokenSource? _notePreviewCancellation;
    private Task _notePreviewTask = Task.CompletedTask;
    private bool _auditionIsPlaying;
    private bool _auditionPauseRequested;
    private bool _isDraggingAuditionVolume;
    private bool _isClosingAuditionVolume;
    private bool _isClosingBpmEditor;
    private Window? _ownerWindow;
    private long _auditionTick;
    private bool _continuousFollowActive;
    private bool _playbackScrollAnimationActive;
    private long _lastFollowTick = -1;
    private ComboBoxItem? _customNewNoteLengthItem;
    private string _frameRateMode = "VSync";
    private AuditionProgress? _latestAuditionProgress;
    private long _auditionProgressTimestamp;
    private long _nextAuditionFrameTimestamp;
    private long _lastAuditionTextTimestamp;
    private bool _auditionRenderingAttached;

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
        Surface.NoteCreated += Surface_OnNoteCreated;
        Surface.NoteRhythmPreviewChanged += Surface_OnNoteRhythmPreviewChanged;
        Surface.ZoomChanged += Surface_OnZoomChanged;
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

    public int ShiftAllNotesInGenshinRange(int keySteps) =>
        Surface.ShiftAllNotesInGenshinRange(keySteps);

    public GenshinRangeMappingResult? MapToGenshinRange() => Surface.MapToGenshinRange();

    public ScoreCleanupResult? ApplyScoreCleanup(ScoreCleanupOptions options) =>
        Surface.ApplyScoreCleanup(options);

    public int GenerateShortPressDurations() => Surface.GenerateShortPressDurations();

    public PitchLabelMode PitchLabelMode => KeyboardLabels.LabelMode;

    public NoteArticulation CurrentArticulation =>
        Surface.SelectedArticulation ?? Surface.DefaultArticulation;

    public int SelectAllNotes() => Surface.SelectAllNotes();

    public int SelectNotesBeforeCursor() => Surface.SelectNotesRelativeToPlaybackCursor(false);

    public int SelectNotesAfterCursor() => Surface.SelectNotesRelativeToPlaybackCursor(true);

    public void UndoEdit() => Surface.UndoEdit();

    public void RedoEdit() => Surface.RedoEdit();

    public void SetPitchLabelMode(PitchLabelMode mode)
    {
        KeyboardLabels.LabelMode = mode;
        if (!_loadingSettings)
        {
            _settingsService?.SetPitchLabelMode(mode.ToString());
        }
    }

    public void SetArticulation(NoteArticulation articulation)
    {
        Surface.DefaultArticulation = articulation;
        _settingsService?.SetDefaultArticulation(articulation.ToString());
        Surface.SetSelectedArticulation(articulation);
    }

    public void SetPitchLayoutMode(PianoRollPitchLayoutMode mode) =>
        SelectComboItem(PitchLayoutComboBox, mode.ToString());

    private void PitchLayoutComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Surface is null ||
            PitchLayoutComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<PianoRollPitchLayoutMode>(tag, out var mode))
        {
            return;
        }

        Surface.PitchLayoutMode = mode;
        UpdateSampleInstrumentAvailability(mode);
        if (!_loadingSettings)
        {
            _settingsService?.SetPianoRollLayoutMode(mode.ToString());
        }
        UpdateHiddenNotesIndicator();
        Dispatcher.BeginInvoke(() =>
        {
            if (mode == PianoRollPitchLayoutMode.Genshin21)
            {
                EditorScrollViewer.ScrollToVerticalOffset(0);
                return;
            }

            var rows = PianoRollPitchLayouts.GetRows(
                mode,
                Score?.Tracks.SelectMany(track => track.Notes).Select(note => note.Pitch));
            var focusPitch = Surface.SelectedNote?.Pitch ??
                             Score?.Tracks.SelectMany(track => track.Notes)
                                 .Select(note => note.Pitch)
                                 .Where(pitch => pitch is >= 21 and <= 108)
                                 .DefaultIfEmpty(60)
                                 .First() ?? 60;
            var row = Enumerable.Range(0, rows.Count)
                .MinBy(index => Math.Abs(rows[index].Pitch - focusPitch));
            var offset = PianoRollSurface.RulerHeight + row * Surface.RowHeight -
                         EditorScrollViewer.ViewportHeight / 2;
            EditorScrollViewer.ScrollToVerticalOffset(Math.Max(0, offset));
        }, DispatcherPriority.Loaded);
    }

    private void UpdateHiddenNotesIndicator()
    {
        if (HiddenNotesText is null || Surface is null)
        {
            return;
        }

        var count = Surface.HiddenNoteCount;
        HiddenNotesText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HiddenNotesText.Text = count > 0
            ? string.Format((string)FindResource("Editor_HiddenNotes"), count)
            : string.Empty;
    }

    public void SetGamePlaybackActive(bool isActive)
    {
        Surface.IsEditingEnabled = !isActive;
        PlaybackCursor.Visibility = Visibility.Collapsed;
    }

    public void SetFrameRateMode(string mode)
    {
        if (mode is not ("30" or "60" or "VSync"))
        {
            return;
        }

        _frameRateMode = mode;
        _nextAuditionFrameTimestamp = 0;
    }

    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomIn();

    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomOut();

    private void VerticalZoomIn_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomRowsIn();

    private void VerticalZoomOut_OnClick(object sender, RoutedEventArgs e) => Surface.ZoomRowsOut();

    private void SelectBeforeCursor_OnClick(object sender, RoutedEventArgs e) => SelectNotesBeforeCursor();

    private void SelectAfterCursor_OnClick(object sender, RoutedEventArgs e) => SelectNotesAfterCursor();

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

    private void NewNoteLengthComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingNewNoteLength || Surface is null ||
            NewNoteLengthComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !double.TryParse(
                tag,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var factor))
        {
            return;
        }

        Surface.NewNoteLengthFactor = factor;
        if (!_loadingSettings && !_updatingNewNoteLength)
        {
            _settingsService?.SetNewNoteLengthFactor(factor);
        }
    }

    private void SetNewNoteLength(long rhythmTick, int ppq) =>
        SetNewNoteLength(rhythmTick / (double)Math.Max(1, ppq), commit: true);

    private void SetNewNoteLength(double factor) => SetNewNoteLength(factor, commit: true);

    private void SetNewNoteLength(double factor, bool commit)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return;
        }

        _updatingNewNoteLength = true;
        try
        {
            var matchingItem = NewNoteLengthComboBox.Items
                .OfType<ComboBoxItem>()
                .Where(item => !ReferenceEquals(item, _customNewNoteLengthItem))
                .FirstOrDefault(item =>
                    item.Tag is string tag &&
                    double.TryParse(
                        tag,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var itemFactor) &&
                    Math.Abs(itemFactor - factor) < 0.000001);

            if (matchingItem is null)
            {
                if (_customNewNoteLengthItem is not null)
                {
                    NewNoteLengthComboBox.Items.Remove(_customNewNoteLengthItem);
                }

                _customNewNoteLengthItem = new ComboBoxItem
                {
                    Content = FormatMusicalLength(factor),
                    Tag = factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                };
                NewNoteLengthComboBox.Items.Add(_customNewNoteLengthItem);
                matchingItem = _customNewNoteLengthItem;
            }
            else if (_customNewNoteLengthItem is not null)
            {
                NewNoteLengthComboBox.Items.Remove(_customNewNoteLengthItem);
                _customNewNoteLengthItem = null;
            }

            NewNoteLengthComboBox.SelectedItem = matchingItem;
            if (commit)
            {
                Surface.NewNoteLengthFactor = factor;
            }
        }
        finally
        {
            _updatingNewNoteLength = false;
        }

        if (commit && !_loadingSettings)
        {
            _settingsService?.SetNewNoteLengthFactor(factor);
        }
    }

    private void Surface_OnNoteRhythmPreviewChanged(
        object? sender,
        NoteRhythmPreviewChangedEventArgs e)
    {
        if (Score is null)
        {
            return;
        }

        SetNewNoteLength(
            e.RhythmTick / (double)Math.Max(1, Score.Timing.Ppq),
            commit: e.IsCommitted);
    }

    private static string FormatMusicalLength(double factor)
    {
        var wholeNoteFraction = factor / 4d;
        var bestDenominator = 1;
        var bestNumerator = Math.Max(1, (int)Math.Round(wholeNoteFraction));
        var bestError = double.MaxValue;
        for (var denominator = 1; denominator <= 128; denominator++)
        {
            var numerator = Math.Max(1, (int)Math.Round(wholeNoteFraction * denominator));
            var error = Math.Abs(numerator / (double)denominator - wholeNoteFraction);
            if (error < bestError)
            {
                bestNumerator = numerator;
                bestDenominator = denominator;
                bestError = error;
            }
            if (error < 0.000001)
            {
                break;
            }
        }

        var divisor = GreatestCommonDivisor(bestNumerator, bestDenominator);
        return $"{bestNumerator / divisor}/{bestDenominator / divisor}";
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }
        return Math.Max(1, Math.Abs(left));
    }

    private void EditorScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        Surface.SetViewport(
            e.HorizontalOffset,
            e.VerticalOffset,
            e.ViewportWidth,
            e.ViewportHeight);
        PlaybackCursorHeadTransform.Y = e.VerticalOffset;
        if (e.VerticalChange != 0)
        {
            KeyboardScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        }
    }

    private void Surface_OnSelectedNoteChanged(object? sender, EventArgs e)
    {
        if (Surface.SelectedNoteCount == 0)
        {
            SelectionLoopToggle.IsChecked = false;
            return;
        }

        if (Surface.SelectedNoteCount == 1 &&
            Surface.SelectedNote is { } selectedNote &&
            Score is { } selectedScore)
        {
            SetNewNoteLength(
                Math.Max(1, selectedNote.RhythmTick ?? selectedNote.DurationTick),
                selectedScore.Timing.Ppq);
        }

        if (NoteEditorPopup.IsOpen && Surface.SelectedNote is { } note && Score is { } score)
        {
            SelectRhythmOption(note.RhythmTick ?? note.DurationTick, score.Timing.Ppq);
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

        if (System.Windows.Application.Current is not GenshinPiano.App.App app)
        {
            return;
        }

        if (_themeService is null)
        {
            _themeService = app.ThemeService;
            _themeService.ThemeChanging += ThemeService_OnThemeChanging;
            _themeService.ThemeChanged += ThemeService_OnThemeChanged;
        }

        if (_settingsService is not null)
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

            SetNewNoteLength(editor.NewNoteLengthFactor);

            if (Enum.TryParse<NoteArticulation>(editor.DefaultArticulation, out var articulation))
            {
                Surface.DefaultArticulation = articulation;
            }

            if (Enum.TryParse<PitchLabelMode>(editor.PitchLabelMode, out var pitchLabelMode))
            {
                SetPitchLabelMode(pitchLabelMode);
            }

            NaturalSustainCheckBox.IsChecked = editor.NaturalSustain;
            SelectComboItem(AuditionInstrumentComboBox, editor.AuditionInstrument.ToString());
            AuditionVolumeSlider.Value = editor.AuditionVolume / 100d;
            SetAuditionVolume(editor.AuditionVolume);
            SetFrameRateMode(editor.PianoRollFrameRate);
            if (Enum.TryParse<PianoRollPitchLayoutMode>(editor.PianoRollLayoutMode, out var layoutMode))
            {
                SelectComboItem(PitchLayoutComboBox, layoutMode.ToString());
                Surface.PitchLayoutMode = layoutMode;
            }
            Surface.PixelsPerBeat = editor.PianoRollPixelsPerBeat;
            Surface.RowHeight = editor.PianoRollRowHeight;
        }
        finally
        {
            _loadingSettings = false;
        }
        if (AuditionInstrumentComboBox.SelectedItem is ComboBoxItem { Tag: string selectedTag } &&
            int.TryParse(selectedTag, out var selectedInstrument) &&
            selectedInstrument != editor.AuditionInstrument)
        {
            _settingsService.SetAuditionInstrument(selectedInstrument);
        }
    }

    private void PianoRollEditor_OnUnloaded(object sender, RoutedEventArgs e)
    {
        _auditionPauseRequested = false;
        _auditionCancellation?.Cancel();
        _notePreviewCancellation?.Cancel();
        StopAuditionRendering(renderFinalFrame: false);
        AuditionVolumePopup.IsOpen = false;
        if (_themeService is not null)
        {
            _themeService.ThemeChanging -= ThemeService_OnThemeChanging;
            _themeService.ThemeChanged -= ThemeService_OnThemeChanged;
            _themeService = null;
        }
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
        editor.UpdateHiddenNotesIndicator();
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
        var initialSampleTimestamp = Stopwatch.GetTimestamp();
        _latestAuditionProgress = new AuditionProgress(
            _auditionTick,
            playbackEndTick,
            playbackStartPosition,
            playbackEndPosition,
            initialSampleTimestamp);
        _auditionProgressTimestamp = initialSampleTimestamp;
        StartAuditionRendering();
        var progress = new Progress<AuditionProgress>(item =>
        {
            _latestAuditionProgress = item;
            _auditionProgressTimestamp = item.SampleTimestamp > 0
                ? item.SampleTimestamp
                : Stopwatch.GetTimestamp();
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
    }

    private void CompositionTarget_OnRendering(object? sender, EventArgs e) =>
        RenderAuditionFrame(force: false);

    private void RenderAuditionFrame(bool force)
    {
        if (!_auditionIsPlaying || Score is null || _latestAuditionProgress is not { } progress)
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
        var interpolatedPosition = progress.Position + elapsed;
        if (interpolatedPosition > progress.Duration)
        {
            interpolatedPosition = progress.Duration;
        }

        var tick = TimeToTick(interpolatedPosition, Score.Timing, progress.DurationTick);
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

    private static long TimeToTick(TimeSpan time, TimingDefinition timing, long maximumTick)
    {
        long low = 0;
        var high = Math.Max(0, maximumTick);
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (GenshinPiano.Core.Playback.ScorePlaybackPlanner.TickToTime(middle, timing) <= time)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
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
        var dismissedTransientEditor = false;

        if (NoteEditorPopup.IsOpen && !IsPointerOverPopup(NoteEditorPopup))
        {
            NoteEditorPopup.IsOpen = false;
            dismissedTransientEditor = true;
        }

        if (EditorHelpPopup.IsOpen &&
            !EditorHelpButton.IsMouseOver &&
            !IsPointerOverPopup(EditorHelpPopup))
        {
            EditorHelpButton.IsChecked = false;
            dismissedTransientEditor = true;
        }

        if (BpmEditPanel.Visibility == Visibility.Visible &&
            !BpmControlHost.IsMouseOver)
        {
            Keyboard.ClearFocus();
            ApplyBpm();
            BeginCloseBpmEditor();
            dismissedTransientEditor = true;
        }

        if (AuditionVolumePopup.IsOpen &&
            !AuditionVolumeButton.IsMouseOver &&
            !IsPointerOverAuditionVolumePopup())
        {
            BeginCloseAuditionVolumePopup();
            dismissedTransientEditor = true;
        }

        // A click whose first purpose is dismissing a transient editor must not
        // fall through to the piano roll and create a note at the same position.
        if (dismissedTransientEditor && e.ChangedButton == MouseButton.Left && Surface.IsMouseOver)
        {
            e.Handled = true;
        }
    }

    private void OwnerWindow_OnDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            NoteEditorPopup.IsOpen = false;
            EditorHelpButton.IsChecked = false;

            if (BpmEditPanel.Visibility == Visibility.Visible)
            {
                Keyboard.ClearFocus();
                ApplyBpm();
                BeginCloseBpmEditor();
            }

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
        => IsPointerOverPopup(AuditionVolumePopup);

    private static bool IsPointerOverPopup(System.Windows.Controls.Primitives.Popup popup)
    {
        if (popup.Child is not FrameworkElement
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
        BpmEditPanel.Opacity = 1;
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

        EditorScrollViewer.ScrollToHorizontalOffset(Math.Max(0, x - anchor));
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

    private void ThemeService_OnThemeChanging(object? sender, EventArgs e)
    {
        _themeHorizontalOffset = EditorScrollViewer.HorizontalOffset;
        _themeVerticalOffset = EditorScrollViewer.VerticalOffset;
    }

    private void ThemeService_OnThemeChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            EditorScrollViewer.UpdateLayout();
            EditorScrollViewer.ScrollToHorizontalOffset(_themeHorizontalOffset);
            EditorScrollViewer.ScrollToVerticalOffset(_themeVerticalOffset);
        }, DispatcherPriority.ContextIdle);
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
            return;
        }

        // The keyboard-label column is a synchronized view of the piano roll, not an
        // independently scrollable pane. Forward a plain wheel gesture to the main
        // scroll viewer; its ScrollChanged handler then moves both panes together.
        if (KeyboardScrollViewer.IsMouseOver &&
            modifiers == System.Windows.Input.ModifierKeys.None)
        {
            EditorScrollViewer.ScrollToVerticalOffset(
                Math.Max(0, EditorScrollViewer.VerticalOffset - e.Delta));
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
            _gateRatioPercentage = Math.Clamp(
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
        UpdateGateRatioDescription(_gateRatioPercentage);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => GateRatioPreviewTrack.Focus());
    }

    private void UpdateGateRatioDescription(double value)
    {
        var percentage = (int)Math.Round(value);
        GateRatioText.Text = $"{percentage}%";
        UpdateGateRatioPreview(value);
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

    private void GateRatioPreviewTrack_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateGateRatioPreview(_gateRatioPercentage);

    private void GateRatioPreviewTrack_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var x = e.GetPosition(GateRatioPreviewTrack).X;
        var fillEdge = GetGateRatioPreviewFillEdge();
        if (Math.Abs(x - fillEdge) > 10)
        {
            return;
        }

        _isDraggingGateRatioPreview = true;
        GateRatioPreviewTrack.CaptureMouse();
        UpdateGateRatioFromPreviewPointer(e);
        e.Handled = true;
    }

    private void GateRatioPreviewTrack_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(GateRatioPreviewTrack).X;
        GateRatioPreviewTrack.Cursor = _isDraggingGateRatioPreview ||
                                       Math.Abs(x - GetGateRatioPreviewFillEdge()) <= 10
            ? Cursors.SizeWE
            : Cursors.Arrow;
        if (_isDraggingGateRatioPreview)
        {
            UpdateGateRatioFromPreviewPointer(e);
            e.Handled = true;
        }
    }

    private void GateRatioPreviewTrack_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDraggingGateRatioPreview)
        {
            return;
        }

        UpdateGateRatioFromPreviewPointer(e);
        _isDraggingGateRatioPreview = false;
        ApplySelectedGateRatio();
        GateRatioPreviewTrack.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void GateRatioPreviewTrack_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingGateRatioPreview)
        {
            GateRatioPreviewTrack.Cursor = Cursors.Arrow;
        }
    }

    private void GateRatioPreviewTrack_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        var shouldCommit = _isDraggingGateRatioPreview;
        _isDraggingGateRatioPreview = false;
        GateRatioPreviewTrack.Cursor = Cursors.Arrow;
        if (shouldCommit)
        {
            ApplySelectedGateRatio();
        }
    }

    private void UpdateGateRatioFromPreviewPointer(MouseEventArgs e)
    {
        var availableWidth = Math.Max(1, GateRatioPreviewTrack.ActualWidth - 2);
        var x = e.GetPosition(GateRatioPreviewTrack).X - 1;
        SetGateRatioPercentage(Math.Round(Math.Clamp(x / availableWidth * 100, 10, 95)));
    }

    private double GetGateRatioPreviewFillEdge() =>
        1 + Math.Max(0, GateRatioPreviewTrack.ActualWidth - 2) * _gateRatioPercentage / 100d;

    private void SetGateRatioPercentage(double percentage)
    {
        _gateRatioPercentage = Math.Clamp(percentage, 10, 95);
        UpdateGateRatioDescription(_gateRatioPercentage);
    }

    private void UpdateGateRatioPreview(double value)
    {
        if (GateRatioPreviewTrack is null || GateRatioPreviewFill is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, GateRatioPreviewTrack.ActualWidth - 2);
        GateRatioPreviewFill.Width = availableWidth * Math.Clamp(value / 100d, 0.1, 0.95);
    }

    private void GatePreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            double.TryParse(tag, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var percentage))
        {
            SetGateRatioPercentage(percentage);
            ApplySelectedGateRatio();
        }
    }

    private void RhythmLengthListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplySelectedRhythm();

    private void ApplySelectedRhythm()
    {
        if (_updatingNoteEditor || !NoteEditorPopup.IsOpen ||
            Score is null || RhythmLengthListBox.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        var rhythmTick = GetRhythmTick(item, Score.Timing.Ppq);
        Surface.UpdateSelectedRhythm(rhythmTick);
    }

    private void ApplySelectedGateRatio()
    {
        if (_updatingNoteEditor || !NoteEditorPopup.IsOpen)
        {
            return;
        }

        Surface.UpdateSelectedGateRatio(_gateRatioPercentage / 100d);
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
        TryHandleEditorShortcut(e);
    }

    public void TryHandleEditorShortcut(KeyEventArgs e)
    {
        var key = ShortcutKeyResolver.Resolve(e);
        var modifiers = Keyboard.Modifiers;
        var isInputFocused = Keyboard.FocusedElement is TextBox or ComboBox;
        if (!_auditionIsPlaying && !isInputFocused &&
            (modifiers & ModifierKeys.Control) != 0)
        {
            if (key == Key.A)
            {
                Surface.SelectAllNotes();
                e.Handled = true;
                return;
            }

            if ((modifiers & ModifierKeys.Shift) != 0 && key is Key.Left or Key.Right)
            {
                Surface.SelectNotesRelativeToPlaybackCursor(key == Key.Right);
                e.Handled = true;
                return;
            }
        }

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return;
        }

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

        if (key is Key.Left or Key.Right && Surface.SelectedNoteCount > 0)
        {
            Surface.NudgeSelectedHorizontally(
                key == Key.Right ? 1 : -1,
                wholeBeat: (modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
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

    private void Surface_OnZoomChanged(object? sender, EventArgs e)
    {
        if (_loadingSettings) return;
        _settingsService?.SetPianoRollPixelsPerBeat(Surface.PixelsPerBeat);
        _settingsService?.SetPianoRollRowHeight(Surface.RowHeight);
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
            if (Surface.ShiftSelectedRhythms(step, RhythmFactors) &&
                Surface.SelectedNote is { } updatedNote)
            {
                SelectRhythmOption(
                    updatedNote.RhythmTick ?? updatedNote.DurationTick,
                    score.Timing.Ppq);
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
