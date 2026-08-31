using SkiaSharp;

namespace GenshinPiano.Ocr.Engine;

internal static class NotationGeometryAnalyzer
{
    private const byte InkThreshold = 145;

    public static GeometryFeatures Analyze(SKBitmap source, SKRect glyph)
    {
        if (glyph.Width < 2 || glyph.Height < 2)
        {
            return GeometryFeatures.Empty;
        }

        var roi = SKRectI.Intersect(
            new SKRectI(
                (int)Math.Floor(glyph.Left - (glyph.Width * 0.35f)),
                (int)Math.Floor(glyph.Top - (glyph.Height * 1.75f)),
                (int)Math.Ceiling(glyph.Right + (glyph.Width * 1.15f)),
                (int)Math.Ceiling(glyph.Bottom + (glyph.Height * 1.85f))),
            new SKRectI(0, 0, source.Width, source.Height));
        if (roi.IsEmpty)
        {
            return GeometryFeatures.Empty;
        }

        var components = FindComponents(source, roi);
        var digitBody = FindDigitBody(components, glyph);
        if (digitBody is null)
        {
            return GeometryFeatures.Empty;
        }

        var reference = digitBody.Bounds;
        var upperDots = components.Count(component =>
            component != digitBody && IsOctaveDot(component, reference, above: true));
        var lowerDots = components.Count(component =>
            component != digitBody && IsOctaveDot(component, reference, above: false));
        var underlineCount = components.Count(component =>
            component != digitBody && IsUnderline(component, reference));
        var isDotted = components.Any(component =>
            component != digitBody && IsAugmentationDot(component, reference));
        return new GeometryFeatures(
            Math.Clamp(upperDots, 0, 2) - Math.Clamp(lowerDots, 0, 2),
            Math.Clamp(underlineCount, 0, 3),
            isDotted,
            digitBody.Bounds);
    }

    private static InkComponent? FindDigitBody(
        IEnumerable<InkComponent> components,
        SKRect ocrBounds) =>
        components
            .Where(component =>
                component.Bounds.MidX >= ocrBounds.Left - (ocrBounds.Width * 0.10f) &&
                component.Bounds.MidX <= ocrBounds.Right + (ocrBounds.Width * 0.10f) &&
                component.Bounds.MidY >= ocrBounds.Top - (ocrBounds.Height * 0.10f) &&
                component.Bounds.MidY <= ocrBounds.Bottom + (ocrBounds.Height * 0.10f) &&
                component.Bounds.Height >= Math.Max(3, ocrBounds.Height * 0.22f))
            .OrderByDescending(component => component.Bounds.Height)
            .ThenByDescending(component => component.Pixels)
            .FirstOrDefault();

    private static List<InkComponent> FindComponents(SKBitmap source, SKRectI roi)
    {
        var width = roi.Width;
        var height = roi.Height;
        var ink = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = source.GetPixel(roi.Left + x, roi.Top + y);
                var luminance = ((77 * color.Red) + (150 * color.Green) + (29 * color.Blue)) >> 8;
                ink[(y * width) + x] = luminance < InkThreshold;
            }
        }

        var visited = new bool[ink.Length];
        var components = new List<InkComponent>();
        var queue = new Queue<int>();
        for (var start = 0; start < ink.Length; start++)
        {
            if (!ink[start] || visited[start])
            {
                continue;
            }

            visited[start] = true;
            queue.Enqueue(start);
            var minX = width;
            var minY = height;
            var maxX = 0;
            var maxY = 0;
            var pixels = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var x = current % width;
                var y = current / width;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                pixels++;

                for (var deltaY = -1; deltaY <= 1; deltaY++)
                {
                    for (var deltaX = -1; deltaX <= 1; deltaX++)
                    {
                        if (deltaX == 0 && deltaY == 0)
                        {
                            continue;
                        }

                        var nextX = x + deltaX;
                        var nextY = y + deltaY;
                        if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                        {
                            continue;
                        }

                        var next = (nextY * width) + nextX;
                        if (ink[next] && !visited[next])
                        {
                            visited[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            if (pixels >= 2)
            {
                components.Add(new InkComponent(
                    new SKRect(
                        roi.Left + minX,
                        roi.Top + minY,
                        roi.Left + maxX + 1,
                        roi.Top + maxY + 1),
                    pixels));
            }
        }

        return components;
    }

    private static bool IsOctaveDot(InkComponent component, SKRect glyph, bool above)
    {
        var bounds = component.Bounds;
        var verticalMatch = above
            ? bounds.Bottom <= glyph.Top - Math.Max(1, glyph.Height * 0.04f) &&
              bounds.Bottom >= glyph.Top - (glyph.Height * 1.45f)
            : bounds.Top >= glyph.Bottom + Math.Max(1, glyph.Height * 0.04f) &&
              bounds.Top <= glyph.Bottom + (glyph.Height * 1.45f);
        if (!verticalMatch || bounds.MidX < glyph.Left - (glyph.Width * 0.25f) ||
            bounds.MidX > glyph.Right + (glyph.Width * 0.25f))
        {
            return false;
        }

        var minSize = Math.Max(2, glyph.Height * 0.07f);
        var maxSize = glyph.Height * 0.38f;
        var aspect = bounds.Width / Math.Max(1, bounds.Height);
        var fill = component.Pixels / Math.Max(1, bounds.Width * bounds.Height);
        return bounds.Width >= minSize && bounds.Height >= minSize &&
               bounds.Width <= maxSize && bounds.Height <= maxSize &&
               aspect is >= 0.45f and <= 2.2f && fill >= 0.25f;
    }

    private static bool IsUnderline(InkComponent component, SKRect glyph)
    {
        var bounds = component.Bounds;
        if (bounds.Top < glyph.Bottom + Math.Max(1, glyph.Height * 0.08f) ||
            bounds.Top > glyph.Bottom + (glyph.Height * 1.65f))
        {
            return false;
        }

        var overlapsCenter = bounds.Left <= glyph.MidX && bounds.Right >= glyph.MidX;
        var aspect = bounds.Width / Math.Max(1, bounds.Height);
        return overlapsCenter &&
               bounds.Width >= glyph.Width * 0.45f &&
               bounds.Height <= glyph.Height * 0.22f &&
               aspect >= 2.2f;
    }

    private static bool IsAugmentationDot(InkComponent component, SKRect glyph)
    {
        var bounds = component.Bounds;
        if (bounds.Left < glyph.Right + Math.Max(1, glyph.Width * 0.08f) ||
            bounds.Left > glyph.Right + (glyph.Width * 0.65f) ||
            bounds.MidY < glyph.Top + (glyph.Height * 0.28f) ||
            bounds.MidY > glyph.Bottom - (glyph.Height * 0.28f))
        {
            return false;
        }

        var maxSize = glyph.Height * 0.38f;
        var aspect = bounds.Width / Math.Max(1, bounds.Height);
        var fill = component.Pixels / Math.Max(1, bounds.Width * bounds.Height);
        return bounds.Width >= 2 && bounds.Height >= 2 &&
               bounds.Width <= maxSize && bounds.Height <= maxSize &&
               aspect is >= 0.45f and <= 2.2f && fill >= 0.25f;
    }

    private sealed record InkComponent(SKRect Bounds, int Pixels);
}

internal sealed record GeometryFeatures(
    int OctaveShift,
    int UnderlineCount,
    bool IsDotted,
    SKRect? DigitBounds)
{
    public static GeometryFeatures Empty { get; } = new(0, 0, false, null);
}
