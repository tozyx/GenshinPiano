namespace GenshinPiano.Application.Updates;

public sealed class UpdateCoordinator(
    SemanticVersion currentVersion,
    IUpdateSource source,
    IUpdatePackageDownloader downloader,
    IUpdatePackageVerifier verifier,
    IUpdateSource? inspectionSource = null)
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private UpdatePackage? _preparedPackage;

    public SemanticVersion CurrentVersion => currentVersion;

    public UpdateState State { get; private set; } = UpdateState.Idle;

    public event EventHandler<UpdateState>? StateChanged;

    public async Task<UpdateManifest?> InspectLatestAsync(string channel, CancellationToken cancellationToken = default)
    {
        var manifest = await (inspectionSource ?? source).GetLatestAsync(channel, cancellationToken);
        if (manifest is not null) _ = ApplicationPackage(manifest);
        return manifest;
    }

    /// <summary>Reads metadata without interrupting an active background download.</summary>
    public async Task<UpdateManifest?> FindUpdateAsync(string channel, CancellationToken cancellationToken = default)
    {
        var manifest = await source.GetLatestAsync(channel, cancellationToken);
        if (manifest is null || manifest.Version.CompareTo(currentVersion) <= 0) return null;
        _ = ApplicationPackage(manifest);
        return manifest;
    }

    public void ObserveCheck(UpdateManifest? manifest)
    {
        if (State.Stage is not (UpdateStage.Idle or UpdateStage.Available or UpdateStage.Failed)) return;
        SetState(manifest is null || manifest.Version.CompareTo(currentVersion) <= 0 ? UpdateState.Idle : new UpdateState(
            UpdateStage.Available, AvailableVersion: manifest.Version, SourceName: manifest.SourceName));
    }

    /// <summary>Waits for an existing operation and reuses its verified package when possible.</summary>
    public async Task DownloadAsync(UpdateManifest manifest, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (manifest.Version.CompareTo(currentVersion) <= 0) return;
            var package = ApplicationPackage(manifest);
            if (State.Stage == UpdateStage.Ready && File.Exists(State.DownloadedPath) && _preparedPackage is not null &&
                (_preparedPackage.Version.CompareTo(package.Version) > 0 ||
                 (_preparedPackage.Version.CompareTo(package.Version) == 0 &&
                  string.Equals(_preparedPackage.Sha256, package.Sha256, StringComparison.OrdinalIgnoreCase) &&
                  _preparedPackage.Signature == package.Signature))) return;
            await DownloadCoreAsync(manifest, package, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(UpdateState.Idle);
            throw;
        }
        catch (Exception exception)
        {
            SetState(new UpdateState(UpdateStage.Failed, ErrorMessage: exception.Message));
        }
        finally { _operationLock.Release(); }
    }

    public async Task CheckAsync(
        bool networkAllowed,
        bool automaticallyDownload,
        string channel,
        CancellationToken cancellationToken = default)
    {
        if (!networkAllowed)
        {
            SetState(new UpdateState(UpdateStage.Disabled));
            return;
        }

        await _operationLock.WaitAsync(cancellationToken);

        try
        {
            SetState(new UpdateState(UpdateStage.Checking));
            var manifest = await FindUpdateAsync(channel, cancellationToken);
            if (manifest is null)
            {
                SetState(UpdateState.Idle);
                return;
            }

            var package = ApplicationPackage(manifest);

            SetState(new UpdateState(
                UpdateStage.Available,
                AvailableVersion: manifest.Version,
                SourceName: manifest.SourceName));
            if (!automaticallyDownload)
            {
                return;
            }

            await DownloadCoreAsync(manifest, package, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(UpdateState.Idle);
            throw;
        }
        catch (Exception exception)
        {
            SetState(new UpdateState(UpdateStage.Failed, ErrorMessage: exception.Message));
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Disable() => SetState(new UpdateState(UpdateStage.Disabled));

    public void Reset() => SetState(UpdateState.Idle);

    private static UpdatePackage ApplicationPackage(UpdateManifest manifest) =>
        manifest.Packages.FirstOrDefault(candidate => candidate.Kind == UpdatePackageKind.Application && !candidate.Optional)
        ?? throw new InvalidDataException("The update manifest does not contain an application package.");

    private async Task DownloadCoreAsync(UpdateManifest manifest, UpdatePackage package, CancellationToken cancellationToken)
    {
        var progress = new InlineProgress(value => SetState(new UpdateState(
            UpdateStage.Downloading, Math.Clamp(value, 0, 1), manifest.Version, manifest.SourceName)));
        SetState(new UpdateState(UpdateStage.Downloading, AvailableVersion: manifest.Version, SourceName: manifest.SourceName));
        var path = await downloader.DownloadAsync(package, progress, cancellationToken);
        SetState(new UpdateState(UpdateStage.Verifying, 1, manifest.Version, manifest.SourceName));
        if (!await verifier.VerifyAsync(package, path, cancellationToken))
            throw new InvalidDataException("The downloaded update package failed integrity or signature verification.");
        _preparedPackage = package;
        SetState(new UpdateState(UpdateStage.Ready, 1, manifest.Version, manifest.SourceName, path, ReleaseNotes: manifest.ReleaseNotes));
    }

    private void SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
