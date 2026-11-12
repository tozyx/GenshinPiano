using System.IO;
using System.IO.Compression;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Application.Updates;
using GenshinPiano.Infrastructure.Ocr;

namespace GenshinPiano.App.Services;

public sealed record OcrAddonInstallResult(bool Updated, string Version, string SourceName);

public sealed record OcrAddonUpdateCheck(string? InstalledVersion, UpdateManifest? Manifest,
    UpdatePackage? Package, DateTimeOffset CheckedAt, string? ErrorMessage = null)
{
    public bool CanUpdate => ErrorMessage is null && Package is not null &&
        (!SemanticVersion.TryParse(InstalledVersion, out var current) || Package.Version.CompareTo(current) > 0);
    public bool IsCurrent => ErrorMessage is null && Package is not null &&
        SemanticVersion.TryParse(InstalledVersion, out var current) && Package.Version.CompareTo(current) <= 0;
}

/// <summary>Downloads and atomically installs the optional OCR component.</summary>
public sealed class OcrAddonPackageManager(
    IUpdateSource source,
    IUpdatePackageDownloader downloader,
    IUpdatePackageVerifier verifier,
    IOcrAddonService addonService,
    string installRoot)
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _runtimeLock = new();
    private int _recognitions;
    private bool _isInstalling;
    public OcrAddonUpdateCheck? LastCheck { get; private set; }
    public bool IsInstalling { get { lock (_runtimeLock) return _isInstalling; } }
    public bool IsRecognizing { get { lock (_runtimeLock) return _recognitions > 0; } }
    public event EventHandler? StatusChanged;

    public IDisposable BeginRecognition()
    {
        lock (_runtimeLock)
        {
            if (_isInstalling) throw new InvalidOperationException("The OCR add-on is being updated.");
            _recognitions++;
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return new RecognitionLease(this);
    }

    private sealed class RecognitionLease(OcrAddonPackageManager manager) : IDisposable
    {
        private OcrAddonPackageManager? _owner = manager;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            lock (owner._runtimeLock) owner._recognitions--;
            owner.StatusChanged?.Invoke(owner, EventArgs.Empty);
        }
    }

    /// <summary>Metadata only: never downloads or replaces the installed runtime.</summary>
    public async Task<OcrAddonUpdateCheck> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        string? installed = null;
        OcrAddonUpdateCheck result;
        try
        {
            var manifest = await source.GetLatestAsync("stable", cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            installed = addonService.FindInstalledAddon()?.EngineVersion;
            var package = manifest?.Packages.FirstOrDefault(candidate =>
                candidate.Kind == UpdatePackageKind.OptionalComponent && candidate.Id == "addon.ocr.win-x64");
            result = new(installed, manifest, package, DateTimeOffset.UtcNow,
                package is null ? "No compatible OCR add-on package was found." : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            installed = addonService.FindInstalledAddon()?.EngineVersion;
            result = new(installed, null, null, DateTimeOffset.UtcNow, error.Message);
        }
        LastCheck = result;
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public async Task<OcrAddonInstallResult> DownloadAndInstallAsync(
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
        => await InstallCheckedUpdateAsync(await CheckForUpdatesAsync(cancellationToken), progress, cancellationToken);

    public async Task<OcrAddonInstallResult> InstallCheckedUpdateAsync(
        OcrAddonUpdateCheck check, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            lock (_runtimeLock)
            {
                if (_recognitions > 0) throw new InvalidOperationException("Stop OCR recognition before updating the add-on.");
                _isInstalling = true;
            }
            StatusChanged?.Invoke(this, EventArgs.Empty);
            var installed = addonService.FindInstalledAddon();
            if (check.ErrorMessage is not null) throw new InvalidOperationException(check.ErrorMessage);
            var manifest = check.Manifest ?? throw new InvalidDataException("No compatible OCR add-on package was found.");
            var package = check.Package ??
                throw new InvalidDataException("The release does not contain a compatible OCR add-on package.");
            if (installed is not null &&
                SemanticVersion.TryParse(installed.EngineVersion, out var installedVersion) &&
                package.Version.CompareTo(installedVersion) <= 0)
            {
                LastCheck = check with { InstalledVersion = installed.EngineVersion };
                return new OcrAddonInstallResult(false, installed.EngineVersion, manifest.SourceName);
            }
            var downloaded = await downloader.DownloadAsync(package, progress, cancellationToken);
            if (!await verifier.VerifyAsync(package, downloaded, cancellationToken))
                throw new InvalidDataException("The OCR add-on failed integrity or signature verification.");
            await InstallAsync(downloaded, package.Version, cancellationToken);
            installed = addonService.FindInstalledAddon() ??
                throw new InvalidDataException("The installed OCR add-on is not compatible with this application.");
            LastCheck = check with { InstalledVersion = installed.EngineVersion };
            return new OcrAddonInstallResult(true, installed.EngineVersion, manifest.SourceName);
        }
        finally
        {
            lock (_runtimeLock) _isInstalling = false;
            _operationLock.Release();
            StatusChanged?.Invoke(this, EventArgs.Empty);
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
                _ = OcrAddonPackageValidator.Validate(target,expectedVersion.ToString());
                if (addonService.FindInstalledAddon() is null)
                    throw new InvalidDataException("The installed OCR add-on could not be loaded.");
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
        _ = OcrAddonPackageValidator.Validate(directory,expectedVersion.ToString());
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
