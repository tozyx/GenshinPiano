using System.Diagnostics;
using System.Text.Json;

namespace GenshinPiano.Updater;

internal static class UpdateRunner
{
    internal static async Task<int> RunAsync(UpdateInstallationPlan plan, int exitTimeoutMilliseconds = 60_000)
    {
        var log = Path.Combine(plan.InstallDirectory, "update-cache", "updater.log");
        try
        {
            Process? app;
            try { app = Process.GetProcessById(plan.ProcessId); }
            catch (ArgumentException) { app = null; }
            using (app)
            {
                if (app is not null && !app.WaitForExit(exitTimeoutMilliseconds))
                    throw new TimeoutException($"Application process {plan.ProcessId} did not exit; update cancelled.");
            }
            if (!File.Exists(Path.Combine(plan.StagingDirectory, plan.ApplicationExecutable)))
                throw new FileNotFoundException("The staged application executable is missing.");

            Directory.CreateDirectory(plan.RollbackDirectory);
            var preserved = new HashSet<string>(plan.PreservedEntries, StringComparer.OrdinalIgnoreCase);
            var moves = new Stack<(string Source, string Destination)>();
            void MoveAndRecord(string source, string destination)
            {
                Move(source, destination);
                moves.Push((source, destination));
            }
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(plan.InstallDirectory).ToArray())
                {
                    var name = Path.GetFileName(entry);
                    if (preserved.Contains(name)) continue;
                    MoveAndRecord(entry, Path.Combine(plan.RollbackDirectory, name));
                }
                foreach (var entry in Directory.EnumerateFileSystemEntries(plan.StagingDirectory).ToArray())
                {
                    var name = Path.GetFileName(entry);
                    if (preserved.Contains(name)) continue;
                    var target = Path.Combine(plan.InstallDirectory, name);
                    MoveAndRecord(entry, target);
                }
                var completedPath = Path.Combine(plan.InstallDirectory, "update-cache", "update-completed.json");
                var latestReleaseNotesPath = Path.Combine(
                    plan.InstallDirectory,
                    "update-cache",
                    "latest-release-notes.json");
                var forceRemoteRefreshPath = Path.Combine(
                    plan.InstallDirectory,
                    "update-cache",
                    "force-remote-refresh");
                if (!string.Equals(plan.Version, "rollback", StringComparison.OrdinalIgnoreCase))
                {
                    var marker = new UpdateCompletionMarker(plan.Version, plan.ReleaseNotes);
                    await File.WriteAllTextAsync(
                        completedPath,
                        JsonSerializer.Serialize(marker, UpdaterJsonContext.Default.UpdateCompletionMarker));
                    await File.WriteAllTextAsync(
                        latestReleaseNotesPath,
                        JsonSerializer.Serialize(marker, UpdaterJsonContext.Default.UpdateCompletionMarker));
                    DeleteIfExists(forceRemoteRefreshPath);
                }
                else
                {
                    DeleteIfExists(completedPath);
                    DeleteIfExists(latestReleaseNotesPath);

                    // A rollback restores the previous application executable as well. Do not let that
                    // older build reuse a package downloaded by the version that was just rolled back.
                    // The next update must obtain the release package again from the configured remote.
                    PurgeDownloadedPackages(plan.InstallDirectory);
                    await File.WriteAllTextAsync(
                        forceRemoteRefreshPath,
                        $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}");
                }
                if (plan.RestartAfterInstall)
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(plan.InstallDirectory, plan.ApplicationExecutable))
                    { UseShellExecute = true, WorkingDirectory = plan.InstallDirectory });
                }
            }
            catch
            {
                while (moves.TryPop(out var move))
                    Move(move.Destination, move.Source);
                throw;
            }
            await File.AppendAllTextAsync(log, $"{DateTimeOffset.Now:u} Installed {plan.Version}{Environment.NewLine}");
            return 0;
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            await File.AppendAllTextAsync(log, $"{DateTimeOffset.Now:u} FAILED: {ex}{Environment.NewLine}");
            return 1;
        }
    }

    private static void Move(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
    }

    private static void PurgeDownloadedPackages(string installDirectory)
    {
        var downloads = Path.Combine(installDirectory, "update-cache", "downloads");
        if (!Directory.Exists(downloads)) return;

        foreach (var path in Directory.EnumerateFiles(downloads))
        {
            File.Delete(path);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
