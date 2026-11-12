using GenshinPiano.Application.Updates;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class UpdateCoordinatorTests
{
    [Theory]
    [InlineData("v3.0.0", "3.0.0", 0)]
    [InlineData("3.0.1", "3.0.0", 1)]
    [InlineData("3.0.0-preview.2", "3.0.0-preview.10", -1)]
    [InlineData("3.0.0", "3.0.0-preview.10", 1)]
    public void SemanticVersion_ParsesAndOrdersVersions(
        string leftText,
        string rightText,
        int expectedSign)
    {
        Assert.True(SemanticVersion.TryParse(leftText, out var left));
        Assert.True(SemanticVersion.TryParse(rightText, out var right));
        Assert.Equal(expectedSign, Math.Sign(left.CompareTo(right)));
    }

    [Fact]
    public async Task CheckAsync_WhenNetworkIsDisabled_DoesNotCallSource()
    {
        var source = new StubSource(CreateManifest("3.1.0"));
        var downloader = new StubDownloader();
        var coordinator = CreateCoordinator(source, downloader);

        await coordinator.CheckAsync(false, true, "preview");

        Assert.Equal(UpdateStage.Disabled, coordinator.State.Stage);
        Assert.Equal(0, source.CallCount);
        Assert.Equal(0, downloader.CallCount);
    }

    [Fact]
    public async Task CheckAsync_WhenCurrentIsLatest_ReturnsToIdle()
    {
        var source = new StubSource(CreateManifest("3.0.0"));
        var downloader = new StubDownloader();
        var coordinator = CreateCoordinator(source, downloader);

        await coordinator.CheckAsync(true, true, "preview");

        Assert.Equal(UpdateStage.Idle, coordinator.State.Stage);
        Assert.Equal(0, downloader.CallCount);
    }

    [Fact]
    public async Task CheckAsync_WhenAutomaticDownloadIsOff_StopsAtAvailable()
    {
        var source = new StubSource(CreateManifest("3.1.0"));
        var downloader = new StubDownloader();
        var coordinator = CreateCoordinator(source, downloader);

        await coordinator.CheckAsync(true, false, "preview");

        Assert.Equal(UpdateStage.Available, coordinator.State.Stage);
        Assert.Equal(new SemanticVersion(3, 1, 0), coordinator.State.AvailableVersion);
        Assert.Equal(0, downloader.CallCount);
    }

    [Fact]
    public async Task CheckAsync_WhenAutomaticDownloadIsOn_ReachesReadyOnce()
    {
        var source = new StubSource(CreateManifest("3.1.0"));
        var downloader = new StubDownloader();
        var coordinator = CreateCoordinator(source, downloader);
        var stages = new List<UpdateStage>();
        coordinator.StateChanged += (_, state) => stages.Add(state.Stage);

        await coordinator.CheckAsync(true, true, "preview");

        Assert.Equal(UpdateStage.Ready, coordinator.State.Stage);
        Assert.Equal(1, coordinator.State.Progress);
        Assert.Equal("simulated.zip", coordinator.State.DownloadedPath);
        Assert.Equal(1, downloader.CallCount);
        Assert.Contains(UpdateStage.Checking, stages);
        Assert.Contains(UpdateStage.Downloading, stages);
        Assert.Contains(UpdateStage.Verifying, stages);
        Assert.Equal(UpdateStage.Ready, stages[^1]);
    }

    [Fact]
    public async Task CheckAsync_WhenSourceFails_ExposesFailedState()
    {
        var coordinator = CreateCoordinator(
            new StubSource(exception: new InvalidOperationException("offline")),
            new StubDownloader());

        await coordinator.CheckAsync(true, true, "preview");

        Assert.Equal(UpdateStage.Failed, coordinator.State.Stage);
        Assert.Contains("offline", coordinator.State.ErrorMessage);
    }

    [Fact]
    public async Task RacingSource_UsesWorkingMirrorWhenFirstMirrorFails()
    {
        var expected = CreateManifest("3.1.0");
        var source = new RacingUpdateSource(
        [
            new StubSource(exception: new HttpRequestException("blocked")),
            new StubSource(expected),
        ]);

        var manifest = await source.GetLatestAsync("preview", CancellationToken.None);

        Assert.Same(expected, manifest);
    }

    [Fact]
    public async Task RacingSource_SelectsHigherVersionThatArrivesInsideGraceWindow()
    {
        var slowerNewer = CreateManifest("3.2.0");
        var fasterOlder = CreateManifest("3.1.0");
        var source = new RacingUpdateSource(
        [
            new DelayedStubSource(slowerNewer, TimeSpan.FromMilliseconds(250)),
            new DelayedStubSource(fasterOlder, TimeSpan.FromMilliseconds(10)),
        ]);

        var manifest = await source.GetLatestAsync("preview", CancellationToken.None);

        Assert.Same(slowerNewer, manifest);
    }

    [Fact]
    public async Task RacingSource_WaitsForNewerManifestBeyondLegacyGraceWindow()
    {
        var slowerNewer = CreateManifest("3.2.0");
        var fasterOlder = CreateManifest("3.1.0");
        var source = new RacingUpdateSource(
        [
            new DelayedStubSource(slowerNewer, TimeSpan.FromMilliseconds(200)),
            new DelayedStubSource(fasterOlder, TimeSpan.FromMilliseconds(5)),
        ], gracePeriod: TimeSpan.FromMilliseconds(25));

        var manifest = await source.GetLatestAsync("preview", CancellationToken.None);

        Assert.Same(slowerNewer, manifest);
    }

    [Fact]
    public async Task RacingSource_MergesEquivalentDownloadMirrorsForSameVersion()
    {
        var github = CreateManifest("3.2.0");
        var gitCode = github with
        {
            Packages =
            [
                github.Packages[0] with
                {
                    DownloadUri = new Uri("https://gitcode.example/app.zip"),
                },
            ],
        };
        var source = new RacingUpdateSource(
        [
            new DelayedStubSource(gitCode, TimeSpan.FromMilliseconds(5)),
            new DelayedStubSource(github, TimeSpan.FromMilliseconds(30)),
        ], gracePeriod: TimeSpan.FromMilliseconds(1));

        var manifest = await source.GetLatestAsync("preview", CancellationToken.None);

        Assert.NotNull(manifest);
        var package = Assert.Single(manifest.Packages);
        Assert.Equal(2, package.GetDownloadUris().Count);
        Assert.Contains(package.GetDownloadUris(), uri => uri.Host == "gitcode.example");
        Assert.Contains(package.GetDownloadUris(), uri => uri.Host == "example.invalid");
    }

    [Fact]
    public async Task ManualCheckAndDownloadReuseActiveAutomaticDownload()
    {
        var path = Path.GetTempFileName();
        try
        {
            var manifest = CreateManifest("3.1.0");
            var source = new StubSource(manifest);
            var downloader = new BlockingDownloader(path);
            var coordinator = CreateCoordinator(source, downloader);
            var automatic = coordinator.CheckAsync(true, true, "preview");
            await downloader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var discovered = await coordinator.FindUpdateAsync("preview");
            coordinator.ObserveCheck(discovered);
            Assert.Equal(UpdateStage.Downloading, coordinator.State.Stage);
            var manual = coordinator.DownloadAsync(discovered!);
            Assert.False(manual.IsCompleted);
            Assert.Equal(1, downloader.CallCount);

            downloader.Release.SetResult();
            await Task.WhenAll(automatic, manual).WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.DownloadAsync(manifest);
            Assert.Equal(1, downloader.CallCount);
            Assert.Equal(2, source.CallCount);
            Assert.Equal(UpdateStage.Ready, coordinator.State.Stage);
            Assert.Equal(path, coordinator.State.DownloadedPath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CancellingManualWaitLeavesAutomaticDownloadRunning()
    {
        var path = Path.GetTempFileName();
        try
        {
            var manifest = CreateManifest("3.1.0");
            var downloader = new BlockingDownloader(path);
            var coordinator = CreateCoordinator(new StubSource(manifest), downloader);
            var automatic = coordinator.CheckAsync(true, true, "preview");
            await downloader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            var manual = coordinator.DownloadAsync(manifest, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manual);
            Assert.Equal(UpdateStage.Downloading, coordinator.State.Stage);
            Assert.False(automatic.IsCompleted);
            downloader.Release.SetResult();
            await automatic.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(UpdateStage.Ready, coordinator.State.Stage);
            Assert.Equal(1, downloader.CallCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DownloadingAnOlderSelectionDoesNotReplaceNewerPreparedPackage()
    {
        var path = Path.GetTempFileName();
        try
        {
            var downloader = new BlockingDownloader(path);
            downloader.Release.SetResult();
            var coordinator = CreateCoordinator(new StubSource(CreateManifest("3.2.0")), downloader);
            await coordinator.CheckAsync(true, true, "preview");
            await coordinator.DownloadAsync(CreateManifest("3.1.0"));
            Assert.Equal(new SemanticVersion(3, 2, 0), coordinator.State.AvailableVersion);
            Assert.Equal(1, downloader.CallCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ReadOnlyCheckDoesNotStartDownload()
    {
        var downloader = new StubDownloader();
        var coordinator = CreateCoordinator(new StubSource(CreateManifest("3.1.0")), downloader);
        var manifest = await coordinator.FindUpdateAsync("preview");
        Assert.NotNull(manifest);
        Assert.Equal(UpdateStage.Idle, coordinator.State.Stage);
        Assert.Equal(0, downloader.CallCount);
    }

    [Fact]
    public async Task InspectionReportsCurrentReleaseWithoutChangingAutomaticDiscovery()
    {
        var downloader = new StubDownloader();
        var coordinator = new UpdateCoordinator(new(3,0,0),new StubSource(CreateManifest("3.2.0")),
            downloader,new StubVerifier(),new StubSource(CreateManifest("3.0.0")));
        var latest = await coordinator.InspectLatestAsync("stable");
        Assert.Equal(new SemanticVersion(3,0,0),latest!.Version);
        coordinator.ObserveCheck(latest);
        Assert.Equal(UpdateStage.Idle,coordinator.State.Stage);
        Assert.Equal(0,downloader.CallCount);
        await coordinator.CheckAsync(true,false,"stable");
        Assert.Equal(new SemanticVersion(3,2,0),coordinator.State.AvailableVersion);
    }

    private sealed class BlockingDownloader(string path) : IUpdatePackageDownloader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public async Task<string> DownloadAsync(UpdatePackage package, IProgress<double> progress, CancellationToken cancellationToken)
        {
            CallCount++;
            progress.Report(0.25);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return path;
        }
    }

    private static UpdateCoordinator CreateCoordinator(
        IUpdateSource source,
        IUpdatePackageDownloader downloader) =>
        new(new SemanticVersion(3, 0, 0), source, downloader, new StubVerifier());

    private static UpdateManifest CreateManifest(string versionText)
    {
        Assert.True(SemanticVersion.TryParse(versionText, out var version));
        return new UpdateManifest(
            1,
            "preview",
            version,
            DateTimeOffset.UtcNow,
            [new UpdatePackage(
                "app.win-x64",
                UpdatePackageKind.Application,
                version,
                "app.zip",
                100,
                new string('0', 64),
                new Uri("https://example.invalid/app.zip"))],
            "test");
    }

    private sealed class StubSource(
        UpdateManifest? manifest = null,
        Exception? exception = null) : IUpdateSource
    {
        public int CallCount { get; private set; }

        public Task<UpdateManifest?> GetLatestAsync(
            string channel,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return exception is null
                ? Task.FromResult(manifest)
                : Task.FromException<UpdateManifest?>(exception);
        }
    }

    private sealed class DelayedStubSource(
        UpdateManifest manifest,
        TimeSpan delay) : IUpdateSource
    {
        public async Task<UpdateManifest?> GetLatestAsync(
            string channel,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return manifest;
        }
    }

    private sealed class StubDownloader : IUpdatePackageDownloader
    {
        public int CallCount { get; private set; }

        public Task<string> DownloadAsync(
            UpdatePackage package,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            progress.Report(0.5);
            progress.Report(1);
            return Task.FromResult("simulated.zip");
        }
    }

    private sealed class StubVerifier : IUpdatePackageVerifier
    {
        public Task<bool> VerifyAsync(
            UpdatePackage package,
            string downloadedPath,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
