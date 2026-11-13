using System.IO.Compression;
using System.Text.Json;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Application.Updates;
using GenshinPiano.Infrastructure.Ocr;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class OcrResourcePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GPiano-OcrResources-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(_root, "addons", "ocr");
    private string Worker => Path.Combine(_root, "GenshinPiano.exe");

    public OcrResourcePackageTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Worker, [1]);
    }

    private static void Resources(string path, string version = "1.0.0", int compatibility = 1, string? requiredFile = null)
    {
        foreach (var name in OcrAddonPackageValidator.RequiredResources)
        {
            var file = Path.Combine(path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, [1]);
        }
        File.WriteAllText(Path.Combine(path, "manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 2, protocolVersion = 1, resourceVersion = version,
            engineCompatibilityVersion = compatibility, staffBackend = "smt", smtModelSha256 = new string('a', 64),
            requiredFiles = requiredFile is null ? OcrAddonPackageValidator.RequiredResources : new[] { requiredFile },
        }));
    }

    private void Legacy(string version = "1.0.0")
    {
        Directory.CreateDirectory(Target);
        File.WriteAllBytes(Path.Combine(Target, "old.exe"), [1]);
        File.WriteAllText(Path.Combine(Target, "manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, protocolVersion = 1, engineVersion = version, executable = "old.exe", launchMode = "file",
        }));
    }

    private ExternalOcrAddonService Service(bool withWorker = true) => new(Target, workerExecutablePath: withWorker ? Worker : null);

    [Fact]
    public void ResourceDiscoveryUsesTrustedHostExecutable_WithoutAddonExecutable()
    {
        Resources(Target);
        var descriptor = Service().FindInstalledAddon();
        Assert.NotNull(descriptor);
        Assert.True(descriptor.IsResourcePackage);
        Assert.True(descriptor.UsesBundledEngine);
        Assert.Equal(Worker, descriptor.ExecutablePath);
        Assert.Equal("1.0.0", descriptor.EngineVersion);
        Assert.Null(Service(withWorker: false).FindInstalledAddon());
        Assert.False(File.Exists(Path.Combine(Target, "GPianoOcrEngine.exe")));
    }

    [Fact]
    public void LegacyFullPackageCanUseBundledEngineWithItsOriginalResourceLayout()
    {
        Legacy();
        foreach (var name in OcrAddonPackageValidator.RequiredResources)
        {
            var relative = name.StartsWith("native/", StringComparison.Ordinal) ? name[7..] : name;
            var file = Path.Combine(Target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, [1]);
        }
        var descriptor = Service().FindInstalledAddon();
        Assert.True(descriptor!.UsesBundledEngine);
        Assert.False(descriptor.IsResourcePackage);
        Assert.Equal(Worker, descriptor.ExecutablePath);
        Assert.Equal(Path.Combine(Target, "old.exe"), Service(false).FindInstalledAddon()!.ExecutablePath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IncompatibleResourceFormatIsRejected(int compatibility)
    {
        Resources(Target, compatibility: compatibility);
        Assert.Null(Service().FindInstalledAddon());
        Assert.Throws<InvalidDataException>(() => OcrAddonPackageValidator.Validate(Target));
    }

    [Fact]
    public void NullModelChecksumIsRejectedWithoutBreakingDiscovery()
    {
        Resources(Target);
        var path = Path.Combine(Target, "manifest.json");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        manifest["smtModelSha256"] = null;
        File.WriteAllText(path, manifest.ToJsonString());
        Assert.Null(Service().FindInstalledAddon());
        Assert.Throws<InvalidDataException>(() => OcrAddonPackageValidator.Validate(Target));
    }

    [Fact]
    public void ManifestCannotOmitRequiredRuntimeOrEscapeResourceDirectory()
    {
        Resources(Target, requiredFile: "models/orpheusnet/orpheusnet-middle.onnx");
        File.Delete(Path.Combine(Target, "native", "onnxruntime.dll"));
        Assert.Null(Service().FindInstalledAddon());
        Resources(Target, requiredFile: "../outside.dll");
        File.WriteAllBytes(Path.Combine(_root, "addons", "outside.dll"), [1]);
        Assert.Null(Service().FindInstalledAddon());
    }

    [Fact]
    public async Task SameVersionLegacyToResourcesMigrationInstallsOnce_AndClearsMigrationStatus()
    {
        Legacy();
        var (manager, download) = Manager();
        var check = await manager.CheckForUpdatesAsync();
        Assert.True(check.RequiresMigration);
        Assert.True(check.CanUpdate);
        Assert.False(check.IsCurrent);
        var result = await manager.InstallCheckedUpdateAsync(check, new Progress<double>());
        Assert.True(result.Updated);
        Assert.Equal(1, download.Calls);
        Assert.True(Service().FindInstalledAddon()!.IsResourcePackage);
        Assert.False(File.Exists(Path.Combine(Target, "old.exe")));
        Assert.False(manager.LastCheck!.RequiresMigration);
        Assert.True(manager.LastCheck.IsCurrent);
        Assert.False((await manager.InstallCheckedUpdateAsync(check, new Progress<double>())).Updated);
        Assert.Equal(1, download.Calls);
    }

    [Fact]
    public async Task ManualMigrationRefreshesCachedStatusWithoutNetworkCheck()
    {
        Legacy();
        var (manager, _) = Manager();
        Assert.True((await manager.CheckForUpdatesAsync()).RequiresMigration);
        Resources(Target);
        Assert.False(manager.LastCheck!.RequiresMigration);
        Assert.True(manager.LastCheck.IsCurrent);
        Assert.False(manager.LastCheck.CanUpdate);
    }

    [Fact]
    public async Task NewerResourcesAreNeverDowngraded()
    {
        Resources(Target, "2.0.0");
        var (manager, download) = Manager();
        var check = await manager.CheckForUpdatesAsync();
        Assert.True(check.IsCurrent);
        Assert.False(check.RequiresMigration);
        Assert.False((await manager.InstallCheckedUpdateAsync(check, new Progress<double>())).Updated);
        Assert.Equal(0, download.Calls);
    }

    [Fact]
    public async Task ResourcesWithoutBundledHostRollBackToLegacyPackage()
    {
        Legacy();
        var (manager, _) = Manager(withWorker: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAndInstallAsync(new Progress<double>()));
        Assert.True(File.Exists(Path.Combine(Target, "old.exe")));
        Assert.False(Service(false).FindInstalledAddon()!.IsResourcePackage);
    }

    [Fact]
    public async Task ResourceArchiveCannotMasqueradeAsLegacyComponent()
    {
        Legacy("0.8.0");
        var (manager, _) = Manager(packageId: OcrResourceContract.LegacyPackageId);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAndInstallAsync(new Progress<double>()));
        Assert.Equal("0.8.0", Service().FindInstalledAddon()!.EngineVersion);
    }

    private (OcrAddonPackageManager Manager, Download Download) Manager(bool withWorker = true, string packageId = OcrResourceContract.PackageId)
    {
        var staging = Path.Combine(_root, "package");
        Resources(Path.Combine(staging, "addons", "ocr"));
        var zip = Path.Combine(_root, "resources.zip");
        ZipFile.CreateFromDirectory(staging, zip);
        var package = new UpdatePackage(packageId, UpdatePackageKind.OptionalComponent, new(1, 0, 0), "ocr-resources-1.0.0-win-x64.zip", 0,
            new string('a', 64), new Uri("https://test.invalid/ocr.zip"));
        var download = new Download(zip);
        return (new(new Source(package), download, new Verified(), Service(withWorker), _root), download);
    }

    private sealed class Source(UpdatePackage package) : IUpdateSource
    {
        public Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken cancellationToken) =>
            Task.FromResult<UpdateManifest?>(new(1, channel, package.Version, DateTimeOffset.UtcNow, [package], "test"));
    }

    private sealed class Download(string zip) : IUpdatePackageDownloader
    {
        public int Calls { get; private set; }
        public Task<string> DownloadAsync(UpdatePackage package, IProgress<double> progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(zip);
        }
    }

    private sealed class Verified : IUpdatePackageVerifier
    {
        public Task<bool> VerifyAsync(UpdatePackage package, string downloadedPath, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
