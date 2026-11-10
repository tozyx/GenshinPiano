using GenshinPiano.Updater;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class UpdateRunnerTests : IDisposable
{
    private const string AppFile = "GenshinPiano.exe";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("gpiano-updater-");
    private string Install => Path.Combine(_root.FullName, "install");
    private string Cache => Path.Combine(Install, "update-cache");
    private string Staging => Path.Combine(Cache, "staging");
    private string Backup => Path.Combine(Cache, "rollback");

    [Fact]
    public async Task RunningApplication_AbortsWithoutMovingFiles()
    {
        var plan = ArrangeUpdate() with { ProcessId = Environment.ProcessId };

        Assert.Equal(1, await UpdateRunner.RunAsync(plan, exitTimeoutMilliseconds: 0));

        Assert.Equal("old", File.ReadAllText(Path.Combine(Install, AppFile)));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Staging, AppFile)));
        Assert.False(Directory.Exists(Backup));
        Assert.Contains("did not exit", File.ReadAllText(Path.Combine(Cache, "updater.log")));
    }

    [Fact]
    public async Task UpdateAndRollback_PreserveUserFilesAndRestoreApplication()
    {
        var plan = ArrangeUpdate();

        Assert.Equal(0, await UpdateRunner.RunAsync(plan));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Install, AppFile)));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Backup, AppFile)));
        Assert.True(File.Exists(Path.Combine(Cache, "update-completed.json")));

        Assert.Equal(0, await UpdateRunner.RunAsync(plan with
        {
            StagingDirectory = Backup,
            RollbackDirectory = Path.Combine(Cache, "backup-of-new"),
            Version = "rollback",
        }));

        Assert.Equal("old", File.ReadAllText(Path.Combine(Install, AppFile)));
        Assert.Equal("user recovery", File.ReadAllText(Path.Combine(Install, "data", "recovery.txt")));
        Assert.False(File.Exists(Path.Combine(Cache, "update-completed.json")));
        Assert.True(File.Exists(Path.Combine(Cache, "force-remote-refresh")));
    }

    [Fact]
    public async Task FailedMove_RestoresOriginalInstallationAndKeepsStaging()
    {
        var plan = ArrangeUpdate();
        using var locked = File.Open(Path.Combine(Staging, AppFile), FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.Equal(1, await UpdateRunner.RunAsync(plan));

        Assert.Equal("old", File.ReadAllText(Path.Combine(Install, AppFile)));
        Assert.True(File.Exists(Path.Combine(Staging, AppFile)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Backup));
        Assert.False(File.Exists(Path.Combine(Cache, "update-completed.json")));
    }

    private UpdateInstallationPlan ArrangeUpdate()
    {
        Directory.CreateDirectory(Staging);
        Directory.CreateDirectory(Path.Combine(Install, "data"));
        File.WriteAllText(Path.Combine(Install, AppFile), "old");
        File.WriteAllText(Path.Combine(Staging, AppFile), "new");
        File.WriteAllText(Path.Combine(Install, "data", "recovery.txt"), "user recovery");
        return new(int.MaxValue, Install, Staging, Backup, AppFile, "3.0.8",
            ["update-cache", "data"], RestartAfterInstall: false);
    }

    public void Dispose() => _root.Delete(recursive: true);
}
