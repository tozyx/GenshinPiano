using System.Globalization;
using System.Text;
using GenshinPiano.Application.Ocr;

namespace GenshinPiano.Ocr.Engine;

internal static class OcrProgressReporter
{
    private static readonly object FileSync = new();
    private static string? _progressPath;

    public static void WriteToFile(string? progressPath)
    {
        _progressPath = string.IsNullOrWhiteSpace(progressPath)
            ? null
            : Path.GetFullPath(progressPath);

        if (_progressPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_progressPath)!);
        }
    }

    public static void Report(OcrProgressStage stage, double progress)
    {
        var line =
            $"OCR_PROGRESS|{stage}|{Math.Clamp(progress, 0, 1).ToString(CultureInfo.InvariantCulture)}";
        if (_progressPath is null)
        {
            Console.Error.WriteLine(line);
            return;
        }

        lock (FileSync)
        {
            File.AppendAllText(_progressPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
