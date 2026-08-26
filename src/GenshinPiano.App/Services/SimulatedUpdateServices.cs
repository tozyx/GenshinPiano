using System.IO;
using System.Text.Json;
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

        var package = new UpdatePackage(
            "app.win-x64.simulated",
            UpdatePackageKind.Application,
            version,
            document.FileName,
            Math.Max(1, document.Size),
            new string('0', 64),
            new Uri("https://localhost/" + Uri.EscapeDataString(document.FileName)));
        return new UpdateManifest(
            1,
            channel,
            version,
            DateTimeOffset.UtcNow,
            [package],
            "Local simulation");
    }

    private sealed record SimulationManifest
    {
        public string Version { get; init; } = "3.0.1-preview.1";

        public string FileName { get; init; } = "GenshinPiano-simulated.zip";

        public long Size { get; init; } = 10_000_000;
    }
}

public sealed class SimulatedUpdatePackageDownloader : IUpdatePackageDownloader
{
    public async Task<string> DownloadAsync(
        UpdatePackage package,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        const int steps = 40;
        for (var step = 1; step <= steps; step++)
        {
            await Task.Delay(40, cancellationToken);
            progress.Report(step / (double)steps);
        }

        return Path.Combine(AppContext.BaseDirectory, "update-cache", "simulated", package.FileName);
    }
}

public sealed class SimulatedUpdatePackageVerifier : IUpdatePackageVerifier
{
    public Task<bool> VerifyAsync(
        UpdatePackage package,
        string downloadedPath,
        CancellationToken cancellationToken) => Task.FromResult(true);
}
