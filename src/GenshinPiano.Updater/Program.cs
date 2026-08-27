using System.Diagnostics;
using System.Text.Json;
using GenshinPiano.Updater;

if (args.Length != 2 || args[0] != "--plan") return 2;
UpdateInstallationPlan? plan;
try
{
    plan = JsonSerializer.Deserialize(
        await File.ReadAllTextAsync(args[1]),
        UpdaterJsonContext.Default.UpdateInstallationPlan);
}
catch (Exception exception)
{
    var fallbackLog = Path.Combine(Path.GetDirectoryName(args[1])!, "updater-bootstrap.log");
    await File.AppendAllTextAsync(fallbackLog, $"{DateTimeOffset.Now:u} PLAN FAILED: {exception}{Environment.NewLine}");
    return 3;
}
if (plan is null) return 3;
var log = Path.Combine(plan.InstallDirectory, "update-cache", "updater.log");
try
{
    try { Process.GetProcessById(plan.ProcessId).WaitForExit(60000); } catch (ArgumentException) { }
    Directory.CreateDirectory(plan.RollbackDirectory);
    var preserved = new HashSet<string>(plan.PreservedEntries, StringComparer.OrdinalIgnoreCase);
    var backupComplete = false;
    try
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(plan.InstallDirectory).ToArray())
        {
            var name = Path.GetFileName(entry);
            if (preserved.Contains(name)) continue;
            Move(entry, Path.Combine(plan.RollbackDirectory, name));
        }
        backupComplete = true;
        foreach (var entry in Directory.EnumerateFileSystemEntries(plan.StagingDirectory))
        {
            var name = Path.GetFileName(entry);
            if (preserved.Contains(name)) continue;
            var target = Path.Combine(plan.InstallDirectory, name);
            Move(entry, target);
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
        if (backupComplete)
            foreach (var entry in Directory.EnumerateFileSystemEntries(plan.InstallDirectory).Where(p => !preserved.Contains(Path.GetFileName(p))).ToArray())
                if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry);
        foreach (var entry in Directory.EnumerateFileSystemEntries(plan.RollbackDirectory))
            Move(entry, Path.Combine(plan.InstallDirectory, Path.GetFileName(entry)));
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

static void Move(string source, string destination)
{
    if (Directory.Exists(source)) Directory.Move(source, destination);
    else File.Move(source, destination);
}

static void PurgeDownloadedPackages(string installDirectory)
{
    var downloads = Path.Combine(installDirectory, "update-cache", "downloads");
    if (!Directory.Exists(downloads)) return;

    foreach (var path in Directory.EnumerateFiles(downloads))
    {
        File.Delete(path);
    }
}

static void DeleteIfExists(string path)
{
    if (File.Exists(path)) File.Delete(path);
}
