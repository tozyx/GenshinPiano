using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.App.Services;

public sealed class UpdateInstallerService
{
    private static readonly string[] PreservedEntries = ["config", "songs", "logs", "update-cache"];

    public async Task<string> PrepareAsync(UpdateState state, CancellationToken cancellationToken = default)
    {
        if (state.Stage != UpdateStage.Ready || string.IsNullOrWhiteSpace(state.DownloadedPath) ||
            !File.Exists(state.DownloadedPath))
            throw new InvalidOperationException("The downloaded update package is unavailable.");

        var install = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var cache = Path.Combine(install, "update-cache");
        var token = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(cache, "staging", token);
        Directory.CreateDirectory(staging);
        await ExtractSafelyAsync(state.DownloadedPath, staging, cancellationToken);

        var sourceUpdater = Path.Combine(install, "GenshinPiano.Updater.exe");
        if (!File.Exists(sourceUpdater))
            throw new FileNotFoundException("GenshinPiano.Updater.exe is missing from the installation.", sourceUpdater);
        var runner = Path.Combine(cache, "updater", token, "GenshinPiano.Updater.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(runner)!);
        File.Copy(sourceUpdater, runner, true);

        var plan = new UpdateInstallationPlan(
            Environment.ProcessId, install, staging,
            Path.Combine(cache, "rollback", token), "GenshinPiano.exe",
            state.AvailableVersion?.ToString() ?? "unknown", PreservedEntries);
        var planPath = Path.Combine(cache, "plans", $"update-{token}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(planPath)!);
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), cancellationToken);
        return planPath;
    }

    public static void Launch(string planPath)
    {
        var install = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var token = Path.GetFileNameWithoutExtension(planPath)["update-".Length..];
        var updater = Path.Combine(install, "update-cache", "updater", token, "GenshinPiano.Updater.exe");
        Process.Start(new ProcessStartInfo(updater, $"--plan \"{planPath}\"")
        { UseShellExecute = true, WorkingDirectory = install });
    }

    private static async Task ExtractSafelyAsync(string zipPath, string destination, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe ZIP entry: {entry.FullName}");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
