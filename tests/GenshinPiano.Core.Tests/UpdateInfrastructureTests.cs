using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using GenshinPiano.Infrastructure.Updates;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class UpdateInfrastructureTests
{
    [Fact]
    public async Task ReleaseSource_SelectsMatchingSelfContainedAssetAndChecksum()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var releases = """
            [{
              "tag_name":"v3.1.0-preview.2",
              "prerelease":true,
              "assets":[
                {"name":"GenshinPiano-3.1.0-preview.2-win-x64.zip","size":123,"browser_download_url":"https://download.test/app.zip"},
                {"name":"GenshinPiano-3.1.0-preview.2-win-x64.zip.sha256","browser_download_url":"https://download.test/app.zip.sha256"},
                {"name":"GenshinPiano-3.1.0-preview.2-win-x64-framework.zip","size":99,"browser_download_url":"https://download.test/framework.zip"}
              ]
            }]
            """;
        using var client = new HttpClient(new StubHttpHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? TextResponse(hash + "  app.zip")
                : JsonResponse(releases)));
        var source = new ReleaseMirrorUpdateSource(
            client,
            "test",
            new Uri("https://api.test/releases"),
            frameworkDependent: false,
            currentVersion: new GenshinPiano.Application.Updates.SemanticVersion(3, 0, 0));

        var manifest = await source.GetLatestAsync("preview", CancellationToken.None);

        Assert.NotNull(manifest);
        var package = Assert.Single(manifest.Packages);
        Assert.Equal("GenshinPiano-3.1.0-preview.2-win-x64.zip", package.FileName);
        Assert.Equal(hash.ToUpperInvariant(), package.Sha256);
        Assert.Equal(123, package.Size);
    }

    [Fact]
    public async Task ReleaseSource_StableChannelIgnoresPrereleases()
    {
        var releases = """
            [
              {"tag_name":"v4.0.0-preview.1","prerelease":true,"assets":[]},
              {"tag_name":"v3.2.0","prerelease":false,"assets":[]}
            ]
            """;
        using var client = new HttpClient(new StubHttpHandler(_ => JsonResponse(releases)));
        var source = new ReleaseMirrorUpdateSource(
            client,
            "test",
            new Uri("https://api.test/releases"),
            frameworkDependent: false,
            currentVersion: new GenshinPiano.Application.Updates.SemanticVersion(3, 0, 0));

        var manifest = await source.GetLatestAsync("stable", CancellationToken.None);

        Assert.Null(manifest);
    }

    [Fact]
    public async Task Downloader_ResumesPartialFileWithRangeRequest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GenshinPianoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var partialPath = Path.Combine(directory, "app.zip.partial");
            await File.WriteAllBytesAsync(partialPath, [1, 2, 3]);
            RangeHeaderValue? observedRange = null;
            using var client = new HttpClient(new StubHttpHandler(request =>
            {
                observedRange = request.Headers.Range;
                return new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    Content = new ByteArrayContent([4, 5, 6]),
                };
            }));
            var downloader = new ResumableUpdatePackageDownloader(client, directory);
            var package = CreatePackage(6);

            var path = await downloader.DownloadAsync(
                package,
                new Progress<double>(),
                CancellationToken.None);

            Assert.Equal(3, observedRange?.Ranges.Single().From);
            Assert.Equal([1, 2, 3, 4, 5, 6], await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Verifier_ValidatesSha256()
    {
        var path = Path.GetTempFileName();
        try
        {
            var data = Encoding.UTF8.GetBytes("GenshinPiano update package");
            await File.WriteAllBytesAsync(path, data);
            var hash = Convert.ToHexString(SHA256.HashData(data));
            var verifier = new Sha256UpdatePackageVerifier();

            Assert.True(await verifier.VerifyAsync(
                CreatePackage(data.Length) with { Sha256 = hash },
                path,
                CancellationToken.None));
            Assert.False(await verifier.VerifyAsync(
                CreatePackage(data.Length) with { Sha256 = new string('0', 64) },
                path,
                CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static GenshinPiano.Application.Updates.UpdatePackage CreatePackage(long size) => new(
        "app.win-x64",
        GenshinPiano.Application.Updates.UpdatePackageKind.Application,
        new GenshinPiano.Application.Updates.SemanticVersion(3, 1, 0),
        "app.zip",
        size,
        new string('0', 64),
        new Uri("https://download.test/app.zip"));

    private static HttpResponseMessage JsonResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage TextResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.ASCII, "text/plain"),
    };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
