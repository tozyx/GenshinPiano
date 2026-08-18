using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenshinPiano.Core.Playback;

namespace GenshinPiano.App.Controls;

public enum PitchLabelMode
{
    LetterWithKey,
    NumberedWithKey,
    LetterOnly,
    NumberedOnly,
}

public sealed class PianoRollKeyboard : Control
{
    private static readonly (GenshinKey Key, int Pitch)[] Rows =
        GenshinKeyMap.All.Reverse().ToArray();

    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight),
        typeof(double),
        typeof(PianoRollKeyboard),
        new FrameworkPropertyMetadata(
            PianoRollSurface.DefaultRowHeight,
            FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelModeProperty = DependencyProperty.Register(
        nameof(LabelMode),
        typeof(PitchLabelMode),
        typeof(PianoRollKeyboard),
        new FrameworkPropertyMetadata(PitchLabelMode.LetterWithKey, FrameworkPropertyMetadataOptions.AffectsRender));

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public PitchLabelMode LabelMode
    {
        get => (PitchLabelMode)GetValue(LabelModeProperty);
        set => SetValue(LabelModeProperty, value);
    }

    protected override Size MeasureOverride(Size constraint) => new(
        72,
        PianoRollSurface.RulerHeight + Rows.Length * RowHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
        drawingContext.DrawLine(
            new Pen(BorderBrush, 1),
            new Point(0, PianoRollSurface.RulerHeight),
            new Point(RenderSize.Width, PianoRollSurface.RulerHeight));

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var row = 0; row < Rows.Length; row++)
        {
            var y = PianoRollSurface.RulerHeight + row * RowHeight;
            if (row % 2 == 1)
            {
                drawingContext.DrawRectangle(
                    WithOpacity(BorderBrush, 0.16),
                    null,
                    new Rect(0, y, RenderSize.Width, RowHeight));
            }

            drawingContext.DrawLine(
                new Pen(WithOpacity(BorderBrush, 0.55), 1),
                new Point(0, y + RowHeight),
                new Point(RenderSize.Width, y + RowHeight));

            var entry = Rows[row];
            var label = PitchLabelFormatter.FormatKeyboardLabel(entry.Pitch, entry.Key, LabelMode);
            var text = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeights.SemiBold, FontStretch),
                10.5,
                Foreground,
                pixelsPerDip);
            drawingContext.DrawText(text, new Point(9, y + Math.Max(2, (RowHeight - text.Height) / 2)));
        }
    }

    private static Brush WithOpacity(Brush? source, double opacity)
    {
        var brush = source?.CloneCurrentValue() ?? Brushes.Transparent.CloneCurrentValue();
        brush.Opacity *= opacity;
        brush.Freeze();
        return brush;
    }
}

public static class PitchLabelFormatter
{
    public static string FormatKeyboardLabel(int pitch, GenshinKey key, PitchLabelMode mode)
    {
        var pitchLabel = mode is PitchLabelMode.LetterWithKey or PitchLabelMode.LetterOnly
            ? GetLetterPitch(pitch)
            : GetNumberedPitch(pitch);
        return mode is PitchLabelMode.LetterWithKey or PitchLabelMode.NumberedWithKey
            ? $"{pitchLabel}  {key}"
            : pitchLabel;
    }

    public static string FormatNoteLabel(int pitch, GenshinKey key, PitchLabelMode mode) =>
        mode switch
        {
            PitchLabelMode.LetterWithKey or PitchLabelMode.NumberedWithKey => key.ToString(),
            PitchLabelMode.LetterOnly => GetLetterPitch(pitch),
            PitchLabelMode.NumberedOnly => GetNumberedPitch(pitch),
            _ => key.ToString(),
        };

    private static string GetLetterPitch(int pitch)
    {
        var noteName = (pitch % 12) switch
        {
            0 => "C",
            2 => "D",
            4 => "E",
            5 => "F",
            7 => "G",
            9 => "A",
            11 => "B",
            _ => "?",
        };
        return $"{noteName}{pitch / 12 - 1}";
    }

    private static string GetNumberedPitch(int pitch)
    {
        var degree = (pitch % 12) switch
        {
            0 => "1",
            2 => "2",
            4 => "3",
            5 => "4",
            7 => "5",
            9 => "6",
            11 => "7",
            _ => "?",
        };

        return pitch switch
        {
            < 60 => degree + "-",
            >= 72 => degree + "+",
            _ => degree,
        };
    }

}
