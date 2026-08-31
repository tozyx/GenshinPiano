using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GenshinPiano.App.Services;

public sealed class WindowsNotificationService : IDisposable
{
    private const uint NotificationId = 0x4750;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NifInfo = 16;
    private const uint NiifUser = 0x00000004;
    private const uint NiifLargeIcon = 0x00000020;
    private const int IdiApplication = 32512;

    private readonly System.Threading.Timer _hideTimer;
    private NotifyIconData _activeNotification;
    private bool _hasActiveNotification;
    private IntPtr _ownedSmallIcon;
    private IntPtr _ownedLargeIcon;
    private bool _disposed;

    public WindowsNotificationService()
    {
        _hideTimer = new System.Threading.Timer(HideNotificationIcon);
    }

    public void Show(string title, string message)
    {
        if (_disposed || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message) ||
            System.Windows.Application.Current?.MainWindow is not { } mainWindow)
        {
            return;
        }

        var handle = new WindowInteropHelper(mainWindow).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        RemoveActiveNotification();
        LoadApplicationIcons(out var smallIcon, out var largeIcon);
        _activeNotification = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = handle,
            Id = NotificationId,
            Flags = NifIcon | NifTip | NifInfo,
            IconHandle = smallIcon,
            Tip = "GenshinPiano",
            Info = Truncate(message, 255),
            TimeoutOrVersion = 7000,
            InfoTitle = Truncate(title, 63),
            InfoFlags = NiifUser | NiifLargeIcon,
            BalloonIconHandle = largeIcon,
        };
        _hasActiveNotification = ShellNotifyIcon(NimAdd, ref _activeNotification);
        if (_hasActiveNotification)
        {
            _hideTimer.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hideTimer.Dispose();
        RemoveActiveNotification();
    }

    private void HideNotificationIcon(object? state)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            RemoveActiveNotification();
        });
    }

    private void RemoveActiveNotification()
    {
        if (_hasActiveNotification)
        {
            ShellNotifyIcon(NimDelete, ref _activeNotification);
            _hasActiveNotification = false;
        }

        ReleaseApplicationIcons();
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private void LoadApplicationIcons(out IntPtr smallIcon, out IntPtr largeIcon)
    {
        smallIcon = IntPtr.Zero;
        largeIcon = IntPtr.Zero;
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath) &&
            ExtractIconEx(executablePath, 0, out largeIcon, out smallIcon, 1) > 0)
        {
            _ownedSmallIcon = smallIcon;
            _ownedLargeIcon = largeIcon;
        }

        if (smallIcon == IntPtr.Zero)
        {
            smallIcon = largeIcon != IntPtr.Zero
                ? largeIcon
                : LoadIcon(IntPtr.Zero, (IntPtr)IdiApplication);
        }

        if (largeIcon == IntPtr.Zero)
        {
            largeIcon = smallIcon;
        }
    }

    private void ReleaseApplicationIcons()
    {
        if (_ownedSmallIcon != IntPtr.Zero)
        {
            DestroyIcon(_ownedSmallIcon);
        }

        if (_ownedLargeIcon != IntPtr.Zero && _ownedLargeIcon != _ownedSmallIcon)
        {
            DestroyIcon(_ownedLargeIcon);
        }

        _ownedSmallIcon = IntPtr.Zero;
        _ownedLargeIcon = IntPtr.Zero;
    }

    [DllImport(
        "shell32.dll",
        EntryPoint = "Shell_NotifyIconW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(
        [MarshalAs(UnmanagedType.U4)] uint message,
        ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string filePath,
        int iconIndex,
        out IntPtr largeIcon,
        out IntPtr smallIcon,
        uint iconCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIconHandle;
    }
}
