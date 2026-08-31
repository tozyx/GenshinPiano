using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GenshinPiano.App.Services;

public sealed class WindowsTaskbarProgressService
{
    private ITaskbarList3? _taskbar;
    private bool _unavailable;

    public void SetProgress(double progress)
    {
        if (!TryGetWindowHandle(out var handle) || !TryGetTaskbar(out var taskbar))
        {
            return;
        }

        try
        {
            taskbar.SetProgressState(handle, TaskbarProgressState.Normal);
            taskbar.SetProgressValue(
                handle,
                (ulong)Math.Round(Math.Clamp(progress, 0, 1) * 1000),
                1000);
        }
        catch (COMException)
        {
            _unavailable = true;
        }
    }

    public void Clear()
    {
        if (!TryGetWindowHandle(out var handle) || !TryGetTaskbar(out var taskbar))
        {
            return;
        }

        try
        {
            taskbar.SetProgressState(handle, TaskbarProgressState.None);
        }
        catch (COMException)
        {
            _unavailable = true;
        }
    }

    private bool TryGetTaskbar(out ITaskbarList3 taskbar)
    {
        taskbar = null!;
        if (_unavailable || !OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            return false;
        }

        try
        {
            _taskbar ??= (ITaskbarList3)new TaskbarList();
            _taskbar.HrInit();
            taskbar = _taskbar;
            return true;
        }
        catch (COMException)
        {
            _unavailable = true;
            return false;
        }
    }

    private static bool TryGetWindowHandle(out IntPtr handle)
    {
        handle = System.Windows.Application.Current?.MainWindow is { } window
            ? new WindowInteropHelper(window).Handle
            : IntPtr.Zero;
        return handle != IntPtr.Zero;
    }

    private enum TaskbarProgressState
    {
        None = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    private class TaskbarList;

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr windowHandle);
        void DeleteTab(IntPtr windowHandle);
        void ActivateTab(IntPtr windowHandle);
        void SetActiveAlt(IntPtr windowHandle);
        void MarkFullscreenWindow(IntPtr windowHandle, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr windowHandle, ulong completed, ulong total);
        void SetProgressState(IntPtr windowHandle, TaskbarProgressState state);
    }
}
