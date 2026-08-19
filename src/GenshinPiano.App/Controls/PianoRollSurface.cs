using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public sealed class PianoRollSurface : Control
{
    public const double RulerHeight = 30;
    public const double DefaultRowHeight = 24;
    private const double MinimumPixelsPerBeat = 48;
    private const double MaximumPixelsPerBeat = 320;

    private static readonly (GenshinKey Key, int Pitch)[] Rows =
        GenshinKeyMap.All.Reverse().ToArray();

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

    public static readonly DependencyProperty PlaybackTickProperty = DependencyProperty.Register(
        nameof(PlaybackTick),
        typeof(long),
        typeof(PianoRollSurface),
        new FrameworkPropertyMetadata(-1L));

    private readonly Stack<ScoreDocument> _undo = new();
    private readonly Stack<ScoreDocument> _redo = new();
    private readonly HashSet<Guid> _selectedNoteIds = [];
    private Guid? _primarySelectedNoteId;
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
        set => SetValue(RowHeightProperty, Math.Clamp(value, 18, 42));
    }

    public PitchLabelMode LabelMode
    {
        get => (PitchLabelMode)GetValue(LabelModeProperty);
        set => SetValue(LabelModeProperty, value);
    }

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
        .FirstOrDefault(note => note.Id == _primarySelectedNoteId)
        ?.Articulation;

    public NoteEvent? SelectedNote => Score?.Tracks
        .SelectMany(track => track.Notes)
        .FirstOrDefault(note => note.Id == _primarySelectedNoteId);

    public int SelectedNoteCount => _selectedNoteIds.Count;

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

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int OptimizeAllNoteDurations()
    {
        if (!IsEditingEnabled || Score is null)
        {
            return 0;
        }

        var noteCount = Score.Tracks.Sum(track => track.Notes.Count);
        if (noteCount == 0)
        {
            return 0;
        }

        Commit(NoteDurationCalculator.OptimizeAllDurations(Score));
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return noteCount;
    }

    public int GenerateShortPressDurations()
    {
        if (!IsEditingEnabled || Score is null)
        {
            return 0;
        }

        var noteCount = Score.Tracks.Sum(track => track.Notes.Count);
        if (noteCount == 0)
        {
            return 0;
        }

        Commit(NoteDurationCalculator.GenerateShortPressDurations(Score));
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return noteCount;
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

        ReplaceNotes(notes.Select(note => note with
            {
                DurationMode = DurationMode.Auto,
                Articulation = articulation,
                GateRatio = NoteDurationCalculator.GetGateRatio(articulation),
            }).ToArray());
        SelectedNoteChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool UpdateSelectedDuration(long rhythmTick, double gateRatio)
    {
        var notes = GetSelectedNotes();
        if (!IsEditingEnabled || notes.Count == 0 || rhythmTick <= 0 ||
            gateRatio is < NoteDurationCalculator.MinimumGateRatio or
                > NoteDurationCalculator.MaximumGateRatio)
        {
            return false;
        }

        var articulation = ResolveArticulation(gateRatio);
        ReplaceNotes(notes.Select(note => note with
            {
                RhythmTick = rhythmTick,
                DurationMode = DurationMode.Auto,
                Articulation = articulation,
                GateRatio = gateRatio,
            }).ToArray());
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
            RulerHeight + Rows.Length * RowHeight);
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

        if (point.Y < RulerHeight)
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
            if (e.ClickCount == 2)
            {
                ClearSelection();
                CreateNote(point);
                e.Handled = true;
                return;
            }

            _selectionBeforeMarquee = (Keyboard.Modifiers & ModifierKeys.Control) != 0
                ? [.. _selectedNoteIds]
                : [];
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                ClearSelection();
            }

            _dragStart = point;
            _selectionRect = new Rect(point, point);
            _dragMode = DragMode.Marquee;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        _clickedNoteId = hit.Value.Note.Id;
        _clickedWasSelected = _selectedNoteIds.Contains(hit.Value.Note.Id);
        if (!control && !_clickedWasSelected)
        {
            _selectedNoteIds.Clear();
        }

        if (!_clickedWasSelected)
        {
            _selectedNoteIds.Add(hit.Value.Note.Id);
        }

        _primarySelectedNoteId = hit.Value.Note.Id;
        RaiseSelectionChanged();
        _dragOriginals = GetSelectedNotes();
        _dragPreviews = _dragOriginals;
        _dragStart = point;
        _dragHasMoved = false;
        _dragMode = control
            ? DragMode.Copy
            : DragMode.Move;
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
        var hit = point.Y >= RulerHeight && Score is not null
            ? HitTestNote(point)
            : null;
        if (hit is null)
        {
            return;
        }

        if (!_selectedNoteIds.Contains(hit.Value.Note.Id))
        {
            _selectedNoteIds.Clear();
            _selectedNoteIds.Add(hit.Value.Note.Id);
        }

        _primarySelectedNoteId = hit.Value.Note.Id;
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
            var isOverRuler = Score is not null && point.Y < RulerHeight;
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
            Cursor = isNearPlaybackCursor && !_suppressPlaybackResizeCursorUntilExit
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
            _selectionRect = NormalizeRect(_dragStart, point);
            UpdateMarqueeSelection(_selectionRect.Value);
        }
        else if (_dragMode is DragMode.Move or DragMode.Copy && _dragOriginals.Count > 0)
        {
            if (!_dragHasMoved && (point - _dragStart).Length < 3)
            {
                return;
            }

            _dragHasMoved = true;
            var anchor = _dragOriginals.First(note => note.Id == _primarySelectedNoteId);
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
            var rowDelta = Math.Clamp(requestedRowDelta, -minimumRow, Rows.Length - 1 - maximumRow);
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
            _rulerHoverX = releasePoint.Y < RulerHeight
                ? Math.Clamp(releasePoint.X, 0, RenderSize.Width)
                : double.NaN;
            _suppressPlaybackResizeCursorUntilExit = true;
            Cursor = releasePoint.Y < RulerHeight ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_dragMode == DragMode.Marquee)
        {
            _selectionRect = null;
            RaiseSelectionChanged();
        }
        else if (_dragHasMoved && !_dragPreviews.SequenceEqual(_dragOriginals) && Score is not null)
        {
            if (_dragMode == DragMode.Copy)
            {
                var primaryIndex = Math.Max(0, _dragOriginals
                    .Select((note, index) => (note, index))
                    .FirstOrDefault(item => item.note.Id == _primarySelectedNoteId)
                    .index);
                var copies = _dragPreviews.Select(note => note with { Id = Guid.NewGuid() }).ToArray();
                _selectedNoteIds.Clear();
                foreach (var copy in copies)
                {
                    _selectedNoteIds.Add(copy.Id);
                }

                _primarySelectedNoteId = copies[primaryIndex].Id;
                Commit(ScoreEditor.AddNotes(Score, copies));
                RaiseSelectionChanged();
            }
            else
            {
                ReplaceNotes(_dragPreviews);
            }
        }
        else if (_dragMode == DragMode.Copy && _clickedWasSelected && _primarySelectedNoteId is { } clickedId)
        {
            _selectedNoteIds.Remove(clickedId);
            _primarySelectedNoteId = _selectedNoteIds.Count > 0 ? _selectedNoteIds.First() : null;
            RaiseSelectionChanged();
        }
        else if (_dragMode == DragMode.Move && !_dragHasMoved && _clickedNoteId is { } clickedNoteId &&
                 _selectedNoteIds.Count > 1)
        {
            _selectedNoteIds.Clear();
            _selectedNoteIds.Add(clickedNoteId);
            _primarySelectedNoteId = clickedNoteId;
            RaiseSelectionChanged();
        }

        _dragOriginals = [];
        _dragPreviews = [];
        _selectionRect = null;
        _dragMode = DragMode.None;
        _dragHasMoved = false;
        _clickedNoteId = null;
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
        if (key is Key.Delete or Key.Back && _selectedNoteIds.Count > 0)
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
            ? Rows.Length - 1
            : Math.Min(Rows.Length - 1,
                (int)Math.Ceiling((_viewport.Bottom - RulerHeight) / RowHeight));
        for (var row = firstRow; row <= lastRow; row++)
        {
            if (row % 2 == 1)
            {
                drawingContext.DrawRectangle(
                    WithOpacity(BorderBrush, 0.10),
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

        drawingContext.DrawRectangle(
            WithOpacity(BorderBrush, 0.13),
            null,
            new Rect(0, 0, RenderSize.Width, RulerHeight));

        for (var tick = firstGridTick; tick <= maximumTick; tick += snap)
        {
            var x = TickToX(tick, ppq);
            var isBar = tick % (ppq * 4L) == 0;
            var isBeat = tick % ppq == 0;
            var pen = isBar
                ? barPen
                : isBeat ? beatPen : minorPen;
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));

            var rulerTickHeight = isBar ? 12 : isBeat ? 8 : 4;
            drawingContext.DrawLine(
                new Pen(WithOpacity(Foreground, isBar ? 0.72 : isBeat ? 0.52 : 0.34), 1),
                new Point(x, 0),
                new Point(x, rulerTickHeight));
        }

        var rowPen = new Pen(WithOpacity(BorderBrush, 0.35), 1);
        var firstRow = _viewport.IsEmpty
            ? 0
            : Math.Max(0, (int)Math.Floor((_viewport.Top - RulerHeight) / RowHeight));
        var lastRow = _viewport.IsEmpty
            ? Rows.Length
            : Math.Min(Rows.Length,
                (int)Math.Ceiling((_viewport.Bottom - RulerHeight) / RowHeight));
        for (var row = firstRow; row <= lastRow; row++)
        {
            var y = RulerHeight + row * RowHeight;
            drawingContext.DrawLine(rowPen, new Point(0, y), new Point(RenderSize.Width, y));
        }
        drawingContext.DrawLine(
            new Pen(WithOpacity(NoteBrush, 0.72), 1),
            new Point(0, 0.5),
            new Point(RenderSize.Width, 0.5));

        if (!double.IsNaN(_rulerHoverX))
        {
            drawingContext.DrawLine(
                new Pen(WithOpacity(NoteBrush, 0.55), 1),
                new Point(_rulerHoverX, 0),
                new Point(_rulerHoverX, RulerHeight));
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ticksPerMeasure = ppq * 4L;
        var firstMeasureTick = Math.Max(0, minimumTick - minimumTick % ticksPerMeasure);
        for (var tick = firstMeasureTick; tick <= maximumTick; tick += ticksPerMeasure)
        {
            var text = new FormattedText(
                (tick / ticksPerMeasure + 1).ToString(),
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
                11,
                Foreground,
                pixelsPerDip);
            drawingContext.DrawText(text, new Point(TickToX(tick, ppq) + 6, 14));
        }
    }

    private void DrawNotes(DrawingContext drawingContext, ScoreDocument score)
    {
        var movePreviews = _dragMode == DragMode.Move
            ? _dragPreviews.ToDictionary(note => note.Id)
            : new Dictionary<Guid, NoteEvent>();
        var visibleLeft = _viewport.IsEmpty ? 0 : _viewport.Left - 4;
        var visibleRight = _viewport.IsEmpty ? RenderSize.Width : _viewport.Right + 4;
        var visibleTop = _viewport.IsEmpty ? RulerHeight : _viewport.Top - RowHeight;
        var visibleBottom = _viewport.IsEmpty ? RenderSize.Height : _viewport.Bottom + RowHeight;
        foreach (var note in score.Tracks.Where(track => !track.IsMuted).SelectMany(track => track.Notes))
        {
            var noteX = TickToX(note.StartTick, score.Timing.Ppq);
            var noteWidth = Math.Max(3, TickToX(Math.Max(1, note.DurationTick), score.Timing.Ppq));
            var noteY = RulerHeight + PitchToRow(note.Pitch) * RowHeight;
            if (noteX + noteWidth < visibleLeft || noteX > visibleRight ||
                noteY + RowHeight < visibleTop || noteY > visibleBottom)
            {
                continue;
            }

            DrawNote(
                drawingContext,
                movePreviews.GetValueOrDefault(note.Id) ?? note,
                score.Timing.Ppq,
                _selectedNoteIds.Contains(note.Id),
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

        if (note.DurationMode == DurationMode.Auto && note.RhythmTick is > 0)
        {
            var rhythmBounds = new Rect(
                bounds.X,
                bounds.Y,
                Math.Max(2, TickToX(note.RhythmTick.Value, ppq)),
                bounds.Height);
            drawingContext.DrawRoundedRectangle(
                null,
                new Pen(WithOpacity(NoteBrush, isCopyPreview ? 0.38 : 0.55), 1),
                rhythmBounds,
                3,
                3);
        }

        drawingContext.DrawRoundedRectangle(
            isCopyPreview
                ? WithOpacity(NoteBrush, 0.42)
                : selected ? NoteBrush : WithOpacity(NoteBrush, 0.78),
            isCopyPreview
                ? new Pen(WithOpacity(Foreground, 0.9), 1.2)
                : selected ? new Pen(Foreground, 1.4) : null,
            bounds,
            3,
            3);

        if (bounds.Width >= 24)
        {
            var entry = Rows[PointToRow(bounds.Y + bounds.Height / 2)];
            var noteLabel = PitchLabelFormatter.FormatNoteLabel(note.Pitch, entry.Key, LabelMode);
            var label = isCopyPreview ? $"+ {noteLabel}" : noteLabel;
            var text = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeights.SemiBold, FontStretch),
                10,
                Foreground,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawText(text, new Point(bounds.X + 5, bounds.Y + 4));
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
        var row = Array.FindIndex(Rows, entry => entry.Pitch == note.Pitch);
        if (row < 0)
        {
            bounds = Rect.Empty;
            return false;
        }

        bounds = new Rect(
            TickToX(note.StartTick, ppq),
            RulerHeight + row * RowHeight + 3,
            Math.Max(5, TickToX(note.DurationTick, ppq)),
            RowHeight - 6);
        return true;
    }

    private void CreateNote(Point point)
    {
        var score = Score!;
        var snap = GetSnapTick(score);
        var rhythmTick = snap;
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

        _selectedNoteIds.Clear();
        _selectedNoteIds.Add(note.Id);
        _primarySelectedNoteId = note.Id;
        Commit(ScoreEditor.AddNote(score, note));
        RaiseSelectionChanged();
    }

    private void ReplaceNote(NoteEvent replacement)
        => ReplaceNotes([replacement]);

    private void ReplaceNotes(IReadOnlyCollection<NoteEvent> replacements)
    {
        var score = Score!;
        Commit(ScoreEditor.ReplaceNotes(score, replacements));
    }

    private void DeleteSelectedNotes()
    {
        var score = Score;
        if (score is null || _selectedNoteIds.Count == 0)
        {
            return;
        }

        var selectedIds = _selectedNoteIds.ToArray();
        _selectedNoteIds.Clear();
        _primarySelectedNoteId = null;
        Commit(ScoreEditor.DeleteNotes(score, selectedIds));
        RaiseSelectionChanged();
    }

    private IReadOnlyList<NoteEvent> GetSelectedNotes() => Score?.Tracks
        .SelectMany(track => track.Notes)
        .Where(note => _selectedNoteIds.Contains(note.Id))
        .ToArray() ?? [];

    private void ClearSelection()
    {
        if (_selectedNoteIds.Count == 0)
        {
            return;
        }

        _selectedNoteIds.Clear();
        _primarySelectedNoteId = null;
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

        _selectedNoteIds.Clear();
        _selectedNoteIds.UnionWith(_selectionBeforeMarquee);
        foreach (var note in Score.Tracks.SelectMany(track => track.Notes))
        {
            if (TryGetNoteBounds(note, Score.Timing.Ppq, out var bounds) && selectionRect.IntersectsWith(bounds))
            {
                _selectedNoteIds.Add(note.Id);
            }
        }

        _primarySelectedNoteId = _selectedNoteIds.Count > 0 ? _selectedNoteIds.First() : null;
        RaiseSelectionChanged();
    }

    private void Commit(ScoreDocument updatedScore)
    {
        if (Score is null || updatedScore == Score)
        {
            return;
        }

        _undo.Push(Score);
        _redo.Clear();
        SetScoreInternally(updatedScore);
    }

    private void Undo()
    {
        if (_undo.Count == 0 || Score is null)
        {
            return;
        }

        _redo.Push(Score);
        SetScoreInternally(_undo.Pop());
    }

    private void Redo()
    {
        if (_redo.Count == 0 || Score is null)
        {
            return;
        }

        _undo.Push(Score);
        SetScoreInternally(_redo.Pop());
    }

    private void SetScoreInternally(ScoreDocument score)
    {
        _internalScoreChange = true;
        SetCurrentValue(ScoreProperty, score);
        _internalScoreChange = false;
    }

    private long GetSnapTick(ScoreDocument score) => Math.Max(1, score.Timing.Ppq / SnapDivision);

    private double TickToX(long tick, int ppq) => tick * PixelsPerBeat / ppq;

    private long XToTick(double x, int ppq) => checked((long)Math.Round(x * ppq / PixelsPerBeat));

    private static long Snap(long tick, long step) => checked((long)Math.Round(
        tick / (double)step,
        MidpointRounding.AwayFromZero) * step);

    private static long FloorToGrid(long tick, long step) => checked(
        (long)Math.Floor(tick / (double)step) * step);

    private int PointToRow(double y) =>
        Math.Clamp((int)((y - RulerHeight) / RowHeight), 0, Rows.Length - 1);

    private static Rect NormalizeRect(Point first, Point second) => new(
        new Point(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
        new Point(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));

    private static int PitchToRow(int pitch) =>
        Array.FindIndex(Rows, entry => entry.Pitch == pitch);

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
        if (!surface._internalScoreChange)
        {
            surface._undo.Clear();
            surface._redo.Clear();
        }

        if (args.NewValue is ScoreDocument score)
        {
            var existingIds = score.Tracks.SelectMany(track => track.Notes).Select(note => note.Id).ToHashSet();
            var selectionChanged = surface._selectedNoteIds.RemoveWhere(id => !existingIds.Contains(id)) > 0;
            if (surface._primarySelectedNoteId is { } primary && !existingIds.Contains(primary))
            {
                surface._primarySelectedNoteId = surface._selectedNoteIds.Count > 0
                    ? surface._selectedNoteIds.First()
                    : null;
                selectionChanged = true;
            }

            if (selectionChanged)
            {
                surface.SelectedNoteChanged?.Invoke(surface, EventArgs.Empty);
            }
        }

        surface.InvalidateMeasure();
        surface.InvalidateVisual();
    }

    private enum DragMode
    {
        None,
        Move,
        Copy,
        Marquee,
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
