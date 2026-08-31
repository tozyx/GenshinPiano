using System.Runtime.InteropServices;

namespace GenshinPiano.Ocr.Engine;

internal static class WindowsProcessIdentity
{
    private const string OcrApplicationId = "tozyx.GenshinPiano.OcrEngine";

    public static void Configure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(OcrApplicationId);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Process grouping is cosmetic. OCR must remain usable if the shell
            // API is unavailable on an unusual Windows environment.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
