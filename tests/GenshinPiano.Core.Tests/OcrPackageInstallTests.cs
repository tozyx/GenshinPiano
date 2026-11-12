using System.IO.Compression;
using System.Text.Json;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Application.Updates;
using GenshinPiano.Infrastructure.Ocr;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class OcrPackageInstallTests
{
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; }=Path.Combine(Path.GetTempPath(),"GPiano-OcrInstall-"+Guid.NewGuid().ToString("N"));
        public Sandbox() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root,true);
    }
    private static void Addon(string path,string version,int protocol=1,bool required=false)
    {
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path,"engine.exe"),[1]);
        File.WriteAllText(Path.Combine(path,"manifest.json"),JsonSerializer.Serialize(new
        {
            schemaVersion=1,protocolVersion=protocol,engineVersion=version,executable="engine.exe",launchMode="file",
            requiredFiles=required?new[]{"model.gguf"}:Array.Empty<string>(),
        }));
    }
    private sealed class Source(UpdatePackage package) : IUpdateSource
    {
        public int Calls { get; private set; }
        public Task<UpdateManifest?> GetLatestAsync(string channel,CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<UpdateManifest?>(new(1,channel,package.Version,DateTimeOffset.UtcNow,[package],"test"));
        }
    }
    private sealed class Downloader(string zip) : IUpdatePackageDownloader
    {
        public int Calls { get; private set; }
        public Task<string> DownloadAsync(UpdatePackage package,IProgress<double> progress,CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(zip);
        }
    }
    private sealed class Verified : IUpdatePackageVerifier
    {
        public Task<bool> VerifyAsync(UpdatePackage package,string downloadedPath,CancellationToken cancellationToken)=>Task.FromResult(true);
    }
    private sealed class RejectAfterSwap(ExternalOcrAddonService service) : IOcrAddonService
    {
        private int _calls;
        public OcrAddonDescriptor? FindInstalledAddon()=>_calls++==0?service.FindInstalledAddon():null;
        public Task<OcrAnalysisResult> AnalyzeAsync(string imagePath,OcrNotationHint notationHint,string language,OcrWatermarkMode watermarkMode=OcrWatermarkMode.Auto,bool includeAccompaniment=true,IProgress<OcrProgressUpdate>? progress=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private static OcrAddonPackageManager Manager(string root,string zip,IOcrAddonService service)=>new(
        new Source(new("addon.ocr.win-x64",UpdatePackageKind.OptionalComponent,new(0,9,0),"ocr-addons-0.9.0-win-x64.zip",0,new string('a',64),new Uri("https://test.invalid/package.zip"))),
        new Downloader(zip),new Verified(),service,root);

    [Fact]
    public async Task LegacyAddonCanBeUpgradedWithoutChangingProtocol()
    {
        using var box=new Sandbox();
        var target=Path.Combine(box.Root,"addons","ocr");
        Addon(target,"0.8.0");File.WriteAllText(Path.Combine(target,"old-python-marker"),"old");
        var source=Path.Combine(box.Root,"package");Addon(Path.Combine(source,"addons","ocr"),"0.9.0");
        var zip=Path.Combine(box.Root,"new.zip");ZipFile.CreateFromDirectory(source,zip);
        var host=new ExternalOcrAddonService(target);
        var result=await Manager(box.Root,zip,host).DownloadAndInstallAsync(new Progress<double>());
        Assert.True(result.Updated);
        Assert.Equal("0.9.0",host.FindInstalledAddon()!.EngineVersion);
        Assert.False(File.Exists(Path.Combine(target,"old-python-marker")));
    }

    [Theory]
    [InlineData(2,false)]
    [InlineData(1,true)]
    public async Task IncompatibleOrIncompletePackageCannotReplaceInstalledAddon(int protocol,bool missingModel)
    {
        using var box=new Sandbox();
        var target=Path.Combine(box.Root,"addons","ocr");Addon(target,"0.8.0");
        var source=Path.Combine(box.Root,"package");Addon(Path.Combine(source,"addons","ocr"),"0.9.0",protocol,missingModel);
        var zip=Path.Combine(box.Root,"new.zip");ZipFile.CreateFromDirectory(source,zip);
        var host=new ExternalOcrAddonService(target);
        await Assert.ThrowsAsync<InvalidDataException>(()=>Manager(box.Root,zip,host).DownloadAndInstallAsync(new Progress<double>()));
        Assert.Equal("0.8.0",host.FindInstalledAddon()!.EngineVersion);
    }

    [Fact]
    public async Task FailedPostSwapValidationRestoresPreviousAddon()
    {
        using var box=new Sandbox();
        var target=Path.Combine(box.Root,"addons","ocr");Addon(target,"0.8.0");
        var source=Path.Combine(box.Root,"package");Addon(Path.Combine(source,"addons","ocr"),"0.9.0");
        var zip=Path.Combine(box.Root,"new.zip");ZipFile.CreateFromDirectory(source,zip);
        var host=new ExternalOcrAddonService(target);
        await Assert.ThrowsAsync<InvalidDataException>(()=>Manager(box.Root,zip,new RejectAfterSwap(host)).DownloadAndInstallAsync(new Progress<double>()));
        Assert.Equal("0.8.0",host.FindInstalledAddon()!.EngineVersion);
    }

    [Fact]
    public async Task CheckingVersionsDoesNotDownloadAndSelectedInstallDoesNotRecheck()
    {
        using var box = new Sandbox();
        var target = Path.Combine(box.Root,"addons","ocr");
        Addon(target,"0.8.0");
        var staging = Path.Combine(box.Root,"package");
        Addon(Path.Combine(staging,"addons","ocr"),"0.9.0");
        var zip = Path.Combine(box.Root,"new.zip");
        ZipFile.CreateFromDirectory(staging,zip);
        var source = new Source(new("addon.ocr.win-x64",UpdatePackageKind.OptionalComponent,new(0,9,0),"ocr.zip",0,new string('a',64),new Uri("https://test.invalid/ocr.zip")));
        var downloader = new Downloader(zip);
        var host = new ExternalOcrAddonService(target);
        var manager = new OcrAddonPackageManager(source,downloader,new Verified(),host,box.Root);
        var check = await manager.CheckForUpdatesAsync();
        Assert.True(check.CanUpdate);
        Assert.False(check.IsCurrent);
        Assert.Equal("0.8.0",host.FindInstalledAddon()!.EngineVersion);
        Assert.Equal(0,downloader.Calls);

        using (manager.BeginRecognition())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InstallCheckedUpdateAsync(check,new Progress<double>()));
            Assert.Equal(0,downloader.Calls);
            Assert.False(manager.IsInstalling);
        }
        Assert.False(manager.IsRecognizing);
        await manager.InstallCheckedUpdateAsync(check,new Progress<double>());
        Assert.True(manager.LastCheck!.IsCurrent);
        Assert.Equal(1,source.Calls);
        Assert.Equal(1,downloader.Calls);
        Assert.False(manager.IsInstalling);
    }

    [Theory]
    [InlineData("0.9.0",true,false)]
    [InlineData("0.10.0",true,false)]
    [InlineData("0.8.0",false,true)]
    public async Task LatestStatusComparesComponentVersions(string installed,bool isCurrent,bool canUpdate)
    {
        using var box = new Sandbox();
        var target = Path.Combine(box.Root,"addons","ocr"); Addon(target,installed);
        var manager = Manager(box.Root,"unused.zip",new ExternalOcrAddonService(target));
        var check = await manager.CheckForUpdatesAsync();
        Assert.Equal(isCurrent,check.IsCurrent);
        Assert.Equal(canUpdate,check.CanUpdate);
    }

    [Theory]
    [InlineData(null, "1.0.0", true, false)]
    [InlineData("0.7.0", "0.8.0", true, false)]
    [InlineData("0.7.0", "1.0.0", true, false)]
    [InlineData("1.0.0", "0.7.0", false, true)]
    [InlineData("1.0.0", null, false, true)]
    public async Task CachedCheckUsesManuallyChangedInstalledVersion(
        string? initialVersion, string? installedVersion, bool isCurrent, bool canUpdate)
    {
        using var box = new Sandbox();
        var target = Path.Combine(box.Root, "addons", "ocr");
        if (initialVersion is not null) Addon(target, initialVersion);
        var source = new Source(new("addon.ocr.win-x64", UpdatePackageKind.OptionalComponent,
            new(0,8,0), "ocr.zip", 0, new string('a',64), new Uri("https://test.invalid/ocr.zip")));
        var downloader = new Downloader("unused.zip");
        var manager = new OcrAddonPackageManager(source, downloader, new Verified(),
            new ExternalOcrAddonService(target), box.Root);
        Assert.Null(manager.LastCheck);
        Assert.Equal(0, source.Calls);
        var original = await manager.CheckForUpdatesAsync();

        // Simulate extracting/replacing/removing the plug-in outside the application.
        if (installedVersion is not null) Addon(target, installedVersion);
        else Directory.Delete(target, true);

        var current = Assert.IsType<OcrAddonUpdateCheck>(manager.LastCheck);
        Assert.Equal(installedVersion, current.InstalledVersion);
        Assert.Equal(isCurrent, current.IsCurrent);
        Assert.Equal(canUpdate, current.CanUpdate);
        Assert.Same(original.Manifest, current.Manifest);
        Assert.Same(original.Package, current.Package);
        Assert.Equal(original.CheckedAt, current.CheckedAt);
        Assert.Equal(1, source.Calls);
        Assert.Equal(0, downloader.Calls);

        // An update selected before the manual installation must never downgrade it.
        if (isCurrent)
        {
            var result = await manager.InstallCheckedUpdateAsync(original, new Progress<double>());
            Assert.False(result.Updated);
            Assert.Equal(installedVersion, result.Version);
            Assert.Equal(0, downloader.Calls);
        }
    }

    private sealed class OfflineSource : IUpdateSource
    {
        public Task<UpdateManifest?> GetLatestAsync(string channel,CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    [Fact]
    public async Task FailedCheckIsUnknownInsteadOfUpToDate()
    {
        using var box = new Sandbox();
        var target = Path.Combine(box.Root,"addons","ocr"); Addon(target,"0.9.0");
        var manager = new OcrAddonPackageManager(new OfflineSource(),new Downloader("unused"),new Verified(),new ExternalOcrAddonService(target),box.Root);
        var check = await manager.CheckForUpdatesAsync();
        Assert.False(check.IsCurrent);
        Assert.False(check.CanUpdate);
        Assert.Equal("offline",check.ErrorMessage);
        Assert.Equal("0.9.0",check.InstalledVersion);
    }
}
