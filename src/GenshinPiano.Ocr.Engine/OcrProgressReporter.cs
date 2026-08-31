using System.Globalization;
using GenshinPiano.Application.Ocr;

namespace GenshinPiano.Ocr.Engine;

internal static class OcrProgressReporter
{
    public static void Report(OcrProgressStage stage, double progress)
    {
        Console.Error.WriteLine(
            $"OCR_PROGRESS|{stage}|{Math.Clamp(progress, 0, 1).ToString(CultureInfo.InvariantCulture)}");
    }
}
