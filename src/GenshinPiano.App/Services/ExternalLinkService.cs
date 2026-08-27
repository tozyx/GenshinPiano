using System.Diagnostics;

namespace GenshinPiano.App.Services;

public static class ExternalLinkService
{
    public static bool TryOpen(string url, out Exception? exception)
    {
        exception = null;
        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            exception = ex;
            return false;
        }
    }
}
