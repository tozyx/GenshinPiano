namespace GenshinPiano.Application.Updates;

public sealed record UpdateInstallationPlan(
    int ProcessId,
    string InstallDirectory,
    string StagingDirectory,
    string RollbackDirectory,
    string ApplicationExecutable,
    string Version,
    string[] PreservedEntries,
    string? ReleaseNotes = null,
    bool RestartAfterInstall = true);
