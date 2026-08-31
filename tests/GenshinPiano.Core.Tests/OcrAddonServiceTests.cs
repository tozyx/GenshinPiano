using System.Text.Json;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Infrastructure.Ocr;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class OcrAddonServiceTests
{
    [Fact]
    public void FindInstalledAddon_ReturnsCompatibleEngineInsideAddonDirectory()
    {
        using var directory = new TemporaryDirectory();
        var executablePath = Path.Combine(directory.Path, "engine.exe");
        File.WriteAllBytes(executablePath, []);
        WriteManifest(directory.Path, "engine.exe", OcrProtocol.CurrentVersion);

        var descriptor = new ExternalOcrAddonService(directory.Path).FindInstalledAddon();

        Assert.NotNull(descriptor);
        Assert.Equal("0.1.0", descriptor.EngineVersion);
        Assert.Equal(executablePath, descriptor.ExecutablePath);
        Assert.Equal(OcrAddonLaunchMode.Stdio, descriptor.LaunchMode);
    }

    [Fact]
    public void FindInstalledAddon_ReadsFileLaunchMode()
    {
        using var directory = new TemporaryDirectory();
        var executablePath = Path.Combine(directory.Path, "engine.exe");
        File.WriteAllBytes(executablePath, []);
        WriteManifest(
            directory.Path,
            "engine.exe",
            OcrProtocol.CurrentVersion,
            OcrAddonLaunchMode.File);

        var descriptor = new ExternalOcrAddonService(directory.Path).FindInstalledAddon();

        Assert.NotNull(descriptor);
        Assert.Equal(OcrAddonLaunchMode.File, descriptor.LaunchMode);
    }

    [Fact]
    public void FindInstalledAddon_RejectsIncompatibleProtocol()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllBytes(Path.Combine(directory.Path, "engine.exe"), []);
        WriteManifest(directory.Path, "engine.exe", OcrProtocol.CurrentVersion + 1);

        var descriptor = new ExternalOcrAddonService(directory.Path).FindInstalledAddon();

        Assert.Null(descriptor);
    }

    [Fact]
    public void FindInstalledAddon_RejectsExecutableOutsideAddonDirectory()
    {
        using var root = new TemporaryDirectory();
        var addonDirectory = Path.Combine(root.Path, "ocr");
        Directory.CreateDirectory(addonDirectory);
        File.WriteAllBytes(Path.Combine(root.Path, "outside.exe"), []);
        WriteManifest(addonDirectory, "../outside.exe", OcrProtocol.CurrentVersion);

        var descriptor = new ExternalOcrAddonService(addonDirectory).FindInstalledAddon();

        Assert.Null(descriptor);
    }

    private static void WriteManifest(
        string directory,
        string executable,
        int protocolVersion,
        OcrAddonLaunchMode? launchMode = null)
    {
        var manifest = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["protocolVersion"] = protocolVersion,
            ["engineVersion"] = "0.1.0",
            ["executable"] = executable,
        };
        if (launchMode is not null)
        {
            manifest["launchMode"] = launchMode.Value.ToString().ToLowerInvariant();
        }

        var json = JsonSerializer.Serialize(manifest);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), json);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"GenshinPiano-OcrTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
