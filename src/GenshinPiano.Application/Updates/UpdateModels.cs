namespace GenshinPiano.Application.Updates;

public enum UpdateStage
{
    Disabled,
    Idle,
    Checking,
    Available,
    Downloading,
    Verifying,
    Ready,
    Failed,
}

public enum UpdatePackageKind
{
    Application,
    OptionalComponent,
}

public sealed record UpdatePackage(
    string Id,
    UpdatePackageKind Kind,
    SemanticVersion Version,
    string FileName,
    long Size,
    string Sha256,
    Uri DownloadUri,
    bool Optional = false);

public sealed record UpdateManifest(
    int SchemaVersion,
    string Channel,
    SemanticVersion Version,
    DateTimeOffset PublishedAt,
    IReadOnlyList<UpdatePackage> Packages,
    string SourceName);

public sealed record UpdateState(
    UpdateStage Stage,
    double Progress = 0,
    SemanticVersion? AvailableVersion = null,
    string? SourceName = null,
    string? DownloadedPath = null,
    string? ErrorMessage = null)
{
    public static UpdateState Idle { get; } = new(UpdateStage.Idle);
}

public interface IUpdateSource
{
    Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken cancellationToken);
}

public interface IUpdatePackageDownloader
{
    Task<string> DownloadAsync(
        UpdatePackage package,
        IProgress<double> progress,
        CancellationToken cancellationToken);
}

public interface IUpdatePackageVerifier
{
    Task<bool> VerifyAsync(
        UpdatePackage package,
        string downloadedPath,
        CancellationToken cancellationToken);
}
