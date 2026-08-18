using System.Windows.Input;

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
}
