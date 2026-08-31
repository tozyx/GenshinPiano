using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GenshinPiano.Ocr.Engine;

internal sealed class OrpheusNetClassifier : IDisposable
{
    // SRCNN is useful when the source glyph contains few actual pixels. A
    // Laplacian blur score is deliberately not used here: notation is often
    // binarized, and JPEG noise or hard threshold edges can make a blurry
    // glyph score as "sharper" than a clean one.
    private const float EnhancementMaximumHeight = 48;
    private const float EnhancementMaximumWidth = 80;

    private static readonly string[] Labels =
    [
        "(", ")", "0", "1", "2", "3", "4", "5", "6", "7",
        "breath", "flat", "sharp",
    ];

    private readonly InferenceSession _session;
    private readonly SrcnnEnhancer? _enhancer;
    private int _enhancementAttempts;
    private int _enhancementAgreements;
    private int _enhancementDisagreements;

    public OrpheusNetClassifier(string modelPath)
    {
        _session = new InferenceSession(modelPath);
        var enhancementPath = Path.Combine(
            Path.GetDirectoryName(modelPath) ?? string.Empty,
            "orpheusnet-srcnn-x3.onnx");
        if (File.Exists(enhancementPath))
        {
            _enhancer = new SrcnnEnhancer(enhancementPath);
        }
    }

    public ClassificationResult Classify(SKBitmap source, SKRect bounds)
    {
        var original = ClassifyCore(source, bounds);
        if (_enhancer is null ||
            bounds.Height > EnhancementMaximumHeight ||
            bounds.Width > EnhancementMaximumWidth)
        {
            return original;
        }

        using var enhanced = _enhancer.Enhance(source, bounds);
        if (enhanced is null)
        {
            return original;
        }

        _enhancementAttempts++;
        var restored = ClassifyCore(
            enhanced,
            new SKRect(0, 0, enhanced.Width, enhanced.Height));
        if (restored.Label == original.Label)
        {
            _enhancementAgreements++;
            return restored.Confidence > original.Confidence ? restored : original;
        }

        // Keep disagreements as shadow results until row context or another
        // detector can provide independent evidence. Confidence alone is not
        // enough because both networks can be confidently wrong on a watermark.
        _enhancementDisagreements++;
        return original;
    }

    public ClassificationResult ClassifyWithoutEnhancement(SKBitmap source, SKRect bounds) =>
        ClassifyCore(source, bounds);

    private ClassificationResult ClassifyCore(SKBitmap source, SKRect bounds)
    {
        var tensor = CreateInput(source, bounds);
        var input = NamedOnnxValue.CreateFromTensor("image", tensor);
        using var results = _session.Run([input]);
        var logits = results.First().AsEnumerable<float>().ToArray();
        var bestIndex = 0;
        for (var index = 1; index < logits.Length; index++)
        {
            if (logits[index] > logits[bestIndex])
            {
                bestIndex = index;
            }
        }

        var max = logits.Max();
        var denominator = logits.Sum(value => Math.Exp(value - max));
        var confidence = denominator <= 0
            ? 0
            : Math.Exp(logits[bestIndex] - max) / denominator;
        var secondBest = logits
            .Where((_, index) => index != bestIndex)
            .Max();
        var secondConfidence = denominator <= 0
            ? 0
            : Math.Exp(secondBest - max) / denominator;
        return new ClassificationResult(
            Labels[bestIndex],
            confidence,
            confidence - secondConfidence);
    }

    public void Dispose()
    {
        if (_enhancementAttempts > 0 && DiagnosticsEnabled())
        {
            Console.Error.WriteLine(
                $"OCR SRCNN: attempts={_enhancementAttempts}, " +
                $"agreements={_enhancementAgreements}, " +
                $"disagreements={_enhancementDisagreements}");
        }

        _enhancer?.Dispose();
        _session.Dispose();
    }

    private static bool DiagnosticsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("GENSHINPIANO_OCR_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal);

    private static DenseTensor<float> CreateInput(SKBitmap source, SKRect bounds)
    {
        const int inputSize = 28;
        // Match OrpheusNet's training-time resize_and_pad_image exactly: the
        // longest glyph side fills the full 28-pixel input. Keeping an extra
        // two-pixel margin here made already-small glyphs from compressed
        // scores such as Lemon even smaller than anything seen in training.
        const float contentSize = inputSize;
        var clipped = SKRect.Intersect(
            bounds,
            new SKRect(0, 0, source.Width, source.Height));
        using var normalized = new SKBitmap(inputSize, inputSize, SKColorType.Gray8, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.White);
            var scale = Math.Min(contentSize / clipped.Width, contentSize / clipped.Height);
            var width = clipped.Width * scale;
            var height = clipped.Height * scale;
            var left = (inputSize - width) / 2f;
            var top = (inputSize - height) / 2f;
            canvas.DrawBitmap(source, clipped, new SKRect(left, top, left + width, top + height));
        }

        var tensor = new DenseTensor<float>([1, 1, inputSize, inputSize]);
        for (var y = 0; y < inputSize; y++)
        {
            for (var x = 0; x < inputSize; x++)
            {
                var value = normalized.GetPixel(x, y).Red / 255f;
                tensor[0, 0, y, x] = (value - 0.5f) / 0.5f;
            }
        }

        return tensor;
    }

    internal sealed record ClassificationResult(
        string Label,
        double Confidence,
        double Margin);
}
