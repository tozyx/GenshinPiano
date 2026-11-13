using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GenshinPiano.Infrastructure.Ocr;

/// <summary>Gives the bundled worker a distinct image name without publishing a second bundle.</summary>
internal sealed class OcrWorkerExecutable(string applicationExecutable, Func<string, string, bool>? createLink = null)
{
    internal const string FileName = "GenshinPiano.Ocr.exe";
    private readonly object _gate = new();
    private string? _preparedPath;

    public string Prepare()
    {
        lock (_gate)
        {
            if (_preparedPath is not null) return _preparedPath;
            var source = Path.GetFullPath(applicationExecutable);
            var directory = Path.GetDirectoryName(source)!;
            var destination = Path.Combine(directory, FileName);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) || SameContent(source, destination))
                return _preparedPath = destination;

            // A hard link shares the existing single-file bundle on NTFS. Other filesystems
            // use a local copy; neither variant adds another runtime to the downloadable ZIP.
            var temporary = Path.Combine(directory, $".GenshinPiano.Ocr-{Guid.NewGuid():N}.tmp");
            try
            {
                if (!(createLink ?? TryCreateLink)(temporary, source)) File.Copy(source, temporary);
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return _preparedPath = destination;
        }
    }

    private static bool SameContent(string source, string destination)
    {
        if (!File.Exists(destination) || new FileInfo(source).Length != new FileInfo(destination).Length) return false;
        using var sourceStream = File.OpenRead(source);
        using var destinationStream = File.OpenRead(destination);
        return SHA256.HashData(sourceStream).AsSpan().SequenceEqual(SHA256.HashData(destinationStream));
    }

    private static bool TryCreateLink(string destination, string source) =>
        OperatingSystem.IsWindows() && CreateHardLink(destination, source, IntPtr.Zero);

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
