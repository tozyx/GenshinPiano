using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GenshinPiano.Ocr.Engine;

internal sealed class SrcnnEnhancer : IDisposable
{
    private const int Scale = 3;
    private readonly InferenceSession _session;

    public SrcnnEnhancer(string modelPath)
    {
        _session = new InferenceSession(modelPath);
    }

    public SKBitmap? Enhance(SKBitmap source, SKRect bounds)
    {
        var clipped = SKRect.Intersect(bounds, new SKRect(0, 0, source.Width, source.Height));
        var sourceWidth = Math.Max(1, (int)Math.Ceiling(clipped.Width));
        var sourceHeight = Math.Max(1, (int)Math.Ceiling(clipped.Height));
        if (sourceWidth < 2 || sourceHeight < 2 || sourceWidth > 160 || sourceHeight > 160)
        {
            return null;
        }

        using var crop = new SKBitmap(
            sourceWidth,
            sourceHeight,
            SKColorType.Gray8,
            SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(
                source,
                clipped,
                new SKRect(0, 0, sourceWidth, sourceHeight));
        }

        using var bicubic = crop.Resize(
            new SKImageInfo(
                sourceWidth * Scale,
                sourceHeight * Scale,
                SKColorType.Gray8,
                SKAlphaType.Opaque),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (bicubic is null)
        {
            return null;
        }

        var tensor = new DenseTensor<float>([1, 1, bicubic.Height, bicubic.Width]);
        for (var y = 0; y < bicubic.Height; y++)
        {
            for (var x = 0; x < bicubic.Width; x++)
            {
                tensor[0, 0, y, x] = bicubic.GetPixel(x, y).Red / 255f;
            }
        }

        using var results = _session.Run(
            [NamedOnnxValue.CreateFromTensor("image", tensor)]);
        var output = results.First().AsTensor<float>();
        var enhanced = new SKBitmap(
            bicubic.Width,
            bicubic.Height,
            SKColorType.Gray8,
            SKAlphaType.Opaque);
        for (var y = 0; y < enhanced.Height; y++)
        {
            for (var x = 0; x < enhanced.Width; x++)
            {
                var value = (byte)Math.Clamp(
                    (int)Math.Round(output[0, 0, y, x] * 255f),
                    0,
                    255);
                enhanced.SetPixel(x, y, new SKColor(value, value, value));
            }
        }

        return enhanced;
    }

    public void Dispose() => _session.Dispose();
}
