using SkiaSharp;

namespace GenshinPiano.Ocr.Engine;

internal static class StaffNotationDetector
{
    public static bool LooksLikeStaffNotation(string imagePath)
    {
        if (!File.Exists(imagePath)) return false;
        using var image = SKBitmap.Decode(imagePath);
        if (image is null || image.Width < 80 || image.Height < 80) return false;

        var stepX = Math.Max(1, image.Width / 900);
        var minimumDarkPixels = Math.Max(24, (image.Width / stepX) * 35 / 100);
        var candidateRows = new List<int>();
        for (var y = 0; y < image.Height; y++)
        {
            var dark = 0;
            for (var x = 0; x < image.Width; x += stepX)
            {
                var color = image.GetPixel(x, y);
                var luminance = (color.Red * 299 + color.Green * 587 + color.Blue * 114) / 1000;
                if (luminance < 145) dark++;
            }
            if (dark >= minimumDarkPixels) candidateRows.Add(y);
        }

        var centers = CollapseAdjacentRows(candidateRows);
        for (var i = 0; i + 4 < centers.Count; i++)
        {
            var gaps = new[]
            {
                centers[i + 1] - centers[i], centers[i + 2] - centers[i + 1],
                centers[i + 3] - centers[i + 2], centers[i + 4] - centers[i + 3],
            };
            var average = gaps.Average();
            if (average < 2 || average > image.Height / 20d) continue;
            if (gaps.All(gap => Math.Abs(gap - average) <= Math.Max(2, average * 0.28))) return true;
        }
        return false;
    }

    private static List<int> CollapseAdjacentRows(IReadOnlyList<int> rows)
    {
        var centers = new List<int>();
        for (var index = 0; index < rows.Count;)
        {
            var start = rows[index];
            var end = start;
            while (++index < rows.Count && rows[index] <= end + 2) end = rows[index];
            centers.Add((start + end) / 2);
        }
        return centers;
    }
}
