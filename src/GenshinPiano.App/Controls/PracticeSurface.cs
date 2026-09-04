using System.IO;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public enum PracticeSurfaceMode
{
    GameKeys,
    VerticalRoll,
}

public sealed class PracticeSurface : FrameworkElement
{
    private static readonly (GenshinKey Key, int Pitch)[] DisplayKeys =
        GenshinKeyMap.All.Chunk(7).Reverse().SelectMany(group => group).ToArray();
    private static readonly string[] WebIconNames = ["do", "re", "mi", "fa", "so", "la", "ti"];
    private static readonly Dictionary<int, Geometry[]> WebIconCache = [];
    private static Geometry[]? WebBorderCache;
    private readonly HashSet<GenshinKey> _pressedKeys = [];
    private GenshinKey? _pointerKey;
    private IReadOnlyList<IReadOnlyList<GenshinKey>> _practiceSequence = [];
    private IReadOnlySet<GenshinKey> _targetKeys = new HashSet<GenshinKey>();
    private readonly Dictionary<GenshinKey, int> _wrongKeyVersions = [];
    private int _practiceIndex;
    private double _rollCursorTick;
    private double _rollAnimationFrom;
    private double _rollAnimationTo;
    private readonly Stopwatch _rollAnimationClock = new();
    private bool _isRollAnimating;
    private double _rollSpacing = 1;
    private double _hiddenCursorRatio = .18;
    private bool _isDraggingHiddenCursor;

    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score), typeof(ScoreDocument), typeof(PracticeSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentKeysProperty = DependencyProperty.Register(
        nameof(CurrentKeys), typeof(string), typeof(PracticeSurface),
        new FrameworkPropertyMetadata("—", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressPercentProperty = DependencyProperty.Register(
        nameof(ProgressPercent), typeof(double), typeof(PracticeSurface),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(PracticeSurfaceMode), typeof(PracticeSurface),
        new FrameworkPropertyMetadata(PracticeSurfaceMode.GameKeys, FrameworkPropertyMetadataOptions.AffectsRender));

    public ScoreDocument? Score { get => (ScoreDocument?)GetValue(ScoreProperty); set => SetValue(ScoreProperty, value); }
    public string CurrentKeys { get => (string)GetValue(CurrentKeysProperty); set => SetValue(CurrentKeysProperty, value); }
    public double ProgressPercent { get => (double)GetValue(ProgressPercentProperty); set => SetValue(ProgressPercentProperty, value); }
    public PracticeSurfaceMode Mode { get => (PracticeSurfaceMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }

    public event EventHandler<GenshinKey>? PracticeKeyPressed;

    public void SetPracticePosition(IReadOnlyList<IReadOnlyList<GenshinKey>> sequence, int index)
    {
        _practiceSequence = sequence;
        _practiceIndex = sequence.Count == 0 ? 0 : Math.Clamp(index, 0, sequence.Count - 1);
        _targetKeys = sequence.Count == 0
            ? new HashSet<GenshinKey>()
            : sequence[_practiceIndex].ToHashSet();
        ProgressPercent = sequence.Count <= 1 ? 0 : _practiceIndex * 100d / (sequence.Count - 1);
        InvalidateVisual();
    }

    public void SetRollCursorTick(double tick, bool animate)
    {
        if (!animate)
        {
            StopRollAnimation();
            _rollCursorTick = tick;
            InvalidateVisual();
            return;
        }
        _rollAnimationFrom = _rollCursorTick;
        _rollAnimationTo = tick;
        _rollAnimationClock.Restart();
        if (!_isRollAnimating)
        {
            _isRollAnimating = true;
            CompositionTarget.Rendering += OnRollAnimationFrame;
        }
    }

    public void SetRollSpacing(double spacing)
    {
        _rollSpacing = Math.Clamp(spacing, 1, 2);
        InvalidateVisual();
    }

    private void OnRollAnimationFrame(object? sender, EventArgs e)
    {
        const double durationMs = 320;
        var progress = Math.Clamp(_rollAnimationClock.Elapsed.TotalMilliseconds / durationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        _rollCursorTick = _rollAnimationFrom + (_rollAnimationTo - _rollAnimationFrom) * eased;
        InvalidateVisual();
        if (progress >= 1) StopRollAnimation();
    }

    private void StopRollAnimation()
    {
        if (_isRollAnimating) CompositionTarget.Rendering -= OnRollAnimationFrame;
        _isRollAnimating = false;
        _rollAnimationClock.Stop();
    }

    public async void FlashWrongKey(GenshinKey key)
    {
        var version = _wrongKeyVersions.TryGetValue(key, out var current) ? current + 1 : 1;
        _wrongKeyVersions[key] = version;
        InvalidateVisual();
        await Task.Delay(220);
        if (_wrongKeyVersions.TryGetValue(key, out current) && current == version)
        {
            _wrongKeyVersions.Remove(key);
            InvalidateVisual();
        }
    }
    public void SetKeyPressed(GenshinKey key, bool pressed)
    {
        var changed = pressed ? _pressedKeys.Add(key) : _pressedKeys.Remove(key);
        if (changed) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var background = GetBrush("SurfaceBrush", Color.FromRgb(249, 250, 247));
        drawingContext.DrawRectangle(background, null, new Rect(RenderSize));
        if (Mode == PracticeSurfaceMode.VerticalRoll)
        {
            DrawVerticalRoll(drawingContext);
        }
        else
        {
            DrawGameKeys(drawingContext);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var point = e.GetPosition(this);
        if (Mode == PracticeSurfaceMode.VerticalRoll &&
            Math.Abs(point.Y - GetHiddenCursorY()) <= 10)
        {
            _isDraggingHiddenCursor = true;
            Cursor = Cursors.SizeNS;
            CaptureMouse();
            e.Handled = true;
            return;
        }
        if (TryHitKey(point) is not { } key) return;
        _pointerKey = key;
        SetKeyPressed(key, true);
        CaptureMouse();
        PracticeKeyPressed?.Invoke(this, key);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDraggingHiddenCursor)
        {
            _isDraggingHiddenCursor = false;
            Cursor = null;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }
        if (_pointerKey is { } key)
        {
            SetKeyPressed(key, false);
            _pointerKey = null;
        }
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isDraggingHiddenCursor = false;
        Cursor = null;
        if (_pointerKey is not { } key) return;
        SetKeyPressed(key, false);
        _pointerKey = null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Mode != PracticeSurfaceMode.VerticalRoll) return;
        var point = e.GetPosition(this);
        if (_isDraggingHiddenCursor)
        {
            const double header = 48;
            const double footer = 54;
            var rollHeight = Math.Max(1, ActualHeight - footer - header);
            _hiddenCursorRatio = Math.Clamp((point.Y - header) / rollHeight, .06, .72);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        Cursor = Math.Abs(point.Y - GetHiddenCursorY()) <= 8 ? Cursors.SizeNS : null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_isDraggingHiddenCursor) Cursor = null;
    }

    private double GetHiddenCursorY()
    {
        const double header = 48;
        const double footer = 54;
        return header + Math.Max(1, ActualHeight - footer - header) * _hiddenCursorRatio;
    }

    private void DrawGameKeys(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var marginX = Math.Max(26, width * .055);
        var top = Math.Max(150, height * .25);
        var availableWidth = width - marginX * 2;
        var availableHeight = height - top - 34;
        var cellWidth = availableWidth / 7;
        var cellHeight = availableHeight / 3;
        var radius = Math.Clamp(Math.Min(cellWidth, cellHeight) * .36, 25, 61);
        var keyBackground = GetBrush("PanelBackgroundBrush", Color.FromRgb(255, 255, 255));
        var active = CurrentKeys?.Where(char.IsLetter).Select(char.ToUpperInvariant).ToHashSet() ?? [];
        active.UnionWith(_pressedKeys.Select(key => key.ToString()[0]));
        var ring = GetBrush("BorderBrush", Color.FromRgb(224, 215, 190));
        var ink = GetBrush("PrimaryTextBrush", Color.FromRgb(27, 48, 48));
        var accent = GetBrush("AccentBrush", Color.FromRgb(62, 145, 137));
        var muted = GetBrush("SecondaryTextBrush", Color.FromRgb(100, 120, 119));

        DrawText(dc, Score?.Metadata.Title ?? string.Empty, 22, FontWeights.SemiBold, ink,
            new Point(marginX, 26), availableWidth, TextAlignment.Left);
        DrawText(dc, "Q W E R T Y U  ·  A S D F G H J  ·  Z X C V B N M", 11,
            FontWeights.Normal, muted, new Point(marginX, 58), availableWidth, TextAlignment.Left);

        DrawFilmStrip(dc, width, accent, ink, muted, keyBackground, ring);

        for (var index = 0; index < DisplayKeys.Length; index++)
        {
            var row = index / 7;
            var column = index % 7;
            var key = DisplayKeys[index].Key;
            var center = new Point(marginX + cellWidth * (column + .5), top + cellHeight * (row + .5));
            var isActive = active.Contains(key.ToString()[0]);
            var isTarget = _targetKeys.Contains(key);
            var isWrong = _wrongKeyVersions.ContainsKey(key);
            var pressedRadius = isActive ? radius * .94 : radius;
            var fill = isWrong ? Brushes.IndianRed : isActive ? accent : isTarget ? GetBrush("AccentSurfaceBrush", Color.FromRgb(220, 235, 250)) : keyBackground;
            var stroke = isWrong ? Brushes.IndianRed : isActive || isTarget ? accent : ring;
            dc.DrawEllipse(fill, new Pen(stroke, isActive || isTarget || isWrong ? 4 : 2), center, pressedRadius, pressedRadius);
            DrawWebBorder(dc, center, pressedRadius * 1.78, isActive || isWrong ? Brushes.White : ring);
            DrawWebIcon(dc, column, center, pressedRadius * .72, isActive || isWrong ? Brushes.White : accent);
            DrawText(dc, key.ToString(), radius * .25, FontWeights.Bold,
                isActive || isWrong ? Brushes.White : muted,
                new Point(center.X - radius, center.Y + radius * .33), radius * 2, TextAlignment.Center);
        }
    }

    private void DrawFilmStrip(
        DrawingContext dc, double width, Brush accent, Brush ink, Brush muted, Brush background, Brush border)
    {
        if (_practiceSequence.Count == 0) return;
        const int visibleRadius = 3;
        var centerX = width / 2;
        var y = 82d;
        var slotWidth = Math.Clamp(width * .085, 58, 96);
        for (var offset = -visibleRadius; offset <= visibleRadius; offset++)
        {
            var stepIndex = _practiceIndex + offset;
            if (stepIndex < 0 || stepIndex >= _practiceSequence.Count) continue;
            var isCurrent = offset == 0;
            var itemWidth = isCurrent ? slotWidth * 1.12 : slotWidth;
            var itemHeight = isCurrent ? 48d : 40d;
            var x = centerX + offset * (slotWidth + 10) - itemWidth / 2;
            var rect = new Rect(x, y, itemWidth, itemHeight);
            var itemBrush = isCurrent
                ? GetBrush("AccentSurfaceBrush", Color.FromRgb(220, 235, 250))
                : background;
            dc.PushOpacity(isCurrent ? 1 : Math.Max(.32, .72 - Math.Abs(offset) * .1));
            dc.DrawRoundedRectangle(itemBrush, new Pen(isCurrent ? accent : border, isCurrent ? 2.2 : 1), rect, 9, 9);
            DrawText(dc, string.Join("+", _practiceSequence[stepIndex]), isCurrent ? 15 : 12,
                isCurrent ? FontWeights.Bold : FontWeights.SemiBold,
                isCurrent ? ink : muted, new Point(rect.X, rect.Y + (isCurrent ? 13 : 11)),
                rect.Width, TextAlignment.Center);
            dc.Pop();
        }
    }
    private void DrawVerticalRoll(DrawingContext dc)
    {
        var keys = GenshinKeyMap.All.ToArray();
        var laneCount = keys.Length;
        var header = 48d;
        var footer = 54d;
        var laneWidth = ActualWidth / laneCount;
        var playLineY = ActualHeight - footer;
        var hiddenCursorY = GetHiddenCursorY();
        var border = GetBrush("BorderBrush", Color.FromRgb(218, 226, 224));
        var accent = GetBrush("AccentBrush", Color.FromRgb(62, 145, 137));
        var ink = GetBrush("PrimaryTextBrush", Color.FromRgb(27, 48, 48));
        var muted = GetBrush("SecondaryTextBrush", Color.FromRgb(100, 120, 119));
        var keyBackground = GetBrush("PanelBackgroundBrush", Color.FromRgb(255, 255, 255));
        var active = CurrentKeys?.Where(char.IsLetter).Select(char.ToUpperInvariant).ToHashSet() ?? [];
        active.UnionWith(_pressedKeys.Select(key => key.ToString()[0]));

        for (var i = 0; i <= laneCount; i++)
        {
            var x = i * laneWidth;
            if (i < laneCount && _targetKeys.Contains(keys[i].Key))
            {
                dc.DrawRectangle(
                    GetBrush("AccentSurfaceBrush", Color.FromRgb(220, 235, 250)),
                    null,
                    new Rect(x + 1, header, Math.Max(1, laneWidth - 2), playLineY - header));
            }
            dc.DrawLine(new Pen(border, i % 7 == 0 ? 1.4 : .65), new Point(x, header), new Point(x, playLineY));
        }

        var notes = Score?.Tracks.Where(track => !track.IsMuted).SelectMany(track => track.Notes).ToArray() ?? [];
        var cursorTick = _rollCursorTick;
        var visibleTicks = Math.Max((Score?.Timing.Ppq * 8d ?? 3840d) / _rollSpacing, 1d);
        dc.PushClip(new RectangleGeometry(new Rect(
            0, hiddenCursorY, ActualWidth, Math.Max(0, playLineY - hiddenCursorY + 2))));
        foreach (var note in notes)
        {
            if (!GenshinKeyMap.TryMapPitch(note.Pitch, Score?.Playback.Transpose ?? 0,
                    Score?.Playback.OutOfRangePolicy ?? OutOfRangePolicy.OctaveFold, out var key))
            {
                continue;
            }

            var keyIndex = Array.FindIndex(keys, item => item.Key == key);
            if (keyIndex < 0) continue;
            var delta = note.StartTick - cursorTick;
            var y = playLineY - delta / visibleTicks * (playLineY - header);
            var noteHeight = Math.Clamp(note.DurationTick / visibleTicks * (playLineY - header), 9, 90);
            if (y + noteHeight < header || y - noteHeight > playLineY) continue;
            var rect = new Rect(keyIndex * laneWidth + 2, y - noteHeight, Math.Max(3, laneWidth - 4), noteHeight);
            var reveal = Math.Clamp((rect.Bottom - hiddenCursorY) / 52d, .08, 1);
            dc.PushOpacity(reveal);
            dc.DrawRoundedRectangle(accent, null, rect, 4, 4);
            dc.Pop();
        }
        dc.Pop();

        var cursorBrush = GetBrush("PracticeCursorBrush", Color.FromRgb(201, 109, 60));
        dc.DrawLine(new Pen(cursorBrush, 2), new Point(0, hiddenCursorY), new Point(ActualWidth, hiddenCursorY));
        dc.DrawRectangle(accent, null, new Rect(0, playLineY - 2, ActualWidth, 4));
        for (var i = 0; i < laneCount; i++)
        {
            var key = keys[i].Key;
            var isActive = active.Contains(key.ToString()[0]);
            var isTarget = _targetKeys.Contains(key);
            var isWrong = _wrongKeyVersions.ContainsKey(key);
            var rect = new Rect(i * laneWidth + 1, playLineY + 7, Math.Max(4, laneWidth - 2), footer - 13);
            var fill = isWrong ? Brushes.IndianRed : isActive ? accent : keyBackground;
            var stroke = isWrong ? Brushes.IndianRed : isActive || isTarget ? accent : border;
            dc.DrawRoundedRectangle(fill, new Pen(stroke, isActive || isTarget || isWrong ? 2 : 1), rect, 4, 4);
            DrawText(dc, key.ToString(), Math.Clamp(laneWidth * .34, 8, 14), FontWeights.Bold,
                isActive || isWrong ? Brushes.White : ink, new Point(rect.X, rect.Y + 9), rect.Width, TextAlignment.Center);
        }
    }

    private static void DrawWebBorder(DrawingContext dc, Point center, double targetSize, Brush brush)
    {
        WebBorderCache ??= LoadSvgGeometry("genshin-border-v2");
        DrawGeometrySet(dc, WebBorderCache, center, targetSize, brush, 0);
    }
    private static void DrawWebIcon(DrawingContext dc, int degree, Point center, double targetSize, Brush brush)
    {
        DrawGeometrySet(dc, LoadWebIcon(degree), center, targetSize, brush, -.08);
    }

    private static void DrawGeometrySet(
        DrawingContext dc, Geometry[] geometries, Point center, double targetSize, Brush brush, double verticalOffset)
    {
        if (geometries.Length == 0) return;
        var bounds = geometries.Select(geometry => geometry.Bounds).Aggregate(Rect.Union);
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
        var scale = targetSize / Math.Max(bounds.Width, bounds.Height);
        dc.PushTransform(new TranslateTransform(
            center.X - (bounds.X + bounds.Width / 2) * scale,
            center.Y - (bounds.Y + bounds.Height / 2) * scale + targetSize * verticalOffset));
        dc.PushTransform(new ScaleTransform(scale, scale));
        foreach (var geometry in geometries) dc.DrawGeometry(brush, null, geometry);
        dc.Pop();
        dc.Pop();
    }

    private static Geometry[] LoadSvgGeometry(string name)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Practice", "Icons", name + ".svg");
            var document = XDocument.Load(path);
            var geometries = document.Descendants()
                .Where(element => element.Name.LocalName == "path")
                .Select(element => element.Attribute("d")?.Value)
                .Where(data => !string.IsNullOrWhiteSpace(data))
                .Select(data => Geometry.Parse(data!))
                .ToArray();
            foreach (var geometry in geometries)
            {
                if (geometry.CanFreeze) geometry.Freeze();
            }
            return geometries;
        }
        catch
        {
            return [];
        }
    }
    private static Geometry[] LoadWebIcon(int degree)
    {
        degree = Math.Clamp(degree, 0, WebIconNames.Length - 1);
        if (WebIconCache.TryGetValue(degree, out var cached)) return cached;
        var geometries = LoadSvgGeometry(WebIconNames[degree]);
        WebIconCache[degree] = geometries;
        return geometries;
    }
    private GenshinKey? TryHitKey(Point point)
    {
        if (Mode == PracticeSurfaceMode.GameKeys) return TryHitGameKey(point);
        const double footer = 54;
        if (point.Y < ActualHeight - footer || point.X < 0 || point.X >= ActualWidth) return null;
        var index = Math.Clamp((int)(point.X / (ActualWidth / GenshinKeyMap.All.Count)), 0, GenshinKeyMap.All.Count - 1);
        return GenshinKeyMap.All[index].Key;
    }
    private GenshinKey? TryHitGameKey(Point point)
    {
        var marginX = Math.Max(26, ActualWidth * .055);
        var top = Math.Max(150, ActualHeight * .25);
        var cellWidth = (ActualWidth - marginX * 2) / 7;
        var cellHeight = (ActualHeight - top - 34) / 3;
        if (point.X < marginX || point.Y < top || cellWidth <= 0 || cellHeight <= 0) return null;
        var column = (int)((point.X - marginX) / cellWidth);
        var row = (int)((point.Y - top) / cellHeight);
        if (column is < 0 or > 6 || row is < 0 or > 2) return null;
        var index = row * 7 + column;
        return index < DisplayKeys.Length ? DisplayKeys[index].Key : null;
    }

    private Brush GetBrush(string key, Color fallback) =>
        TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    private void DrawText(DrawingContext dc, string text, double size, FontWeight weight, Brush brush,
        Point origin, double width, TextAlignment alignment)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, width),
            TextAlignment = alignment,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(formatted, origin);
    }

    private static string NoteName(int pitch) =>
        new[] { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" }[pitch % 12];
}
