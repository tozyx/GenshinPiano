using System.IO;
using System.IO.Compression;
using System.Text.Json;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.App.Services;

public sealed record OcrAddonInstallResult(bool Updated, string Version, string SourceName);

/// <summary>Downloads and atomically installs the optional OCR component.</summary>
public sealed class OcrAddonPackageManager(
    IUpdateSource source,
    IUpdatePackageDownloader downloader,
    IUpdatePackageVerifier verifier,
    IOcrAddonService addonService,
    string installRoot)
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public async Task<OcrAddonInstallResult> DownloadAndInstallAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var installed = addonService.FindInstalledAddon();
            var manifest = await source.GetLatestAsync("stable", cancellationToken) ??
                throw new InvalidOperationException("No compatible OCR add-on package was found.");
            var package = manifest.Packages.FirstOrDefault(candidate =>
                candidate.Kind == UpdatePackageKind.OptionalComponent && candidate.Id == "addon.ocr.win-x64") ??
                throw new InvalidDataException("The release does not contain a compatible OCR add-on package.");
            if (installed is not null &&
                SemanticVersion.TryParse(installed.EngineVersion, out var installedVersion) &&
                package.Version.CompareTo(installedVersion) <= 0)
                return new OcrAddonInstallResult(false, installed.EngineVersion, manifest.SourceName);
            var downloaded = await downloader.DownloadAsync(package, progress, cancellationToken);
            if (!await verifier.VerifyAsync(package, downloaded, cancellationToken))
                throw new InvalidDataException("The OCR add-on failed integrity or signature verification.");
            await InstallAsync(downloaded, package.Version, cancellationToken);
            installed = addonService.FindInstalledAddon() ??
                throw new InvalidDataException("The installed OCR add-on is not compatible with this application.");
            return new OcrAddonInstallResult(true, installed.EngineVersion, manifest.SourceName);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task InstallAsync(
        string archivePath, SemanticVersion expectedVersion, CancellationToken cancellationToken)
    {
        var cacheRoot = Path.Combine(installRoot, "update-cache", "ocr-addon");
        var token = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(cacheRoot, "staging", token);
        var extractedAddon = Path.Combine(stagingRoot, "addons", "ocr");
        var target = Path.Combine(installRoot, "addons", "ocr");
        var backup = Path.Combine(cacheRoot, "backup", token);
        Directory.CreateDirectory(stagingRoot);
        try
        {
            await ExtractSafelyAsync(archivePath, stagingRoot, cancellationToken);
            ValidateStaging(extractedAddon, expectedVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(target, backup);
            }
            try
            {
                Directory.Move(extractedAddon, target);
            }
            catch
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                if (Directory.Exists(backup)) Directory.Move(backup, target);
                throw;
            }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
        }
    }

    private static void ValidateStaging(string directory, SemanticVersion expectedVersion)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("OCR package manifest.json is missing.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        var version = root.TryGetProperty("engineVersion", out var versionElement)
            ? versionElement.GetString() : null;
        var executable = root.TryGetProperty("executable", out var executableElement)
            ? executableElement.GetString() : null;
        if (!SemanticVersion.TryParse(version, out var parsed) || parsed.CompareTo(expectedVersion) != 0)
            throw new InvalidDataException("OCR package version does not match its signed release metadata.");
        if (string.IsNullOrWhiteSpace(executable) ||
            !File.Exists(Path.Combine(directory, Path.GetFileName(executable))))
            throw new InvalidDataException("OCR package executable is missing.");
    }

    private static async Task ExtractSafelyAsync(
        string archivePath, string destination, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe ZIP entry: {entry.FullName}");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(
                target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
