using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenshinPiano.Core.Playback;
using GenshinPiano.Core.Scores;

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
    private IReadOnlyList<PianoRollPitchRow> _rows = PianoRollPitchLayouts.GetRows(
        PianoRollPitchLayoutMode.Genshin21);

    private IReadOnlyList<PianoRollPitchRow> Rows => _rows;

    public static readonly DependencyProperty ScoreProperty = DependencyProperty.Register(
        nameof(Score),
        typeof(ScoreDocument),
        typeof(PianoRollKeyboard),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnLayoutSourceChanged));

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

    public static readonly DependencyProperty PitchLayoutModeProperty = DependencyProperty.Register(
        nameof(PitchLayoutMode),
        typeof(PianoRollPitchLayoutMode),
        typeof(PianoRollKeyboard),
        new FrameworkPropertyMetadata(
            PianoRollPitchLayoutMode.Genshin21,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnLayoutSourceChanged));

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

    public PianoRollPitchLayoutMode PitchLayoutMode
    {
        get => (PianoRollPitchLayoutMode)GetValue(PitchLayoutModeProperty);
        set => SetValue(PitchLayoutModeProperty, value);
    }

    public ScoreDocument? Score
    {
        get => (ScoreDocument?)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    protected override Size MeasureOverride(Size constraint) => new(
        72,
        PianoRollSurface.RulerHeight + Rows.Count * RowHeight);

    private static void OnLayoutSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        ((PianoRollKeyboard)dependencyObject).RefreshRows();
    }

    private void RefreshRows()
    {
        _rows = PianoRollPitchLayouts.GetRows(
            PitchLayoutMode,
            Score?.Tracks.SelectMany(track => track.Notes).Select(note => note.Pitch));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
        drawingContext.DrawLine(
            new Pen(BorderBrush, 1),
            new Point(0, PianoRollSurface.RulerHeight),
            new Point(RenderSize.Width, PianoRollSurface.RulerHeight));

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var row = 0; row < Rows.Count; row++)
        {
            var y = PianoRollSurface.RulerHeight + row * RowHeight;
            if (Rows[row].IsBlackKey || (PitchLayoutMode == PianoRollPitchLayoutMode.Genshin21 && row % 2 == 1))
            {
                drawingContext.DrawRectangle(
                    WithOpacity(Foreground, 0.09),
                    null,
                    new Rect(0, y, RenderSize.Width, RowHeight));
            }

            drawingContext.DrawLine(
                new Pen(WithOpacity(BorderBrush, 0.55), 1),
                new Point(0, y + RowHeight),
                new Point(RenderSize.Width, y + RowHeight));

            var entry = Rows[row];
            var pitchText = CreateLabelText(
                PitchLabelFormatter.FormatPitchLabel(entry.Pitch, LabelMode),
                pixelsPerDip);
            var textY = y + Math.Max(2, (RowHeight - pitchText.Height) / 2);
            if (PitchLabelFormatter.IncludesKey(LabelMode))
            {
                const double pitchColumnLeft = 9;
                const double keyColumnLeft = 40;
                drawingContext.DrawText(
                    pitchText,
                    new Point(pitchColumnLeft, textY));
                if (entry.Key is { } key)
                {
                    drawingContext.DrawText(CreateLabelText(key.ToString(), pixelsPerDip), new Point(keyColumnLeft, textY));
                }
            }
            else
            {
                drawingContext.DrawText(pitchText, new Point(9, textY));
            }
        }
    }

    private FormattedText CreateLabelText(string label, double pixelsPerDip) => new(
                label,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeights.SemiBold, FontStretch),
                10.5,
                Foreground,
                pixelsPerDip);

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
    public static bool IncludesKey(PitchLabelMode mode) =>
        mode is PitchLabelMode.LetterWithKey or PitchLabelMode.NumberedWithKey;

    public static string FormatPitchLabel(int pitch, PitchLabelMode mode) =>
        mode is PitchLabelMode.LetterWithKey or PitchLabelMode.LetterOnly
            ? GetLetterPitch(pitch)
            : GetNumberedPitch(pitch);

    public static string FormatKeyboardLabel(int pitch, GenshinKey key, PitchLabelMode mode)
    {
        var pitchLabel = FormatPitchLabel(pitch, mode);
        return IncludesKey(mode)
            ? $"{pitchLabel}  {key}"
            : pitchLabel;
    }

    public static string FormatNoteLabel(int pitch, GenshinKey? key, PitchLabelMode mode) =>
        mode switch
        {
            PitchLabelMode.LetterWithKey or PitchLabelMode.NumberedWithKey when key is not null => key.Value.ToString(),
            PitchLabelMode.LetterOnly => GetLetterPitch(pitch),
            PitchLabelMode.NumberedOnly => GetNumberedPitch(pitch),
            _ => FormatPitchLabel(pitch, mode),
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
            1 => "C#",
            3 => "D#",
            6 => "F#",
            8 => "G#",
            10 => "A#",
            _ => "?",
        };
        return $"{noteName}{pitch / 12 - 1}";
    }

    private static string GetNumberedPitch(int pitch)
    {
        var degree = (pitch % 12) switch
        {
            0 => "1",
            1 => "1#",
            2 => "2",
            3 => "2#",
            4 => "3",
            5 => "4",
            6 => "4#",
            7 => "5",
            8 => "5#",
            9 => "6",
            10 => "6#",
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
