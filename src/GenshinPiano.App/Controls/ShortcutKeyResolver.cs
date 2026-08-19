using System.Windows.Input;
using System.Runtime.InteropServices;

namespace GenshinPiano.App.Controls;

internal static class ShortcutKeyResolver
{
    public static Key Resolve(KeyEventArgs args) => args.Key switch
    {
        Key.ImeProcessed => args.ImeProcessedKey,
        Key.DeadCharProcessed => args.DeadCharProcessedKey,
        Key.System => args.SystemKey,
        _ => args.Key,
    };

    public static int ResolveBracketStep(KeyEventArgs args)
    {
        var key = Resolve(args);
        if (key == Key.OemOpenBrackets)
        {
            return 1;
        }

        if (key == Key.OemCloseBrackets)
        {
            return -1;
        }

        // Some Chinese IMEs report the event as ImeProcessed without a usable
        // ImeProcessedKey. Read the two physical OEM keys as a Windows-only fallback.
        if ((GetKeyState(VirtualKeyOemOpenBrackets) & KeyDownMask) != 0)
        {
            return 1;
        }

        return (GetKeyState(VirtualKeyOemCloseBrackets) & KeyDownMask) != 0 ? -1 : 0;
    }

    private const int VirtualKeyOemOpenBrackets = 0xDB;
    private const int VirtualKeyOemCloseBrackets = 0xDD;
    private const int KeyDownMask = 0x8000;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
