using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GenshinPiano.App.Dialogs;

internal sealed class CalibrationRoll : FrameworkElement
{
    private const double BeatMs = 600, HitMs = 1800, CycleMs = 3000;
    private readonly Stopwatch _clock = new();
    private SoundPlayer? _sound;
    private MemoryStream? _soundData;
    private long _lastBeat = -1;
    private bool _dragging;
    private long _hitEmphasisStarted;
    public double DelayMilliseconds { get; private set; }
    public event EventHandler? DelayChanged;
    private double Top => 30;
    private double BaseLine => ActualHeight - 58;
    private double PixelsPerMs => Math.Max(1, BaseLine - Top) / HitMs;
    private double LineY => BaseLine + DelayMilliseconds * PixelsPerMs;

    public CalibrationRoll(double delay)
    {
        DelayMilliseconds = Math.Clamp(delay, -300, 300);
        Loaded += (_, _) =>
        {
            try
            {
                _soundData = CreateTone();
                _sound = new SoundPlayer(_soundData);
                _sound.Load();
            }
            catch (Exception ex) { Services.AppLogger.Warning($"Calibration sound: {ex.Message}"); }
            _lastBeat = -1;
            _clock.Restart();
            CompositionTarget.Rendering += Frame;
        };
        Unloaded += (_, _) =>
        {
            CompositionTarget.Rendering -= Frame;
            _clock.Stop();
            _sound?.Stop();
            _sound?.Dispose();
            _soundData?.Dispose();
            _sound = null;
        };
    }

    public void SetDelay(double delay)
    {
        DelayMilliseconds = Math.Clamp(Math.Round(delay), -300, 300);
        DelayChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void Frame(object? sender, EventArgs e)
    {
        var beat = (long)(_clock.Elapsed.TotalMilliseconds / BeatMs);
        if (beat != _lastBeat)
        {
            _lastBeat = beat;
            if (beat % 5 < 4)
            {
                try { _sound?.Play(); }
                catch (Exception ex) { Services.AppLogger.Warning($"Calibration sound: {ex.Message}"); }
            }
        }
        InvalidateVisual();
    }

    public void RegisterHit()
    {
        _hitEmphasisStarted = Stopwatch.GetTimestamp();
        InvalidateVisual();
    }

    private Brush Theme(string key, Color fallback) => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        if (width <= 0 || ActualHeight <= 0) return;
        var phase = _clock.Elapsed.TotalMilliseconds % CycleMs;
        var accent = Theme("AccentBrush", Colors.SteelBlue);
        var marker = Theme("PracticeCursorBrush", Colors.Coral);
        var border = Theme("BorderBrush", Colors.Gray);
        dc.DrawRoundedRectangle(Theme("SurfaceBrush", Colors.WhiteSmoke), new Pen(border, 1), new Rect(RenderSize), 5, 5);
        for (var i = 1; i <= 3; i++)
            dc.DrawLine(new Pen(border, .7), new Point(width * i / 4, Top), new Point(width * i / 4, ActualHeight - 12));
        for (var i = 0; i < 4; i++)
        {
            dc.PushOpacity(phase >= i * BeatMs && phase < (i + 1) * BeatMs ? 1 : .25);
            dc.DrawEllipse(accent, null, new Point(width / 2 + (i - 1.5) * 22, 12), 4, 4);
            dc.Pop();
        }
        var y = Top + phase * PixelsPerMs;
        dc.PushClip(new RectangleGeometry(new Rect(0, Top, width, Math.Max(0, LineY - Top))));
        dc.DrawRoundedRectangle(accent, null, new Rect(width / 2 - 23, y - 24, 46, 24), 4, 4);
        dc.Pop();
        var emphasisElapsed = _hitEmphasisStarted == 0
            ? double.PositiveInfinity
            : Stopwatch.GetElapsedTime(_hitEmphasisStarted).TotalMilliseconds;
        var emphasis = emphasisElapsed < 300 ? 1 - emphasisElapsed / 300 : 0;
        dc.PushOpacity(emphasis * .22);
        dc.DrawRectangle(marker, null, new Rect(12, LineY - 9, Math.Max(0, width - 24), 18));
        dc.Pop();
        dc.DrawLine(new Pen(emphasis > 0 ? marker : accent, 2 + emphasis * 3),
            new Point(12, LineY), new Point(width - 12, LineY));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Math.Abs(e.GetPosition(this).Y - LineY) > 16) return;
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var y = e.GetPosition(this).Y;
        Cursor = _dragging || Math.Abs(y - LineY) <= 16 ? Cursors.SizeNS : Cursors.Arrow;
        if (_dragging) SetDelay((y - BaseLine) / PixelsPerMs);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        SetDelay((e.GetPosition(this).Y - BaseLine) / PixelsPerMs);
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    private static MemoryStream CreateTone()
    {
        const int rate = 44100, count = 4410;
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8.ToArray()); writer.Write(36 + count * 2);
            writer.Write("WAVEfmt "u8.ToArray()); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
            writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8.ToArray()); writer.Write(count * 2);
            for (var i = 0; i < count; i++)
            {
                var t = i / (double)rate;
                var envelope = Math.Min(1, i / 80d) * Math.Exp(-t * 30);
                var body = Math.Sin(2 * Math.PI * (145 - 55 * t) * t);
                var click = Math.Sin(2 * Math.PI * 950 * t) * Math.Exp(-t * 85) * .22;
                writer.Write((short)((body + click) * envelope * 11500));
            }
        }
        stream.Position = 0;
        return stream;
    }
}
