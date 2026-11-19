using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Application.Updates;
using GenshinPiano.Infrastructure.Ocr;
using GenshinPiano.Infrastructure.Updates;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class OcrUpdateMetadataTests
{
    [Theory]
    [InlineData(null, true, false, true)]
    [InlineData("1.0.0", true, false, true)]
    [InlineData("1.0.1", true, true, false)]
    [InlineData("2.0.0", true, true, false)]
    [InlineData("1.0.1", false, false, true)]
    public async Task RepeatedChecksOnlyReadReleaseLists_RegardlessOfInstalledVersion(
        string? installed, bool resources, bool current, bool canUpdate)
    {
        using var server = new Server();
        var source = new RacingUpdateSource([server.Source("github.test"), server.Source("gitcode.test")]);
        var addon = new MutableAddon(installed, resources);
        var manager = new OcrAddonPackageManager(source, new Downloader(), new RejectVerifier(), addon, "unused",
            new OcrPackageMetadataResolver(server.Client));
        for (var index = 0; index < 3; index++)
        {
            var check = await manager.CheckForUpdatesAsync();
            Assert.Equal("1.0.1", check.Package!.Version.ToString());
            Assert.Equal(current, check.IsCurrent);
            Assert.Equal(canUpdate, check.CanUpdate);
            Assert.Equal(2, check.Package.VerificationSources!.Count);
            Assert.Null(check.Package.MirrorDownloadUris); // Still unverified candidates.
        }
        Assert.Equal(6, server.Requests.Count);
        Assert.All(server.Requests, uri => Assert.Equal("/releases", uri.AbsolutePath));
    }

    [Fact]
    public async Task ManualInstallationAfterCheckSkipsVerificationAndDownload()
    {
        using var server = new Server();
        var addon = new MutableAddon("1.0.0");
        var downloader = new Downloader();
        var manager = new OcrAddonPackageManager(server.Source(), downloader, new RejectVerifier(), addon, "unused",
            new OcrPackageMetadataResolver(server.Client));
        var check = await manager.CheckForUpdatesAsync();
        addon.Version = "1.0.1";
        Assert.True(manager.LastCheck!.IsCurrent);
        Assert.False((await manager.InstallCheckedUpdateAsync(check, new Progress<double>())).Updated);
        Assert.Equal(0, downloader.Calls);
        Assert.Single(server.Requests);

        addon.Version = null; // Removing a manual installation must make the cached choice usable again.
        Assert.True(manager.LastCheck!.CanUpdate);
        await Assert.ThrowsAsync<IOException>(() => manager.InstallCheckedUpdateAsync(check, new Progress<double>()));
        Assert.Equal(2, server.SidecarRequests);
        Assert.NotNull(downloader.Package!.Signature);
        Assert.Null(downloader.Package.VerificationSources);
    }

    [Fact]
    public async Task SuccessfulMetadataIsSharedAcrossConcurrentRetries_ThenExpires()
    {
        using var server = new Server();
        var clock = new Clock();
        var resolver = new OcrPackageMetadataResolver(server.Client, clock);
        var package = Assert.Single((await server.Source().GetLatestAsync("stable", default))!.Packages);
        var resolved = await Task.WhenAll(resolver.ResolveAsync(package, default), resolver.ResolveAsync(package, default));
        Assert.All(resolved, item => Assert.Equal(server.Hash.ToUpperInvariant(), item.Sha256));
        Assert.Equal(2, server.SidecarRequests);
        clock.Now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
        await resolver.ResolveAsync(package, default);
        Assert.Equal(4, server.SidecarRequests);
    }

    [Fact]
    public async Task ReplacedAssetsInvalidateCache_EvenWhenVersionAndUrlsStayTheSame()
    {
        using var server = new Server();
        var resolver = new OcrPackageMetadataResolver(server.Client);
        var source = server.Source();
        async Task Resolve() => await resolver.ResolveAsync(Assert.Single((await source.GetLatestAsync("stable", default))!.Packages), default);
        await Resolve();
        server.DownloadCount++;
        await Resolve();
        Assert.Equal(2, server.SidecarRequests);
        server.Revision++;
        server.Hash = new string('b', 64);
        await Resolve();
        Assert.Equal(4, server.SidecarRequests);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("signature")]
    [InlineData("empty-signature")]
    public async Task InvalidVerificationIsNotCached_AndRetryCanRecover(string invalid)
    {
        using var server = new Server();
        var resolver = new OcrPackageMetadataResolver(server.Client);
        var package = Assert.Single((await server.Source().GetLatestAsync("stable", default))!.Packages);
        if (invalid == "checksum") server.Hash = "invalid";
        else server.Signature = invalid == "signature" ? "invalid!" : "";
        await Assert.ThrowsAsync<AggregateException>(() => resolver.ResolveAsync(package, default));
        server.Hash = new string('a', 64);
        server.Signature = Convert.ToBase64String(new byte[384]);
        var resolved = await resolver.ResolveAsync(package, default);
        Assert.Null(resolved.VerificationSources);
        Assert.Equal(invalid == "checksum" ? 3 : 4, server.SidecarRequests);
    }

    [Fact]
    public async Task CancelledVerificationDoesNotPoisonRetryCache()
    {
        using var server = new Server { BlockSignature = true };
        var resolver = new OcrPackageMetadataResolver(server.Client);
        var package = Assert.Single((await server.Source().GetLatestAsync("stable", default))!.Packages);
        using var cancellation = new CancellationTokenSource();
        var resolving = resolver.ResolveAsync(package, cancellation.Token);
        await server.SignatureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
        server.BlockSignature = false;
        await resolver.ResolveAsync(package, default);
        Assert.Equal(4, server.SidecarRequests);
    }

    [Theory]
    [InlineData("equivalent", 2)]
    [InlineData("different-hash", 1)]
    [InlineData("different-signature", 1)]
    [InlineData("unavailable", 1)]
    public async Task MirrorsOnlyRaceAfterVerificationMetadataMatches(string scenario, int mirrorCount)
    {
        using var server = new Server();
        var source = new RacingUpdateSource([server.Source("first.test"), server.Source("second.test")]);
        var package = Assert.Single((await source.GetLatestAsync("stable", default))!.Packages);
        Assert.Equal(0, server.SidecarRequests);
        if (scenario == "unavailable") server.FailedHost = package.DownloadUri.Host;
        else if (scenario != "equivalent")
        {
            server.ChangedHost = package.VerificationSources!.First(item => item.DownloadUri != package.DownloadUri).DownloadUri.Host;
            server.ChangeSignature = scenario == "different-signature";
        }
        var resolved = await new OcrPackageMetadataResolver(server.Client).ResolveAsync(package, default);
        Assert.Equal(mirrorCount, resolved.GetDownloadUris().Count);
        Assert.Null(resolved.VerificationSources);
        if (scenario == "unavailable") Assert.NotEqual(server.FailedHost, resolved.DownloadUri.Host);
    }

    [Fact]
    public async Task DownloaderRejectsUnresolvedPackageWithoutAnyNetworkRequest()
    {
        using var server = new Server();
        var package = Assert.Single((await server.Source().GetLatestAsync("stable", default))!.Packages);
        var downloader = new ResumableUpdatePackageDownloader(server.Client, "unused");
        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(package, new Progress<double>(), default));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task ApiRateLimitFallbackAlsoDefersVerificationFiles()
    {
        using var server = new Server();
        var source = new OcrAddonReleaseSource(server.Client, "GitHub",
            new Uri("https://api.github.com/repos/test/project/releases"), resourcesOnly: true, deferVerification: true);
        var package = Assert.Single((await source.GetLatestAsync("stable", default))!.Packages);
        Assert.Equal("1.0.1", package.Version.ToString());
        Assert.Equal(3, server.Requests.Count); // API, feed, expanded assets.
        Assert.Equal(0, server.SidecarRequests);
        await new OcrPackageMetadataResolver(server.Client).ResolveAsync(package, default);
        Assert.Equal(2, server.SidecarRequests);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("signature")]
    [InlineData("checksum")]
    public async Task InstallationStillRequiresRealChecksumAndRsaSignature(string verification)
    {
        var root = Path.Combine(Path.GetTempPath(), "GPiano-OcrMetadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "addons", "ocr");
            WriteResources(target, "1.0.0");
            var staging = Path.Combine(root, "source");
            WriteResources(Path.Combine(staging, "addons", "ocr"), "1.0.1");
            var zip = Path.Combine(root, "ocr.zip");
            ZipFile.CreateFromDirectory(staging, zip);
            var worker = Path.Combine(root, "GenshinPiano.exe");
            File.WriteAllBytes(worker, [1]);
            using var key = RSA.Create(2048);
            using var server = new Server();
            server.Hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zip)));
            if (verification == "checksum") server.Hash = new string('B', 64);
            var canonical = Encoding.UTF8.GetBytes($"GenshinPiano.Update.v1\nocr-resources-1.0.1-win-x64.zip\n{server.Hash}\n");
            server.Signature = Convert.ToBase64String(verification != "signature"
                ? key.SignData(canonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) : new byte[256]);
            var service = new ExternalOcrAddonService(target, workerExecutablePath: worker);
            var downloader = new Downloader(zip);
            var manager = new OcrAddonPackageManager(server.Source(), downloader,
                new SignedUpdatePackageVerifier(key.ToXmlString(false)), service, root,
                new OcrPackageMetadataResolver(server.Client));
            var check = await manager.CheckForUpdatesAsync();
            Assert.True(check.CanUpdate);
            Assert.Equal(0, server.SidecarRequests);
            if (verification == "valid")
            {
                Assert.True((await manager.InstallCheckedUpdateAsync(check, new Progress<double>())).Updated);
                Assert.True(manager.LastCheck!.IsCurrent);
                Assert.Same(check.Package, manager.LastCheck.Package); // Keep future retries subject to metadata cache expiry.
                Assert.False((await manager.InstallCheckedUpdateAsync(check, new Progress<double>())).Updated);
            }
            else
                await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallCheckedUpdateAsync(check, new Progress<double>()));
            Assert.Equal(2, server.SidecarRequests);
            Assert.Equal(1, downloader.Calls);
            Assert.Equal(verification == "valid" ? "1.0.1" : "1.0.0", service.FindInstalledAddon()!.EngineVersion);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void WriteResources(string path, string version)
    {
        foreach (var name in OcrAddonPackageValidator.RequiredResources)
        {
            var file = Path.Combine(path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, [1]);
        }
        File.WriteAllText(Path.Combine(path, "manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 2, protocolVersion = 1, resourceVersion = version, engineCompatibilityVersion = 1,
            staffBackend = "smt", smtModelSha256 = new string('a', 64), requiredFiles = OcrAddonPackageValidator.RequiredResources,
        }));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Downloader(string? zip = null) : IUpdatePackageDownloader
    {
        public int Calls { get; private set; }
        public UpdatePackage? Package { get; private set; }
        public Task<string> DownloadAsync(UpdatePackage package, IProgress<double> progress, CancellationToken cancellationToken)
        {
            Package = package;
            Calls++;
            return zip is null ? throw new IOException("Simulated interrupted ZIP download.") : Task.FromResult(zip);
        }
    }

    private sealed class RejectVerifier : IUpdatePackageVerifier
    {
        public Task<bool> VerifyAsync(UpdatePackage package, string path, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class MutableAddon(string? version, bool resources = true) : IOcrAddonService
    {
        public string? Version { get; set; } = version;
        public OcrAddonDescriptor? FindInstalledAddon() => Version is { } installed
            ? new(installed, 1, "unused.exe", IsResourcePackage: resources) : null;
        public Task<OcrAnalysisResult> AnalyzeAsync(string path, OcrNotationHint hint, string language,
            OcrWatermarkMode watermarkMode = OcrWatermarkMode.Auto, bool includeAccompaniment = true,
            IProgress<OcrProgressUpdate>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Server : HttpMessageHandler
    {
        public HttpClient Client { get; }
        public ConcurrentQueue<Uri> Requests { get; } = new();
        public int SidecarRequests => Requests.Count(uri => uri.AbsolutePath.EndsWith(".sha256") || uri.AbsolutePath.EndsWith(".sig"));
        public string Hash { get; set; } = new('a', 64);
        public string Signature { get; set; } = Convert.ToBase64String(new byte[384]);
        public int Revision { get; set; } = 1;
        public int DownloadCount { get; set; }
        public string? FailedHost { get; set; }
        public string? ChangedHost { get; set; }
        public bool ChangeSignature { get; set; }
        public bool BlockSignature { get; set; }
        public TaskCompletionSource SignatureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Server() => Client = new HttpClient(this, disposeHandler: false);
        public OcrAddonReleaseSource Source(string host = "github.test") => new(Client, host,
            new Uri($"https://{host}/releases"), resourcesOnly: true, deferVerification: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Enqueue(uri);
            if (uri.Host == "api.github.com") return new HttpResponseMessage(HttpStatusCode.Forbidden);
            if (uri.AbsolutePath.EndsWith("/releases.atom"))
                return Text("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><link href=\"https://github.com/test/project/releases/tag/v3.1.1\" /></entry></feed>");
            if (uri.AbsolutePath.Contains("/releases/expanded_assets/"))
                return Text(string.Join("\n", new[] { "", ".sha256", ".sig" }.Select(suffix =>
                    $"<a href=\"/test/project/releases/download/v3.1.1/ocr-resources-1.0.1-win-x64.zip{suffix}\">asset</a>")));
            if (uri.AbsolutePath == "/releases")
            {
                var assets = new[] { "", ".sha256", ".sig" }.Select((suffix, index) => new
                {
                    id = Revision * 10 + index,
                    updated_at = $"2026-11-{Revision:00}T00:00:00Z", download_count = DownloadCount,
                    name = "ocr-resources-1.0.1-win-x64.zip" + suffix,
                    browser_download_url = $"https://{uri.Host}/ocr.zip" + suffix, size = 0,
                });
                return Text(JsonSerializer.Serialize(new[] { new { assets } }));
            }
            if (uri.Host == FailedHost) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (uri.AbsolutePath.EndsWith(".sha256"))
                return Text(uri.Host == ChangedHost && !ChangeSignature ? new string('b', 64) : Hash);
            if (uri.AbsolutePath.EndsWith(".sig"))
            {
                SignatureStarted.TrySetResult();
                if (BlockSignature) await Task.Delay(Timeout.Infinite, cancellationToken);
                return Text(uri.Host == ChangedHost && ChangeSignature ? Convert.ToBase64String(new byte[256]) : Signature);
            }
            throw new InvalidOperationException($"Unexpected test request: {uri}");
        }
        private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
        protected override void Dispose(bool disposing)
        {
            if (disposing) Client.Dispose();
            base.Dispose(disposing);
        }
    }
}
