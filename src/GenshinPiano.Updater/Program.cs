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
return await UpdateRunner.RunAsync(plan);
