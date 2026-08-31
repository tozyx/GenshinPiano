using System.Runtime.InteropServices;

namespace GenshinPiano.App.Services;

internal static class WindowsAppIdentity
{
    public const string MainApplicationId = "tozyx.GenshinPiano";

    public static bool TrySetCurrentProcess(string applicationId)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        try
        {
            return SetCurrentProcessExplicitAppUserModelID(applicationId) >= 0;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
