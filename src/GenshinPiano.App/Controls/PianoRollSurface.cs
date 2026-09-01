using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public sealed class PianoRollSurface : Control
{
    public const double RulerHeight = 30;
    public const double DefaultRowHeight = 24;
    private const double MinimumPixelsPerBeat = 48;
    private const double MaximumPixelsPerBeat = 320;

    private IReadOnlyList<PianoRollPitchRow> _rows = PianoRollPitchLayouts.GetRows(
        PianoRollPitchLayoutMode.Genshin21);

    private IReadOnlyList<PianoRollPitchRow> Rows => _rows;

    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score),
        typeof(ScoreDocument),
        typeof(PianoRollSurface),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.AffectsRender |
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnScoreChanged));

    public static readonly DependencyProperty NoteBrushProperty = DependencyProperty.Register(
        nameof(NoteBrush),
        typeof(Brush),
        typeof(PianoRollSurface),
        new FrameworkPropertyMetadata(Brushes.CornflowerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight),
        typeof(double),
        typeof(PianoRollSurface),
        new FrameworkPropertyMetadata(
            DefaultRowHeight,
            FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelModeProperty = DependencyProperty.Register(
        nameof(LabelMode),
        typeof(PitchLabelMode),
        typeof(PianoRollSurface),
        new FrameworkPropertyMetadata(PitchLabelMode.LetterWithKey, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PitchLayoutModeProperty = DependencyProperty.Register(
        nameof(PitchLayoutMode),
        typeof(PianoRollPitchLayoutMode),
        typeof(PianoRollSurface),
        new FrameworkPropertyMetadata(
            PianoRollPitchLayoutMode.Genshin21,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnPitchLayoutModeChanged));

    public static readonly DependencyProperty PlaybackTickProperty = DependencyProperty.Register(
        nameof(PlaybackTick),
        typeof(long),
        typeof(PianoRollSurface),
        new FrameworkPropertyMetadata(-1L));

    private PianoRollViewModel _viewModel = new();
    private IReadOnlyList<NoteEvent> _dragOriginals = [];
    private IReadOnlyList<NoteEvent> _dragPreviews = [];
    private Point _dragStart;
    private Rect? _selectionRect;
    private HashSet<Guid> _selectionBeforeMarquee = [];
    private bool _dragHasMoved;
    private bool _clickedWasSelected;
    private Guid? _clickedNoteId;
    private DragMode _dragMode;
    private bool _internalScoreChange;
    private double _pixelsPerBeat = 112;
    private int _snapDivision = 4;
    private double _newNoteLengthFactor = 0.25;
    private double _rulerHoverX = double.NaN;
    private Rect _viewport = Rect.Empty;
    private bool _isDraggingPlaybackCursor;
    private double _playbackCursorMouseDownX;
    private bool _playbackCursorDragMoved;
    private bool _suppressPlaybackResizeCursorUntilExit;

    public NoteArticulation DefaultArticulation { get; set; } = NoteArticulation.Natural;

    public bool IsEditingEnabled { get; set; } = true;

    public PianoRollSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public event EventHandler? SelectedNoteChanged;

    public event EventHandler<NoteEditRequestedEventArgs>? NoteEditRequested;

    public event EventHandler<PlaybackSeekRequestedEventArgs>? PlaybackSeekRequested;

    public event EventHandler<NoteCreatedEventArgs>? NoteCreated;

    public event EventHandler<NoteRhythmPreviewChangedEventArgs>? NoteRhythmPreviewChanged;

    public event EventHandler? ZoomChanged;

    internal void AttachViewModel(PianoRollViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _viewModel.LoadScore(Score);
        InvalidateVisual();
    }

    public ScoreDocument? Score
    {
        get => (ScoreDocument?)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public Brush NoteBrush
    {
        get => (Brush)GetValue(NoteBrushProperty);
        set => SetValue(NoteBrushProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set
        {
            var normalized = Math.Clamp(value, 18, 42);
            if (Math.Abs(RowHeight - normalized) < 0.01) return;
            SetValue(RowHeightProperty, normalized);
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public PitchLabelMode LabelMode
    {
        get => (PitchLabelMode)GetValue(LabelModeProperty);
        set => SetValue(LabelModeProperty, value);
    }

    public PianoRollPitchLayoutMode PitchLayoutMode
    {
        get => (PianoRollPitchLayoutMode)GetValue(PitchLayoutModeProperty);
        set => SetValue(PitchLayoutModeProperty, value);
    }

    public int HiddenNoteCount => Score?.Tracks.SelectMany(track => track.Notes)
        .Count(note => PitchToRow(note.Pitch) < 0) ?? 0;

    public long PlaybackTick
    {
        get => (long)GetValue(PlaybackTickProperty);
        set => SetValue(PlaybackTickProperty, value);
    }

    public double PixelsPerBeat
    {
        get => _pixelsPerBeat;
        set
        {
            var normalized = Math.Clamp(value, MinimumPixelsPerBeat, MaximumPixelsPerBeat);
            if (Math.Abs(_pixelsPerBeat - normalized) < 0.01)
            {
                return;
            }

            _pixelsPerBeat = normalized;
            InvalidateMeasure();
            InvalidateVisual();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SnapDivision
    {
        get => _snapDivision;
        set
        {
            if (value is not (1 or 2 or 4 or 8))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _snapDivision = value;
            InvalidateVisual();
        }
    }

    public double NewNoteLengthFactor
    {
        get => _newNoteLengthFactor;
        set
        {
            if (!double.IsFinite(value) || value is <= 0 or > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _newNoteLengthFactor = value;
        }
    }

    public void SetViewport(double left, double top, double width, double height)
    {
        var viewport = new Rect(
            Math.Max(0, left),
            Math.Max(0, top),
            Math.Max(1, width),
            Math.Max(1, height));
        if (_viewport == viewport)
        {
            return;
        }

        _viewport = viewport;
        InvalidateVisual();
    }

    public void ZoomIn() => PixelsPerBeat *= 1.2;

    public void ZoomOut() => PixelsPerBeat /= 1.2;

    public void ZoomRowsIn() => RowHeight *= 1.12;

    public void ZoomRowsOut() => RowHeight /= 1.12;

    public NoteArticulation? SelectedArticulation => Score?.Tracks
        .SelectMany(track => track.Notes)
        .FirstOrDefault(note => note.Id == _viewModel.Selection.PrimaryId)
        ?.Articulation;

    public NoteEvent? SelectedNote => Score?.Tracks
        .SelectMany(track => track.Notes)
        .FirstOrDefault(note => note.Id == _viewModel.Selection.PrimaryId);

    public int SelectedNoteCount => _viewModel.Selection.Count;

    public bool TryGetSelectedTickRange(out long startTick, out long endTick)
    {
        var notes = GetSelectedNotes();
        if (notes.Count == 0 || Score is null)
        {
            startTick = 0;
            endTick = 0;
            return false;
        }

        startTick = notes.Min(note => note.StartTick);
        endTick = notes.Max(note => checked(
            note.StartTick + Math.Max(1, note.RhythmTick ?? note.DurationTick)));
        return endTick > startTick;
    }

    public bool CanUndo => _viewModel.CanUndo;

    public bool CanRedo => _viewModel.CanRedo;

    public int SelectAllNotes()
    {
        if (!IsEditingEnabled || Score is null)
        {
            return 0;
        }

        var notes = Score.Tracks.SelectMany(track => track.Notes)
            .Where(note => PitchToRow(note.Pitch) >= 0).ToArray();
        _viewModel.Selection.ReplaceWith(notes.Select(note => note.Id));
        RaiseSelectionChanged();
        return notes.Length;
    }

    public int SelectNotesRelativeToPlaybackCursor(bool afterCursor)
    {
        if (!IsEditingEnabled || Score is null || PlaybackTick < 0)
        {
            return 0;
        }

        var notes = Score.Tracks
            .SelectMany(track => track.Notes)
            .Where(note => afterCursor
                ? note.StartTick >= PlaybackTick
                : note.StartTick < PlaybackTick)
            .ToArray();
        _viewModel.Selection.ReplaceWith(notes.Select(note => note.Id));
        RaiseSelectionChanged();
        return notes.Length;
    }

    public bool NudgeSelectedHorizontally(int direction, bool wholeBeat = false)
    {
        var notes = GetSelectedNotes();
        if (!IsEditingEnabled || Score is null || notes.Count == 0 || direction is not (-1 or 1))
        {
            return false;
        }

        var deltaTick = direction * (wholeBeat ? Score.Timing.Ppq : GetSnapTick(Score));
        if (deltaTick < 0)
        {
            deltaTick = Math.Max(deltaTick, -notes.Min(note => note.StartTick));
        }

        if (deltaTick == 0)
        {
            return false;
        }

        ReplaceNotes(notes.Select(note => note with
        {
            StartTick = note.StartTick + deltaTick,
        }).ToArray());
        RaiseSelectionChanged();
        return true;
    }

    public int OptimizeAllNoteDurations()
    {
        if (!IsEditingEnabled || Score is null)
        {
            return 0;
        }

        var noteCount = _viewModel.OptimizeAllNoteDurations();
        if (noteCount == 0)
        {
            return 0;
        }

        SynchronizeViewModelScore();
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return noteCount;
    }

    public int GenerateShortPressDurations()
    {
        if (!IsEditingEnabled || Score is null)
        {
            return 0;
        }

        var noteCount = _viewModel.GenerateShortPressDurations();
        if (noteCount == 0)
        {
            return 0;
        }

        SynchronizeViewModelScore();
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return noteCount;
    }

    public int ShiftAllNotesInGenshinRange(int keySteps)
    {
        if (!IsEditingEnabled || Score is null)
        {
            return 0;
        }

        var noteCount = _viewModel.ShiftAllNotesInGenshinRange(keySteps);
        if (noteCount == 0)
        {
            return 0;
        }

        SynchronizeViewModelScore();
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return noteCount;
    }

    public GenshinRangeMappingResult? MapToGenshinRange()
    {
        var result = _viewModel.MapToGenshinRange();
        if (result is not null && _viewModel.Score is not null)
        {
            SetScoreInternally(_viewModel.Score);
        }
        return result;
    }

    public ScoreCleanupResult? ApplyScoreCleanup(ScoreCleanupOptions options)
    {
        if (!IsEditingEnabled || Score is null)
        {
            return null;
        }

        var result = _viewModel.ApplyScoreCleanup(options);
        if (result is null)
        {
            return null;
        }

        SynchronizeViewModelScore();
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public void UndoEdit()
    {
        if (IsEditingEnabled)
        {
            Undo();
        }
    }

    public void RedoEdit()
    {
        if (IsEditingEnabled)
        {
            Redo();
        }
    }

    public bool SetSelectedArticulation(NoteArticulation articulation)
    {
        var notes = GetSelectedNotes();
        if (!IsEditingEnabled)
        {
            return false;
        }
        if (notes.Count == 0 || Score is null)
        {
            return false;
        }

        if (!_viewModel.SetSelectedArticulation(articulation))
        {
            return false;
        }

        SynchronizeViewModelScore();
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool UpdateSelectedRhythm(long rhythmTick)
    {
        var notes = GetSelectedNotes();
        if (!IsEditingEnabled || notes.Count == 0 || rhythmTick <= 0)
        {
            return false;
        }

        if (!_viewModel.UpdateSelectedRhythm(rhythmTick))
        {
            return false;
        }

        return CompleteSelectedNoteEdit();
    }

    public bool UpdateSelectedGateRatio(double gateRatio)
    {
        var notes = GetSelectedNotes();
        if (!IsEditingEnabled || notes.Count == 0 ||
            gateRatio is < NoteDurationCalculator.MinimumGateRatio or
                > NoteDurationCalculator.MaximumGateRatio)
        {
            return false;
        }

        if (!_viewModel.UpdateSelectedGateRatio(gateRatio, ResolveArticulation(gateRatio)))
        {
            return false;
        }

        return CompleteSelectedNoteEdit();
    }

    public bool ShiftSelectedRhythms(int step, IReadOnlyList<double> rhythmFactors)
    {
        if (!IsEditingEnabled || GetSelectedNotes().Count == 0 ||
            !_viewModel.ShiftSelectedRhythms(step, rhythmFactors))
        {
            return false;
        }

        return CompleteSelectedNoteEdit();
    }

    private bool CompleteSelectedNoteEdit()
    {
        SynchronizeViewModelScore();
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var ppq = Score?.Timing.Ppq ?? 480;
        var lastTick = Score?.Tracks
            .SelectMany(track => track.Notes)
            .Select(note => note.StartTick + Math.Max(note.DurationTick, note.RhythmTick ?? 0))
            .DefaultIfEmpty(0)
            .Max() ?? 0;
        var minimumTicks = checked((long)ppq * 16);
        var contentTicks = Math.Max(minimumTicks, lastTick + (long)ppq * 4);
        return new Size(
            Math.Max(1, TickToX(contentTicks, ppq)),
            RulerHeight + Rows.Count * RowHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));

        var score = Score;
        if (score is null)
        {
            return;
        }

        DrawRows(drawingContext);
        DrawGrid(drawingContext, score);
        DrawNotes(drawingContext, score);
        DrawFrozenRuler(drawingContext, score);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var point = e.GetPosition(this);
        if (Score is null)
        {
            return;
        }

        if (IsOverRuler(point.Y))
        {
            SetPlaybackCursorFromX(point.X);
            _isDraggingPlaybackCursor = true;
            _playbackCursorMouseDownX = point.X;
            _playbackCursorDragMoved = false;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (!IsEditingEnabled)
        {
            e.Handled = true;
            return;
        }

        var hit = HitTestNote(point);
        if (hit is null)
        {
            _selectionBeforeMarquee = (Keyboard.Modifiers & ModifierKeys.Control) != 0
                ? [.. _viewModel.Selection.Ids]
                : [];
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                ClearSelection();
            }

            _dragStart = point;
            _selectionRect = new Rect(point, point);
            _dragHasMoved = false;
            _dragMode = DragMode.Marquee;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _clickedNoteId = hit.Value.Note.Id;
        _clickedWasSelected = _viewModel.Selection.Contains(hit.Value.Note.Id);
        if (!control && !_clickedWasSelected)
        {
            _viewModel.Selection.Clear();
        }

        if (!_clickedWasSelected)
        {
            _viewModel.Selection.Add(hit.Value.Note.Id);
        }

        _viewModel.Selection.MakePrimary(hit.Value.Note.Id);
        RaiseSelectionChanged();
        _dragOriginals = GetSelectedNotes();
        _dragPreviews = _dragOriginals;
        _dragStart = point;
        _dragHasMoved = false;
        _dragMode = IsNearRightEdge(point, hit.Value.Bounds)
            ? DragMode.ResizeRhythm
            : control ? DragMode.Copy : DragMode.Move;
        if (_dragMode == DragMode.ResizeRhythm)
        {
            _dragOriginals = [hit.Value.Note];
            _dragPreviews = _dragOriginals;
            Cursor = Cursors.SizeWE;
        }
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!IsEditingEnabled)
        {
            e.Handled = true;
            return;
        }

        Focus();

        var point = e.GetPosition(this);
        var hit = !IsOverRuler(point.Y) && Score is not null
            ? HitTestNote(point)
            : null;
        if (hit is null)
        {
            ClearSelection();
            e.Handled = true;
            return;
        }

        if (!_viewModel.Selection.Contains(hit.Value.Note.Id))
        {
            _viewModel.Selection.SetSingle(hit.Value.Note.Id);
        }
        else
        {
            _viewModel.Selection.MakePrimary(hit.Value.Note.Id);
        }
        RaiseSelectionChanged();
        NoteEditRequested?.Invoke(
            this,
            new NoteEditRequestedEventArgs(
                hit.Value.Note,
                new Point(hit.Value.Bounds.X, hit.Value.Bounds.Bottom + 4)));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_isDraggingPlaybackCursor && Score is not null)
        {
            if (Math.Abs(point.X - _playbackCursorMouseDownX) >= 3)
            {
                _playbackCursorDragMoved = true;
            }
            SetPlaybackCursorFromX(point.X);
            if (_playbackCursorDragMoved)
            {
                Cursor = Cursors.SizeWE;
            }
            e.Handled = true;
            return;
        }

        if (_dragMode == DragMode.None || Score is null)
        {
            var isOverRuler = Score is not null && IsOverRuler(point.Y);
            var cursorX = Score is null || PlaybackTick < 0
                ? double.NaN
                : TickToX(PlaybackTick, Score.Timing.Ppq);
            var isNearPlaybackCursor = isOverRuler &&
                                       !double.IsNaN(cursorX) &&
                                       Math.Abs(point.X - cursorX) <= 7;
            if (_suppressPlaybackResizeCursorUntilExit &&
                (!isOverRuler || double.IsNaN(cursorX) || Math.Abs(point.X - cursorX) > 10))
            {
                _suppressPlaybackResizeCursorUntilExit = false;
            }
            var noteHit = !isOverRuler && IsEditingEnabled ? HitTestNote(point) : null;
            var isNearNoteEdge = noteHit is { } edgeHit && IsNearRightEdge(point, edgeHit.Bounds);
            Cursor = isNearPlaybackCursor && !_suppressPlaybackResizeCursorUntilExit || isNearNoteEdge
                ? Cursors.SizeWE
                : isOverRuler ? Cursors.Hand : Cursors.Arrow;
            var hoverX = isOverRuler
                ? Math.Clamp(point.X, 0, RenderSize.Width)
                : double.NaN;
            if (!double.Equals(_rulerHoverX, hoverX))
            {
                _rulerHoverX = hoverX;
                InvalidateVisual();
            }
            return;
        }

        if (_dragMode == DragMode.Marquee)
        {
            if (!_dragHasMoved && (point - _dragStart).Length < 3)
            {
                return;
            }

            _dragHasMoved = true;
            _selectionRect = NormalizeRect(_dragStart, point);
            UpdateMarqueeSelection(_selectionRect.Value);
        }
        else if (_dragMode == DragMode.ResizeRhythm && _dragOriginals.Count == 1)
        {
            if (!_dragHasMoved && Math.Abs(point.X - _dragStart.X) < 2)
            {
                return;
            }

            _dragHasMoved = true;
            var original = _dragOriginals[0];
            var snap = GetSnapTick(Score);
            var rawRhythmTick = XToTick(point.X, Score.Timing.Ppq) - original.StartTick;
            var rhythmTick = Math.Max(snap, Snap(rawRhythmTick, snap));
            var gateRatio = ResolveGateRatio(original);
            _dragPreviews =
            [
                original with
                {
                    RhythmTick = rhythmTick,
                    DurationTick = Math.Max(1, (long)Math.Round(rhythmTick * gateRatio)),
                    DurationMode = DurationMode.Auto,
                    GateRatio = gateRatio,
                },
            ];
            NoteRhythmPreviewChanged?.Invoke(
                this,
                new NoteRhythmPreviewChangedEventArgs(rhythmTick, false));
        }
        else if (_dragMode is DragMode.Move or DragMode.Copy && _dragOriginals.Count > 0)
        {
            if (!_dragHasMoved && (point - _dragStart).Length < 3)
            {
                return;
            }

            _dragHasMoved = true;
            var anchor = _dragOriginals.First(note => note.Id == _viewModel.Selection.PrimaryId);
            var snap = GetSnapTick(Score);
            var rawDeltaTick = XToTick(point.X - _dragStart.X, Score.Timing.Ppq);
            var anchorStart = Math.Max(0, Snap(anchor.StartTick + rawDeltaTick, snap));
            var deltaTick = anchorStart - anchor.StartTick;
            var minimumStart = _dragOriginals.Min(note => note.StartTick);
            deltaTick = Math.Max(deltaTick, -minimumStart);

            var anchorRow = PitchToRow(anchor.Pitch);
            var requestedRowDelta = PointToRow(point.Y) - anchorRow;
            var minimumRow = _dragOriginals.Min(note => PitchToRow(note.Pitch));
            var maximumRow = _dragOriginals.Max(note => PitchToRow(note.Pitch));
            var rowDelta = Math.Clamp(requestedRowDelta, -minimumRow, Rows.Count - 1 - maximumRow);
            _dragPreviews = _dragOriginals.Select(note => note with
            {
                StartTick = note.StartTick + deltaTick,
                Pitch = Rows[PitchToRow(note.Pitch) + rowDelta].Pitch,
            }).ToArray();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!double.IsNaN(_rulerHoverX))
        {
            _rulerHoverX = double.NaN;
            InvalidateVisual();
        }
        _suppressPlaybackResizeCursorUntilExit = false;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDraggingPlaybackCursor)
        {
            _isDraggingPlaybackCursor = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
            var releasePoint = e.GetPosition(this);
            _rulerHoverX = IsOverRuler(releasePoint.Y)
                ? Math.Clamp(releasePoint.X, 0, RenderSize.Width)
                : double.NaN;
            _suppressPlaybackResizeCursorUntilExit = true;
            Cursor = IsOverRuler(releasePoint.Y) ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_dragMode == DragMode.Marquee)
        {
            _selectionRect = null;
            if (_dragHasMoved)
            {
                RaiseSelectionChanged();
            }
            else
            {
                CreateNote(_dragStart);
            }
        }
        else if (_dragMode == DragMode.ResizeRhythm && _dragHasMoved &&
                 !_dragPreviews.SequenceEqual(_dragOriginals))
        {
            var rhythmTick = Math.Max(
                1,
                _dragPreviews[0].RhythmTick ?? _dragPreviews[0].DurationTick);
            ReplaceNotes(_dragPreviews);
            RaiseSelectionChanged();
            NoteRhythmPreviewChanged?.Invoke(
                this,
                new NoteRhythmPreviewChangedEventArgs(rhythmTick, true));
        }
        else if (_dragHasMoved && !_dragPreviews.SequenceEqual(_dragOriginals) && Score is not null)
        {
            if (_dragMode == DragMode.Copy)
            {
                var primaryIndex = Math.Max(0, _dragOriginals
                    .Select((note, index) => (note, index))
                    .FirstOrDefault(item => item.note.Id == _viewModel.Selection.PrimaryId)
                    .index);
                var copies = _dragPreviews.Select(note => note with { Id = Guid.NewGuid() }).ToArray();
                _viewModel.Selection.ReplaceWith(
                    copies.Select(copy => copy.Id),
                    copies[primaryIndex].Id);
                if (_viewModel.AddNotes(copies))
                {
                    SynchronizeViewModelScore();
                }
                RaiseSelectionChanged();
            }
            else
            {
                ReplaceNotes(_dragPreviews);
            }
        }
        else if (_dragMode == DragMode.Copy && _clickedWasSelected &&
                 _viewModel.Selection.PrimaryId is { } clickedId)
        {
            _viewModel.Selection.Remove(clickedId);
            RaiseSelectionChanged();
        }
        else if (_dragMode == DragMode.Move && !_dragHasMoved && _clickedNoteId is { } clickedNoteId &&
                 _viewModel.Selection.Count > 1)
        {
            _viewModel.Selection.SetSingle(clickedNoteId);
            RaiseSelectionChanged();
        }

        _dragOriginals = [];
        _dragPreviews = [];
        _selectionRect = null;
        _dragMode = DragMode.None;
        _dragHasMoved = false;
        _clickedNoteId = null;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            PixelsPerBeat *= e.Delta > 0 ? 1.12 : 1 / 1.12;
            e.Handled = true;
            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEditingEnabled)
        {
            return;
        }

        var key = ShortcutKeyResolver.Resolve(e);
        if (key is Key.Delete or Key.Back && _viewModel.Selection.Count > 0)
        {
            DeleteSelectedNotes();
            e.Handled = true;
        }
        else if (key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Undo();
            e.Handled = true;
        }
        else if (key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Redo();
            e.Handled = true;
        }
        else if (key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SelectAllNotes();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isDraggingPlaybackCursor = false;
    }

    private void SetPlaybackCursorFromX(double x)
    {
        if (Score is null)
        {
            return;
        }

        var clampedX = Math.Clamp(x, 0, Math.Max(0, RenderSize.Width));
        var tick = Math.Max(0, XToTick(clampedX, Score.Timing.Ppq));
        PlaybackTick = tick;
        PlaybackSeekRequested?.Invoke(this, new PlaybackSeekRequestedEventArgs(tick));
    }

    private void DrawRows(DrawingContext drawingContext)
    {
        var firstRow = _viewport.IsEmpty
            ? 0
            : Math.Max(0, (int)Math.Floor((_viewport.Top - RulerHeight) / RowHeight));
        var lastRow = _viewport.IsEmpty
            ? Rows.Count - 1
            : Math.Min(Rows.Count - 1,
                (int)Math.Ceiling((_viewport.Bottom - RulerHeight) / RowHeight));
        for (var row = firstRow; row <= lastRow; row++)
        {
            if (Rows[row].IsBlackKey || (PitchLayoutMode == PianoRollPitchLayoutMode.Genshin21 && row % 2 == 1))
            {
                drawingContext.DrawRectangle(
                    WithOpacity(Foreground, 0.09),
                    null,
                    new Rect(0, RulerHeight + row * RowHeight, RenderSize.Width, RowHeight));
            }
        }
    }

    private void DrawGrid(DrawingContext drawingContext, ScoreDocument score)
    {
        var ppq = score.Timing.Ppq;
        var snap = GetSnapTick(score);
        var minimumTick = _viewport.IsEmpty ? 0 : Math.Max(0, XToTick(_viewport.Left, ppq));
        var maximumTick = Math.Max(1, XToTick(
            _viewport.IsEmpty ? RenderSize.Width : _viewport.Right,
            ppq));
        var firstGridTick = Math.Max(0, minimumTick - minimumTick % snap);
        var minorPen = new Pen(WithOpacity(BorderBrush, 0.28), 1);
        var beatPen = new Pen(WithOpacity(BorderBrush, 0.60), 1);
        var barPen = new Pen(WithOpacity(Foreground, 0.42), 1.2);

        for (var tick = firstGridTick; tick <= maximumTick; tick += snap)
        {
            var x = TickToX(tick, ppq);
            var isBar = tick % (ppq * 4L) == 0;
            var isBeat = tick % ppq == 0;
            var pen = isBar
                ? barPen
                : isBeat ? beatPen : minorPen;
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
        }

        var rowPen = new Pen(WithOpacity(BorderBrush, 0.35), 1);
        var firstRow = _viewport.IsEmpty
            ? 0
            : Math.Max(0, (int)Math.Floor((_viewport.Top - RulerHeight) / RowHeight));
        var lastRow = _viewport.IsEmpty
            ? Rows.Count
            : Math.Min(Rows.Count,
                (int)Math.Ceiling((_viewport.Bottom - RulerHeight) / RowHeight));
        for (var row = firstRow; row <= lastRow; row++)
        {
            var y = RulerHeight + row * RowHeight;
            drawingContext.DrawLine(rowPen, new Point(0, y), new Point(RenderSize.Width, y));
        }
    }

    private void DrawNotes(DrawingContext drawingContext, ScoreDocument score)
    {
        var movePreviews = _dragMode is DragMode.Move or DragMode.ResizeRhythm
            ? _dragPreviews.ToDictionary(note => note.Id)
            : new Dictionary<Guid, NoteEvent>();
        var visibleLeft = _viewport.IsEmpty ? 0 : _viewport.Left - 4;
        var visibleRight = _viewport.IsEmpty ? RenderSize.Width : _viewport.Right + 4;
        var visibleTop = _viewport.IsEmpty ? RulerHeight : _viewport.Top - RowHeight;
        var visibleBottom = _viewport.IsEmpty ? RenderSize.Height : _viewport.Bottom + RowHeight;
        foreach (var note in score.Tracks.Where(track => !track.IsMuted).SelectMany(track => track.Notes))
        {
            var noteX = TickToX(note.StartTick, score.Timing.Ppq);
            var noteWidth = Math.Max(3, TickToX(GetVisualRhythmTick(note), score.Timing.Ppq));
            var noteRow = PitchToRow(note.Pitch);
            if (noteRow < 0)
            {
                continue;
            }
            var noteY = RulerHeight + noteRow * RowHeight;
            if (noteX + noteWidth < visibleLeft || noteX > visibleRight ||
                noteY + RowHeight < visibleTop || noteY > visibleBottom)
            {
                continue;
            }

            DrawNote(
                drawingContext,
                movePreviews.GetValueOrDefault(note.Id) ?? note,
                score.Timing.Ppq,
                _viewModel.Selection.Contains(note.Id),
                isCopyPreview: false);
        }

        if (_dragMode == DragMode.Copy && _dragHasMoved)
        {
            foreach (var preview in _dragPreviews)
            {
                DrawNote(drawingContext, preview, score.Timing.Ppq, selected: false, isCopyPreview: true);
            }
        }

        if (_selectionRect is { } selectionRect)
        {
            drawingContext.DrawRectangle(
                WithOpacity(NoteBrush, 0.16),
                new Pen(WithOpacity(NoteBrush, 0.9), 1),
                selectionRect);
        }
    }

    private void DrawFrozenRuler(DrawingContext drawingContext, ScoreDocument score)
    {
        var top = _viewport.IsEmpty ? 0 : _viewport.Top;
        var left = _viewport.IsEmpty ? 0 : _viewport.Left;
        var right = _viewport.IsEmpty ? RenderSize.Width : _viewport.Right;
        drawingContext.DrawLine(
            new Pen(WithOpacity(NoteBrush, 0.72), 1),
            new Point(left, top + 0.5),
            new Point(right, top + 0.5));
        var ppq = score.Timing.Ppq;
        var firstTick = Math.Max(0, XToTick(left, ppq));
        var lastTick = Math.Max(firstTick, XToTick(right, ppq));
        var measureTicks = ppq * 4L;
        var snap = GetSnapTick(score);
        var firstSnapTick = Math.Max(0, firstTick - firstTick % snap);
        for (var tick = firstSnapTick; tick <= lastTick; tick += snap)
        {
            var isMeasure = tick % measureTicks == 0;
            var isBeat = tick % ppq == 0;
            var height = isMeasure ? 12 : isBeat ? 8 : 4;
            var opacity = isMeasure ? 0.72 : isBeat ? 0.52 : 0.34;
            var x = TickToX(tick, ppq);
            drawingContext.DrawLine(
                new Pen(WithOpacity(Foreground, opacity), 1),
                new Point(x, top),
                new Point(x, top + height));
        }
        var firstMeasure = firstTick - firstTick % measureTicks;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var tick = firstMeasure; tick <= lastTick; tick += measureTicks)
        {
            var x = TickToX(tick, ppq);
            drawingContext.DrawText(
                new FormattedText(
                    (tick / measureTicks + 1).ToString(),
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
                    11,
                    Foreground,
                    pixelsPerDip),
                new Point(x + 6, top + 14));
        }
        if (!double.IsNaN(_rulerHoverX))
        {
            drawingContext.DrawLine(
                new Pen(WithOpacity(NoteBrush, 0.55), 1),
                new Point(_rulerHoverX, top),
                new Point(_rulerHoverX, top + RulerHeight));
        }
    }

    private void DrawNote(
        DrawingContext drawingContext,
        NoteEvent note,
        int ppq,
        bool selected,
        bool isCopyPreview)
    {
        if (!TryGetNoteBounds(note, ppq, out var bounds))
        {
            return;
        }

        var cornerRadius = Math.Min(3, bounds.Height / 4);
        drawingContext.DrawRoundedRectangle(
            isCopyPreview
                ? WithOpacity(NoteBrush, 0.42)
                : selected ? NoteBrush : WithOpacity(NoteBrush, 0.78),
            isCopyPreview
                ? new Pen(WithOpacity(Foreground, 0.9), 1.2)
                : selected ? new Pen(Foreground, 1.4) : null,
            bounds,
            cornerRadius,
            cornerRadius);

        var entry = Rows[PointToRow(bounds.Y + bounds.Height / 2)];
        var noteLabel = PitchLabelFormatter.FormatNoteLabel(note.Pitch, entry.Key, LabelMode);
        var label = isCopyPreview ? $"+ {noteLabel}" : noteLabel;
        var fontSize = Math.Clamp(bounds.Height - 4, 8, 10);
        var text = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeights.SemiBold, FontStretch),
            fontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var horizontalPadding = bounds.Width < 20 ? 2 : 4;
        if (bounds.Width >= text.WidthIncludingTrailingWhitespace + horizontalPadding * 2)
        {
            var textY = bounds.Y + Math.Max(0, (bounds.Height - text.Height) / 2);
            drawingContext.DrawText(text, new Point(bounds.X + horizontalPadding, textY));
        }
    }

    private NoteHit? HitTestNote(Point point)
    {
        var score = Score;
        if (score is null)
        {
            return null;
        }

        foreach (var note in score.Tracks.SelectMany(track => track.Notes).Reverse())
        {
            if (TryGetNoteBounds(note, score.Timing.Ppq, out var bounds) && bounds.Contains(point))
            {
                return new NoteHit(note, bounds);
            }
        }

        return null;
    }

    private bool TryGetNoteBounds(NoteEvent note, int ppq, out Rect bounds)
    {
        var row = PitchToRow(note.Pitch);
        if (row < 0)
        {
            bounds = Rect.Empty;
            return false;
        }

        var verticalInset = Math.Clamp(RowHeight * 0.10, 1.5, 3);
        bounds = new Rect(
            TickToX(note.StartTick, ppq),
            RulerHeight + row * RowHeight + verticalInset,
            Math.Max(5, TickToX(GetVisualRhythmTick(note), ppq)),
            Math.Max(1, RowHeight - verticalInset * 2));
        return true;
    }

    private void CreateNote(Point point)
    {
        var score = Score!;
        var snap = GetSnapTick(score);
        var rhythmTick = Math.Max(
            1,
            checked((long)Math.Round(score.Timing.Ppq * NewNoteLengthFactor)));
        var note = new NoteEvent
        {
            Pitch = Rows[PointToRow(point.Y)].Pitch,
            StartTick = Math.Max(0, FloorToGrid(XToTick(point.X, score.Timing.Ppq), snap)),
            RhythmTick = rhythmTick,
            DurationMode = DurationMode.Auto,
            Articulation = DefaultArticulation,
            GateRatio = NoteDurationCalculator.GetGateRatio(DefaultArticulation),
            DurationTick = Math.Max(1, (long)Math.Round(
                rhythmTick * NoteDurationCalculator.GetGateRatio(DefaultArticulation))),
        };

        _viewModel.Selection.SetSingle(note.Id);
        if (_viewModel.AddNote(note))
        {
            SynchronizeViewModelScore();
            NoteCreated?.Invoke(this, new NoteCreatedEventArgs(note.Pitch));
        }
        RaiseSelectionChanged();
    }

    private void ReplaceNote(NoteEvent replacement)
        => ReplaceNotes([replacement]);

    private void ReplaceNotes(IReadOnlyCollection<NoteEvent> replacements)
    {
        if (_viewModel.ReplaceNotes(replacements))
        {
            SynchronizeViewModelScore();
        }
    }

    private void DeleteSelectedNotes()
    {
        var score = Score;
        if (score is null || _viewModel.Selection.Count == 0)
        {
            return;
        }

        if (_viewModel.DeleteSelectedNotes())
        {
            SynchronizeViewModelScore();
        }
        RaiseSelectionChanged();
    }

    private IReadOnlyList<NoteEvent> GetSelectedNotes() => _viewModel.GetSelectedNotes();

    private void ClearSelection()
    {
        if (_viewModel.Selection.Count == 0)
        {
            return;
        }

        _viewModel.Selection.Clear();
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void UpdateMarqueeSelection(Rect selectionRect)
    {
        if (Score is null)
        {
            return;
        }

        var selectedIds = new HashSet<Guid>(_selectionBeforeMarquee);
        foreach (var note in Score.Tracks.SelectMany(track => track.Notes))
        {
            if (TryGetNoteBounds(note, Score.Timing.Ppq, out var bounds) && selectionRect.IntersectsWith(bounds))
            {
                selectedIds.Add(note.Id);
            }
        }

        _viewModel.Selection.ReplaceWith(selectedIds);
        RaiseSelectionChanged();
    }

    private void SynchronizeViewModelScore()
    {
        if (_viewModel.Score is null)
        {
            return;
        }

        SetScoreInternally(_viewModel.Score);
    }

    private void Undo()
    {
        if (!_viewModel.Undo() || _viewModel.Score is null)
        {
            return;
        }

        SetScoreInternally(_viewModel.Score);
    }

    private void Redo()
    {
        if (!_viewModel.Redo() || _viewModel.Score is null)
        {
            return;
        }

        SetScoreInternally(_viewModel.Score);
    }

    private void SetScoreInternally(ScoreDocument score)
    {
        _internalScoreChange = true;
        SetCurrentValue(ScoreProperty, score);
        _internalScoreChange = false;
    }

    private long GetSnapTick(ScoreDocument score) => Math.Max(1, score.Timing.Ppq / SnapDivision);

    private static long GetVisualRhythmTick(NoteEvent note) =>
        Math.Max(1, note.RhythmTick ?? note.DurationTick);

    private static bool IsNearRightEdge(Point point, Rect bounds)
    {
        var handleWidth = Math.Clamp(bounds.Width * 0.28, 2, 6);
        return point.Y >= bounds.Top - 2 && point.Y <= bounds.Bottom + 2 &&
               point.X >= bounds.Right - handleWidth && point.X <= bounds.Right;
    }

    private static double ResolveGateRatio(NoteEvent note)
    {
        if (note.GateRatio is double ratio &&
            ratio is >= NoteDurationCalculator.MinimumGateRatio and
                <= NoteDurationCalculator.MaximumGateRatio)
        {
            return ratio;
        }

        var rhythmTick = GetVisualRhythmTick(note);
        return Math.Clamp(
            note.DurationTick / (double)rhythmTick,
            NoteDurationCalculator.MinimumGateRatio,
            NoteDurationCalculator.MaximumGateRatio);
    }

    private double TickToX(long tick, int ppq) => tick * PixelsPerBeat / ppq;

    private long XToTick(double x, int ppq) => checked((long)Math.Round(x * ppq / PixelsPerBeat));

    private static long Snap(long tick, long step) => checked((long)Math.Round(
        tick / (double)step,
        MidpointRounding.AwayFromZero) * step);

    private static long FloorToGrid(long tick, long step) => checked(
        (long)Math.Floor(tick / (double)step) * step);

    private int PointToRow(double y) =>
        Math.Clamp((int)((y - RulerHeight) / RowHeight), 0, Rows.Count - 1);

    private bool IsOverRuler(double y)
    {
        var top = _viewport.IsEmpty ? 0 : _viewport.Top;
        return y >= top && y < top + RulerHeight;
    }

    private static Rect NormalizeRect(Point first, Point second) => new(
        new Point(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
        new Point(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));

    private int PitchToRow(int pitch)
    {
        for (var row = 0; row < Rows.Count; row++)
        {
            if (Rows[row].Pitch == pitch)
            {
                return row;
            }
        }
        return -1;
    }

    private static Brush WithOpacity(Brush? source, double opacity)
    {
        var brush = source?.CloneCurrentValue() ?? Brushes.Transparent.CloneCurrentValue();
        brush.Opacity *= opacity;
        brush.Freeze();
        return brush;
    }

    private static NoteArticulation ResolveArticulation(double gateRatio)
    {
        if (Math.Abs(gateRatio - 0.95) < 0.0001)
        {
            return NoteArticulation.Legato;
        }

        if (Math.Abs(gateRatio - 0.80) < 0.0001)
        {
            return NoteArticulation.Natural;
        }

        if (Math.Abs(gateRatio - 0.50) < 0.0001)
        {
            return NoteArticulation.Detached;
        }

        return Math.Abs(gateRatio - 0.30) < 0.0001
            ? NoteArticulation.Staccato
            : NoteArticulation.Custom;
    }

    private static void OnScoreChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var surface = (PianoRollSurface)dependencyObject;
        surface.RefreshRows();
        var selectionChanged = false;
        if (!surface._internalScoreChange)
        {
            selectionChanged = surface._viewModel.LoadScore(args.NewValue as ScoreDocument);
        }
        else if (args.NewValue is ScoreDocument internalScore)
        {
            selectionChanged = surface._viewModel.SynchronizeScore(internalScore);
        }

        if (selectionChanged)
        {
            surface.SelectedNoteChanged?.Invoke(surface, EventArgs.Empty);
        }

        surface.InvalidateMeasure();
        surface.InvalidateVisual();
    }

    private static void OnPitchLayoutModeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var surface = (PianoRollSurface)dependencyObject;
        surface.RefreshRows();
        var visibleIds = surface.Score?.Tracks.SelectMany(track => track.Notes)
            .Where(note => surface.PitchToRow(note.Pitch) >= 0)
            .Select(note => note.Id)
            .ToHashSet() ?? [];
        var retainedIds = surface._viewModel.Selection.Ids.Where(visibleIds.Contains).ToArray();
        var selectionChanged = retainedIds.Length != surface._viewModel.Selection.Count;
        surface._viewModel.Selection.ReplaceWith(retainedIds);
        if (selectionChanged)
        {
            surface.SelectedNoteChanged?.Invoke(surface, EventArgs.Empty);
        }
        surface.InvalidateMeasure();
        surface.InvalidateVisual();
    }

    private void RefreshRows()
    {
        _rows = PianoRollPitchLayouts.GetRows(
            PitchLayoutMode,
            Score?.Tracks.SelectMany(track => track.Notes).Select(note => note.Pitch));
    }

    private enum DragMode
    {
        None,
        Move,
        Copy,
        Marquee,
        ResizeRhythm,
    }

    private readonly record struct NoteHit(NoteEvent Note, Rect Bounds);
}

public sealed class NoteEditRequestedEventArgs(NoteEvent note, Point anchor) : EventArgs
{
    public NoteEvent Note { get; } = note;

    public Point Anchor { get; } = anchor;
}

public sealed class PlaybackSeekRequestedEventArgs(long tick) : EventArgs
{
    public long Tick { get; } = tick;
}

public sealed class NoteCreatedEventArgs(int pitch) : EventArgs
{
    public int Pitch { get; } = pitch;
}

public sealed class NoteRhythmPreviewChangedEventArgs(long rhythmTick, bool isCommitted) : EventArgs
{
    public long RhythmTick { get; } = rhythmTick;

    public bool IsCommitted { get; } = isCommitted;
}
