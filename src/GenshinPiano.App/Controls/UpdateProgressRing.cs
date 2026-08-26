using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace GenshinPiano.App.Controls;

public sealed class UpdateProgressRing : FrameworkElement
{
    private readonly DispatcherTimer _animationTimer;
    private double _rotation;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(UpdateProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate),
        typeof(bool),
        typeof(UpdateProgressRing),
        new FrameworkPropertyMetadata(false, OnIsIndeterminateChanged));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush),
        typeof(Brush),
        typeof(UpdateProgressRing),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(UpdateProgressRing),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(UpdateProgressRing),
        new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsRender));

    public UpdateProgressRing()
    {
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(32),
        };
        _animationTimer.Tick += (_, _) =>
        {
            _rotation = (_rotation + 9) % 360;
            InvalidateVisual();
        };
        Unloaded += (_, _) => _animationTimer.Stop();
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public Brush RingBrush
    {
        get => (Brush)GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(18, 18);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var thickness = Math.Clamp(StrokeThickness, 1, 6);
        var radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.DrawEllipse(null, new Pen(TrackBrush, thickness), center, radius, radius);

        var sweep = IsIndeterminate ? 92 : Math.Clamp(Progress, 0, 1) * 360;
        if (sweep <= 0)
        {
            return;
        }

        if (sweep >= 359.9)
        {
            drawingContext.DrawEllipse(null, new Pen(RingBrush, thickness), center, radius, radius);
            return;
        }

        var startAngle = -90 + (IsIndeterminate ? _rotation : 0);
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweep);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweep > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(RingBrush, thickness), geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(
            center.X + Math.Cos(radians) * radius,
            center.Y + Math.Sin(radians) * radius);
    }

    private static void OnIsIndeterminateChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var ring = (UpdateProgressRing)dependencyObject;
        if ((bool)args.NewValue && ring.IsLoaded)
        {
            ring._animationTimer.Start();
        }
        else
        {
            ring._animationTimer.Stop();
        }
        ring.InvalidateVisual();
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += (_, _) =>
        {
            if (IsIndeterminate)
            {
                _animationTimer.Start();
            }
        };
    }
}
