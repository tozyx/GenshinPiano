using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GenshinPiano.Infrastructure.Input;

public sealed class WindowsGlobalEscapeListener : IDisposable
{
    private const int LowLevelKeyboardHook = 13;
    private const int VirtualKeyEscape = 0x1B;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const int SystemKeyDownMessage = 0x0104;
    private const int SystemKeyUpMessage = 0x0105;

    private readonly HookProcedure _hookProcedure;
    private IntPtr _hookHandle;
    private bool _escapeIsDown;

    public WindowsGlobalEscapeListener()
    {
        _hookProcedure = OnKeyboardEvent;
        _hookHandle = SetWindowsHookEx(
            LowLevelKeyboardHook,
            _hookProcedure,
            GetModuleHandle(null),
            0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the global Escape listener.");
        }
    }

    public event EventHandler? EscapePressed;

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private IntPtr OnKeyboardEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == VirtualKeyEscape)
        {
            var messageId = unchecked((int)message.ToInt64());
            if (messageId is KeyDownMessage or SystemKeyDownMessage)
            {
                if (!_escapeIsDown)
                {
                    _escapeIsDown = true;
                    try
                    {
                        EscapePressed?.Invoke(this, EventArgs.Empty);
                    }
                    catch
                    {
                        // Exceptions must not escape a low-level Windows hook callback.
                    }
                }
            }
            else if (messageId is KeyUpMessage or SystemKeyUpMessage)
            {
                _escapeIsDown = false;
            }
        }

        // Returning the next hook result keeps Escape available to the game.
        return CallNextHookEx(_hookHandle, code, message, data);
    }

    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookProcedure callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr message,
        IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
