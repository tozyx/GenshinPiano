using GenshinPiano.Application.Ocr;
using SkiaSharp;

namespace GenshinPiano.Ocr.Engine;

/// <summary>
/// Produces an OCR-oriented grayscale image in which faint background marks
/// are suppressed. This is intentionally not generative inpainting: dark score
/// ink is preserved byte-for-byte and only pixels close to the estimated paper
/// background are lifted toward white.
/// </summary>
internal static class WatermarkSuppressor
{
    private const int HistogramSize = 256;

    public static SKBitmap Create(
        SKBitmap source,
        OcrWatermarkMode mode,
        out SuppressionStatistics statistics)
    {
        var histogram = BuildLuminanceHistogram(source);
        var background = Percentile(histogram, source.Width * source.Height, 0.82);

        // Keep every sufficiently dark stroke untouched. The transition band
        // removes pale tiled text and uneven paper while retaining the black
        // core of digits, octave dots and rhythm underlines.
        var preserveBelow = mode == OcrWatermarkMode.Strong
            ? Math.Clamp(background - 90, 135, 170)
            : Math.Clamp(background - 65, 150, 190);
        var eraseAbove = mode == OcrWatermarkMode.Strong
            ? Math.Clamp(background - 38, preserveBelow + 28, 225)
            : Math.Clamp(background - 18, preserveBelow + 20, 245);
        var result = new SKBitmap(
            source.Width,
            source.Height,
            SKColorType.Gray8,
            SKAlphaType.Opaque);
        long adjustedPixels = 0;
        long erasedPixels = 0;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var luminance = Luminance(source.GetPixel(x, y));
                byte output;
                if (luminance <= preserveBelow)
                {
                    output = (byte)luminance;
                }
                else if (luminance >= eraseAbove)
                {
                    output = byte.MaxValue;
                    if (luminance < byte.MaxValue)
                    {
                        erasedPixels++;
                    }
                }
                else
                {
                    var progress = (luminance - preserveBelow) /
                        (double)(eraseAbove - preserveBelow);
                    var smooth = progress * progress * (3d - (2d * progress));
                    output = (byte)Math.Clamp(
                        (int)Math.Round(luminance + ((255 - luminance) * smooth)),
                        0,
                        255);
                    if (output != luminance)
                    {
                        adjustedPixels++;
                    }
                }

                result.SetPixel(x, y, new SKColor(output, output, output));
            }
        }

        statistics = new SuppressionStatistics(
            background,
            preserveBelow,
            eraseAbove,
            adjustedPixels,
            erasedPixels,
            source.Width * (long)source.Height);
        return result;
    }

    private static int[] BuildLuminanceHistogram(SKBitmap source)
    {
        var histogram = new int[HistogramSize];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                histogram[Luminance(source.GetPixel(x, y))]++;
            }
        }

        return histogram;
    }

    private static int Percentile(IReadOnlyList<int> histogram, long total, double percentile)
    {
        var target = Math.Max(1, (long)Math.Ceiling(total * percentile));
        long accumulated = 0;
        for (var value = 0; value < histogram.Count; value++)
        {
            accumulated += histogram[value];
            if (accumulated >= target)
            {
                return value;
            }
        }

        return byte.MaxValue;
    }

    private static int Luminance(SKColor color) =>
        ((77 * color.Red) + (150 * color.Green) + (29 * color.Blue)) >> 8;
}

internal sealed record SuppressionStatistics(
    int EstimatedBackground,
    int PreserveBelow,
    int EraseAbove,
    long AdjustedPixels,
    long ErasedPixels,
    long TotalPixels)
{
    public double ChangedPercent => TotalPixels == 0
        ? 0
        : ((AdjustedPixels + ErasedPixels) * 100d) / TotalPixels;
}
