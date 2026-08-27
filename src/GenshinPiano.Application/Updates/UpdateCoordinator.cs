namespace GenshinPiano.Application.Updates;

public sealed class UpdateCoordinator(
    SemanticVersion currentVersion,
    IUpdateSource source,
    IUpdatePackageDownloader downloader,
    IUpdatePackageVerifier verifier)
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public SemanticVersion CurrentVersion => currentVersion;

    public UpdateState State { get; private set; } = UpdateState.Idle;

    public event EventHandler<UpdateState>? StateChanged;

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
            var manifest = await source.GetLatestAsync(channel, cancellationToken);
            if (manifest is null || manifest.Version.CompareTo(currentVersion) <= 0)
            {
                SetState(UpdateState.Idle);
                return;
            }

            var package = manifest.Packages.FirstOrDefault(candidate =>
                candidate.Kind == UpdatePackageKind.Application && !candidate.Optional);
            if (package is null)
            {
                SetState(new UpdateState(
                    UpdateStage.Failed,
                    AvailableVersion: manifest.Version,
                    SourceName: manifest.SourceName,
                    ErrorMessage: "The update manifest does not contain an application package."));
                return;
            }

            SetState(new UpdateState(
                UpdateStage.Available,
                AvailableVersion: manifest.Version,
                SourceName: manifest.SourceName));
            if (!automaticallyDownload)
            {
                return;
            }

            var progress = new InlineProgress(value => SetState(new UpdateState(
                UpdateStage.Downloading,
                Math.Clamp(value, 0, 1),
                manifest.Version,
                manifest.SourceName)));
            SetState(new UpdateState(
                UpdateStage.Downloading,
                AvailableVersion: manifest.Version,
                SourceName: manifest.SourceName));
            var downloadedPath = await downloader.DownloadAsync(package, progress, cancellationToken);
            SetState(new UpdateState(
                UpdateStage.Verifying,
                1,
                manifest.Version,
                manifest.SourceName));
            if (!await verifier.VerifyAsync(package, downloadedPath, cancellationToken))
            {
                throw new InvalidDataException("The downloaded update package failed SHA-256 verification.");
            }
            SetState(new UpdateState(
                UpdateStage.Ready,
                1,
                manifest.Version,
                manifest.SourceName,
                downloadedPath,
                ReleaseNotes: manifest.ReleaseNotes));
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
