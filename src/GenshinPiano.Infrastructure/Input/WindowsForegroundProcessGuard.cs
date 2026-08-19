using System.Diagnostics;
using System.Runtime.InteropServices;
using GenshinPiano.Application.Abstractions;

namespace GenshinPiano.Infrastructure.Input;

public sealed class WindowsForegroundProcessGuard : IPlaybackFocusGuard
{
    public static readonly IReadOnlyList<string> DefaultProcessNames =
        ["YuanShen", "GenshinImpact", "freepiano"];

    private readonly IReadOnlyList<string> _allowedProcessNames;
    private readonly HashSet<string> _allowedProcessNameSet;

    public WindowsForegroundProcessGuard(IEnumerable<string>? allowedProcessNames = null)
    {
        _allowedProcessNames = (allowedProcessNames ?? DefaultProcessNames)
            .Select(NormalizeProcessName)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _allowedProcessNameSet = _allowedProcessNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

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
            return _allowedProcessNameSet.Contains(process.ProcessName);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public bool TryFocusFirstPlaybackTarget()
    {
        foreach (var processName in _allowedProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName).OrderBy(process => process.Id))
            {
                using (process)
                {
                    try
                    {
                        var window = process.MainWindowHandle;
                        if (window == IntPtr.Zero)
                        {
                            continue;
                        }

                        _ = ShowWindowAsync(window, RestoreWindow);
                        if (SetForegroundWindow(window))
                        {
                            return true;
                        }
                    }
                    catch (Exception exception) when (
                        exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // The process may exit while its window is being inspected.
                    }
                }
            }
        }

        return false;
    }

    private static string NormalizeProcessName(string name) =>
        Path.GetFileNameWithoutExtension(name.Trim());

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    private const int RestoreWindow = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);
}
