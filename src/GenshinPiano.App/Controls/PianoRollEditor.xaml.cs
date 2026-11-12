using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public partial class PianoRollEditor : UserControl
{
    private static readonly double[] RhythmFactors = [4, 2, 1, 0.5, 0.25, 0.125];

    private bool _updatingNewNoteLength;
    private bool _loadingSettings;
    private IUserSettingsService? _settingsService;
    private IThemeService? _themeService;
    private double _themeHorizontalOffset;
    private double _themeVerticalOffset;
    private ComboBoxItem? _customNewNoteLengthItem;

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
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible && _auditionIsPlaying) PauseAudition();
            if (IsVisible && _settingsService is not null)
            {
                var volume = _settingsService.Current.Editor.AuditionVolume;
                if (Math.Abs(AuditionVolumeSlider.Value * 100 - volume) > 0.1)
                    AuditionVolumeSlider.Value = volume / 100d;
                SetAuditionVolume(volume);
            }
        };
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

    public ScoreAppendResult? AppendScore(ScoreDocument incoming) => Surface.AppendScore(incoming);

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

    private void Root_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        TryHandleEditorShortcut(e);
    }

    public void TryHandleEditorShortcut(KeyEventArgs e)
    {
        if (!IsVisible || e.Handled || e.IsRepeat) return;
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
            Keyboard.FocusedElement is not ComboBox { IsDropDownOpen: true } &&
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

}
