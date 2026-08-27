using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.App.Services;

public sealed class LocalSimulationUpdateSource(
    SemanticVersion currentVersion,
    string? manifestPath = null) : IUpdateSource
{
    private readonly string _manifestPath = manifestPath ?? Path.Combine(
        AppContext.BaseDirectory,
        "config",
        "update-simulation.json");

    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_manifestPath))
        {
            return new UpdateManifest(
                1,
                channel,
                currentVersion,
                DateTimeOffset.UtcNow,
                [],
                "Local simulation");
        }

        await using var stream = File.OpenRead(_manifestPath);
        var document = await JsonSerializer.DeserializeAsync<SimulationManifest>(
            stream,
            cancellationToken: cancellationToken);
        if (document is null || !SemanticVersion.TryParse(document.Version, out var version))
        {
            throw new InvalidDataException("The local update simulation manifest is invalid.");
        }

        if (string.IsNullOrWhiteSpace(document.PackagePath))
        {
            throw new InvalidDataException("The local update manifest must specify packagePath.");
        }

        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(_manifestPath))!;
        var packagePath = ResolvePath(manifestDirectory, document.PackagePath);
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("The local update package was not found.", packagePath);
        }

        var checksumPath = ResolvePath(
            manifestDirectory,
            string.IsNullOrWhiteSpace(document.Sha256Path)
                ? packagePath + ".sha256"
                : document.Sha256Path);
        if (!File.Exists(checksumPath))
        {
            throw new FileNotFoundException("The local update checksum file was not found.", checksumPath);
        }

        var checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken);
        var checksum = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (checksum is null || checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The local update checksum file is invalid.");
        }

        var package = new UpdatePackage(
            "app.win-x64.simulated",
            UpdatePackageKind.Application,
            version,
            Path.GetFileName(packagePath),
            new FileInfo(packagePath).Length,
            checksum.ToLowerInvariant(),
            new Uri(packagePath));
        return new UpdateManifest(
            1,
            channel,
            version,
            DateTimeOffset.UtcNow,
            [package],
            "Local simulation",
            document.ReleaseNotes);
    }

    private sealed record SimulationManifest
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = "3.0.1-preview.1";

        [JsonPropertyName("packagePath")]
        public string PackagePath { get; init; } = string.Empty;

        [JsonPropertyName("sha256Path")]
        public string Sha256Path { get; init; } = string.Empty;

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; init; } = string.Empty;
    }

    private static string ResolvePath(string baseDirectory, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
}

public sealed class SimulatedUpdatePackageDownloader : IUpdatePackageDownloader
{
    public async Task<string> DownloadAsync(
        UpdatePackage package,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        if (!package.DownloadUri.IsFile)
        {
            throw new InvalidOperationException("The local update package URI is not a file path.");
        }

        var destination = Path.Combine(
            AppContext.BaseDirectory,
            "update-cache",
            "simulated",
            package.FileName + ".partial");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && new FileInfo(destination).Length == package.Size)
        {
            await using var cached = File.OpenRead(destination);
            var cachedHash = await System.Security.Cryptography.SHA256.HashDataAsync(cached, cancellationToken);
            if (Convert.ToHexString(cachedHash).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                progress.Report(1);
                return destination;
            }
        }
        await using var input = new FileStream(
            package.DownloadUri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            progress.Report(input.Length == 0 ? 1 : copied / (double)input.Length);
            await Task.Delay(15, cancellationToken);
        }

        return destination;
    }
}

public sealed class SimulatedUpdatePackageVerifier : IUpdatePackageVerifier
{
    public async Task<bool> VerifyAsync(
        UpdatePackage package,
        string downloadedPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(downloadedPath);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
