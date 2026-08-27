using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GenshinPiano.App.Services;

public sealed record FileAssociationState(
    bool ExtensionPointsToGenshinPiano,
    bool OpensWithCurrentExecutable,
    string? RegisteredExecutablePath)
{
    public bool CanUnregister => ExtensionPointsToGenshinPiano;
}

public static class FileAssociationService
{
    public const string ScoreExtension = ".gpiano";
    private const string ProgId = "GenshinPiano.Score";
    private const string ClassesPath = @"Software\Classes";
    private const int ShellChangeNotifyAssociationChanged = 0x08000000;
    private const int ShellChangeNotifyIdList = 0x0000;

    public static FileAssociationState GetState()
    {
        using var classesKey = Registry.CurrentUser.OpenSubKey(ClassesPath);
        var extensionProgId = GetDefaultValue(classesKey, ScoreExtension);
        var extensionPointsToGenshinPiano =
            string.Equals(extensionProgId, ProgId, StringComparison.OrdinalIgnoreCase);

        var registeredCommand = GetDefaultValue(classesKey, $@"{ProgId}\shell\open\command");
        var registeredExecutablePath = ExtractExecutablePath(registeredCommand);
        var currentExecutablePath = TryGetCurrentExecutablePath();
        var opensWithCurrentExecutable =
            extensionPointsToGenshinPiano &&
            !string.IsNullOrWhiteSpace(currentExecutablePath) &&
            PathsEqual(registeredExecutablePath, currentExecutablePath);

        return new FileAssociationState(
            extensionPointsToGenshinPiano,
            opensWithCurrentExecutable,
            registeredExecutablePath);
    }

    public static void RegisterGpianoAssociation()
    {
        var executablePath = GetCurrentExecutablePath();
        using var classesKey = Registry.CurrentUser.CreateSubKey(ClassesPath, writable: true)
                               ?? throw new InvalidOperationException("Could not open user file association registry.");

        using (var extensionKey = classesKey.CreateSubKey(ScoreExtension, writable: true)
                                  ?? throw new InvalidOperationException("Could not create .gpiano registry key."))
        {
            extensionKey.SetValue(null, ProgId, RegistryValueKind.String);
            extensionKey.SetValue("Content Type", "application/x-genshinpiano-score", RegistryValueKind.String);
            extensionKey.SetValue("PerceivedType", "document", RegistryValueKind.String);
        }

        using (var openWithKey = classesKey.CreateSubKey($@"{ScoreExtension}\OpenWithProgids", writable: true)
                                 ?? throw new InvalidOperationException("Could not create .gpiano OpenWith registry key."))
        {
            openWithKey.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using (var progIdKey = classesKey.CreateSubKey(ProgId, writable: true)
                               ?? throw new InvalidOperationException("Could not create GenshinPiano score registry key."))
        {
            progIdKey.SetValue(null, "GenshinPiano score", RegistryValueKind.String);
            progIdKey.SetValue("FriendlyTypeName", "GenshinPiano score", RegistryValueKind.String);
        }

        using (var iconKey = classesKey.CreateSubKey($@"{ProgId}\DefaultIcon", writable: true)
                             ?? throw new InvalidOperationException("Could not create GenshinPiano icon registry key."))
        {
            iconKey.SetValue(null, $"{Quote(executablePath)},0", RegistryValueKind.String);
        }

        using (var commandKey = classesKey.CreateSubKey($@"{ProgId}\shell\open\command", writable: true)
                                ?? throw new InvalidOperationException("Could not create GenshinPiano open command registry key."))
        {
            commandKey.SetValue(null, $"{Quote(executablePath)} \"%1\"", RegistryValueKind.String);
        }

        NotifyShellAssociationsChanged();
    }

    public static void UnregisterGpianoAssociation()
    {
        using var classesKey = Registry.CurrentUser.OpenSubKey(ClassesPath, writable: true);
        if (classesKey is null)
        {
            return;
        }

        var extensionProgId = GetDefaultValue(classesKey, ScoreExtension);
        if (string.Equals(extensionProgId, ProgId, StringComparison.OrdinalIgnoreCase))
        {
            classesKey.DeleteSubKeyTree(ScoreExtension, throwOnMissingSubKey: false);
        }

        classesKey.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        NotifyShellAssociationsChanged();
    }

    private static string? GetDefaultValue(RegistryKey? root, string subKeyPath)
    {
        using var key = root?.OpenSubKey(subKeyPath);
        return key?.GetValue(null) as string;
    }

    private static string GetCurrentExecutablePath() =>
        TryGetCurrentExecutablePath()
        ?? throw new InvalidOperationException("Could not resolve the current executable path.");

    private static string? TryGetCurrentExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuoteIndex = trimmed.IndexOf('"', startIndex: 1);
            return closingQuoteIndex > 1 ? trimmed[1..closingQuoteIndex] : null;
        }

        var firstSpaceIndex = trimmed.IndexOf(' ');
        return firstSpaceIndex > 0 ? trimmed[..firstSpaceIndex] : trimmed;
    }

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
        catch (NotSupportedException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
        catch (PathTooLongException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Quote(string path) => $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void NotifyShellAssociationsChanged() =>
        SHChangeNotify(ShellChangeNotifyAssociationChanged, ShellChangeNotifyIdList, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
