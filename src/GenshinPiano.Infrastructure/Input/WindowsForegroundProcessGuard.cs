using System.Diagnostics;
using System.Runtime.InteropServices;
using GenshinPiano.Application.Abstractions;

namespace GenshinPiano.Infrastructure.Input;

public sealed class WindowsForegroundProcessGuard : IPlaybackFocusGuard
{
    public static readonly IReadOnlyList<string> DefaultProcessNames =
        ["YuanShen", "GenshinImpact", "freepiano"];

    private readonly HashSet<string> _allowedProcessNames;

    public WindowsForegroundProcessGuard(IEnumerable<string>? allowedProcessNames = null)
    {
        _allowedProcessNames = (allowedProcessNames ?? DefaultProcessNames)
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_allowedProcessNames.Count == 0)
        {
            throw new ArgumentException("At least one playback process must be allowed.", nameof(allowedProcessNames));
        }
    }

    public bool IsPlaybackTargetFocused()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return _allowedProcessNames.Contains(process.ProcessName);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string NormalizeProcessName(string name) =>
        Path.GetFileNameWithoutExtension(name.Trim());

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);
}
