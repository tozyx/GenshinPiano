using System.Diagnostics;
using System.Text.Json;
using GenshinPiano.Application.Updates;

if (args.Length != 2 || args[0] != "--plan") return 2;
var plan = JsonSerializer.Deserialize<UpdateInstallationPlan>(await File.ReadAllTextAsync(args[1]));
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
        Process.Start(new ProcessStartInfo(Path.Combine(plan.InstallDirectory, plan.ApplicationExecutable))
        { UseShellExecute = true, WorkingDirectory = plan.InstallDirectory });
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
