using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GenshinPiano.Application.Ocr;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace GenshinPiano.Ocr.Engine;

internal static class OcrResourceEnvironment
{
    private static string? _root;
    private static bool _resolversInstalled;
    public static string Root => _root ?? AppContext.BaseDirectory;
    public static string WorkRoot { get; private set; } = Path.GetTempPath();
    public static string? SmtModelSha256 { get; private set; }

    public static void Configure(string directory, string? workDirectory = null)
    {
        _root = Path.GetFullPath(directory);
        WorkRoot = workDirectory is null ? Path.GetTempPath() : Path.GetFullPath(workDirectory);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        var schema = manifest.RootElement.GetProperty("schemaVersion").GetInt32();
        if (manifest.RootElement.GetProperty("protocolVersion").GetInt32() != OcrProtocol.CurrentVersion)
            throw new InvalidDataException("The OCR resource package uses an incompatible protocol.");
        SmtModelSha256 = null;
        if (schema == OcrResourceContract.SchemaVersion)
        {
            if (manifest.RootElement.GetProperty("engineCompatibilityVersion").GetInt32() != OcrResourceContract.EngineCompatibilityVersion)
                throw new InvalidDataException("The OCR resource package is incompatible with this engine.");
            SmtModelSha256 = manifest.RootElement.GetProperty("smtModelSha256").GetString();
            if (SmtModelSha256 is not { Length: 64 } || !SmtModelSha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("The OCR resources do not declare a valid SMT model checksum.");
        }
        else if (schema != 1) throw new InvalidDataException("Unsupported OCR resource manifest.");

        if (_resolversInstalled) return;
        // These settings are confined to the short-lived OCR worker, before any inference call.
        if (!SetDefaultDllDirectories(0x00001000)) throw new Win32Exception(Marshal.GetLastWin32Error());
        foreach (var path in new[] { NativeDirectory, Path.Combine(Root, "staff-omr", "smt") })
            if (Directory.Exists(path) && AddDllDirectory(path) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly, ResolveNativeLibrary);
        NativeLibrary.SetDllImportResolver(typeof(SKBitmap).Assembly, ResolveNativeLibrary);
        _resolversInstalled = true;
    }

    private static string NativeDirectory => Directory.Exists(Path.Combine(Root, "native")) ? Path.Combine(Root, "native") : Root;

    private static IntPtr ResolveNativeLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var file = name switch
        {
            "onnxruntime" or "onnxruntime.dll" => "onnxruntime.dll",
            "libSkiaSharp" or "libSkiaSharp.dll" => "libSkiaSharp.dll",
            _ => null,
        };
        return file is null ? IntPtr.Zero : NativeLibrary.Load(Path.Combine(NativeDirectory, file));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AddDllDirectory(string newDirectory);
}
