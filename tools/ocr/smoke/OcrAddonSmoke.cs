using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Application.Updates;
using GenshinPiano.Infrastructure.Ocr;
using GenshinPiano.Infrastructure.Updates;

var options=new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented=true,Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)} };
if (args.Length==0) throw new ArgumentException("Commands: recognize <addon> <image> <output.json> [Staff|Numbered] [cancel-ms]; verify <zip> <public-key>; install <zip> <public-key> <install-root>");
if (args[0]=="recognize")
{
    var service=new ExternalOcrAddonService(args[1],TimeSpan.FromMinutes(5));
    using var cancellation=new CancellationTokenSource();
    if (args.Length>5 && int.Parse(args[5])>0) cancellation.CancelAfter(int.Parse(args[5]));
    var watch=Stopwatch.StartNew();
    var stages=new System.Collections.Concurrent.ConcurrentQueue<string>();
    try
    {
        var result=await service.AnalyzeAsync(args[2],args.Length>4?Enum.Parse<OcrNotationHint>(args[4]):OcrNotationHint.Staff,"zh-CN",
            progress:new Progress<OcrProgressUpdate>(p=>stages.Enqueue($"{p.Stage}:{p.Progress:F3}")),cancellationToken:cancellation.Token);
        await File.WriteAllTextAsync(args[3],JsonSerializer.Serialize(new { elapsedSeconds=watch.Elapsed.TotalSeconds,result,stages=stages.ToArray() },options));
        Console.WriteLine($"success={result.Success}; notes={result.Score?.Tracks.Sum(t=>t.Notes.Count) ?? 0}; seconds={watch.Elapsed.TotalSeconds:F3}; error={result.ErrorCode}");
        return result.Success?0:3;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        await File.WriteAllTextAsync(args[3],JsonSerializer.Serialize(new { cancelled=true,elapsedSeconds=watch.Elapsed.TotalSeconds },options));
        Console.WriteLine("Host cancellation completed.");
        return 0;
    }
}
else if (args[0] is "verify" or "install")
{
    var zip=Path.GetFullPath(args[1]);
    var publicKey=File.ReadAllText(args[2]);
    var checksum=File.ReadAllText(zip+".sha256").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)[0];
    var signature=File.ReadAllText(zip+".sig").Trim();
    if (!SemanticVersion.TryParse(Regex.Match(Path.GetFileName(zip),@"ocr-addons-(.+)-win-x64\.zip").Groups[1].Value,out var version))
        throw new InvalidDataException("Invalid OCR package filename.");
    var package=new UpdatePackage("addon.ocr.win-x64",UpdatePackageKind.OptionalComponent,version,Path.GetFileName(zip),new FileInfo(zip).Length,checksum,new Uri(zip),true,signature);
    var verifier=new SignedUpdatePackageVerifier(publicKey);
    var verified=await verifier.VerifyAsync(package,zip,CancellationToken.None);
    if (!verified) throw new InvalidDataException("Package signature/checksum validation failed.");
    using var archive=ZipFile.OpenRead(zip);
    var hasPython=archive.Entries.Any(e=>e.FullName.Contains("/python/",StringComparison.OrdinalIgnoreCase)||e.FullName.EndsWith(".py",StringComparison.OrdinalIgnoreCase));
    if (args[0]=="install")
    {
        var root=Path.GetFullPath(args[3]);
        var host=new ExternalOcrAddonService(Path.Combine(root,"addons","ocr"));
        var manager=new OcrAddonPackageManager(new LocalSource(package),new LocalDownload(zip),verifier,host,root);
        var result=await manager.DownloadAndInstallAsync(new Progress<double>());
        Console.WriteLine(JsonSerializer.Serialize(new { verified,hasPython,installed=result.Version,result.Updated },options));
    }
    else Console.WriteLine(JsonSerializer.Serialize(new { verified,hasPython,version=version.ToString(),bytes=package.Size,mib=package.Size/1048576d,entries=archive.Entries.Count },options));
    return 0;
}
throw new ArgumentException("Unknown command.");

sealed class LocalSource(UpdatePackage package) : IUpdateSource
{
    public Task<UpdateManifest?> GetLatestAsync(string channel,CancellationToken cancellationToken)=>Task.FromResult<UpdateManifest?>(new(1,channel,package.Version,DateTimeOffset.UtcNow,[package],"local-signed-package"));
}
sealed class LocalDownload(string path) : IUpdatePackageDownloader
{
    public Task<string> DownloadAsync(UpdatePackage package,IProgress<double> progress,CancellationToken cancellationToken)=>Task.FromResult(path);
}
