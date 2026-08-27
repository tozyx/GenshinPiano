using System.Text.Json.Serialization;

namespace GenshinPiano.Updater;

internal sealed record UpdateCompletionMarker(string Version, string? ReleaseNotes);

internal sealed record UpdateInstallationPlan(
    int ProcessId,
    string InstallDirectory,
    string StagingDirectory,
    string RollbackDirectory,
    string ApplicationExecutable,
    string Version,
    string[] PreservedEntries,
    string? ReleaseNotes = null,
    bool RestartAfterInstall = true);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UpdateInstallationPlan))]
[JsonSerializable(typeof(UpdateCompletionMarker))]
internal partial class UpdaterJsonContext : JsonSerializerContext;
