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
    public async Task OcrAddonSource_SelectsNewestSignedComponentIndependentOfReleaseTag()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var signature = Convert.ToBase64String(new byte[384]);
        var releases = """
            [{
              "tag_name":"v3.0.1",
              "prerelease":false,
              "assets":[
                {"name":"ocr-addons-0.6.0-win-x64.zip","size":60,"browser_download_url":"https://download.test/old.zip"},
                {"name":"ocr-addons-0.6.0-win-x64.zip.sha256","browser_download_url":"https://download.test/old.sha256"},
                {"name":"ocr-addons-0.6.0-win-x64.zip.sig","browser_download_url":"https://download.test/old.sig"},
                {"name":"ocr-addons-0.7.0-win-x64.zip","size":70,"browser_download_url":"https://download.test/new.zip"},
                {"name":"ocr-addons-0.7.0-win-x64.zip.sha256","browser_download_url":"https://download.test/new.sha256"},
                {"name":"ocr-addons-0.7.0-win-x64.zip.sig","browser_download_url":"https://download.test/new.sig"}
              ]
            }]
            """;
        using var client = new HttpClient(new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal))
                return TextResponse(hash);
            if (request.RequestUri.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal))
                return TextResponse(signature);
            return JsonResponse(releases);
        }));

        var source = new OcrAddonReleaseSource(
            client, "test", new Uri("https://api.test/releases"));
        var manifest = await source.GetLatestAsync("stable", CancellationToken.None);

        Assert.NotNull(manifest);
        Assert.Equal("0.7.0", manifest.Version.ToString());
        var package = Assert.Single(manifest.Packages);
        Assert.Equal(GenshinPiano.Application.Updates.UpdatePackageKind.OptionalComponent, package.Kind);
        Assert.True(package.Optional);
        Assert.Equal("ocr-addons-0.7.0-win-x64.zip", package.FileName);
        Assert.Equal(70, package.Size);
    }

    [Fact]
    public async Task ReleaseSource_SelectsMatchingSelfContainedAssetAndChecksum()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var signature = Convert.ToBase64String(new byte[384]);
        var releases = """
            [{
              "tag_name":"v3.1.0-preview.2",
              "prerelease":true,
              "assets":[
                {"name":"GenshinPiano-3.1.0-preview.2-win-x64.zip","size":123,"browser_download_url":"https://download.test/app.zip"},
                {"name":"GenshinPiano-3.1.0-preview.2-win-x64.zip.sha256","browser_download_url":"https://download.test/app.zip.sha256"},
                {"name":"GenshinPiano-3.1.0-preview.2-win-x64.zip.sig","browser_download_url":"https://download.test/app.zip.sig"},
                {"name":"GenshinPiano-3.1.0-preview.2-win-x64-framework.zip","size":99,"browser_download_url":"https://download.test/framework.zip"}
              ]
            }]
            """;
        using var client = new HttpClient(new StubHttpHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith(".sha256", StringComparison.Ordinal))
                return TextResponse(hash + "  app.zip");
            if (path.EndsWith(".sig", StringComparison.Ordinal))
                return TextResponse(signature);
            return JsonResponse(releases);
        }));
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
        Assert.Equal(signature, package.Signature);
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
    public async Task Downloader_RestartsFullDownloadWhenServerRejectsCachedRange()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GenshinPianoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var partialPath = Path.Combine(directory, "app.zip.partial");
            await File.WriteAllBytesAsync(partialPath, [1, 2, 3]);
            var requestCount = 0;
            using var client = new HttpClient(new StubHttpHandler(request =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    Assert.NotNull(request.Headers.Range);
                    return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
                }

                Assert.Null(request.Headers.Range);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([7, 8, 9, 10, 11, 12]),
                };
            }));
            var downloader = new ResumableUpdatePackageDownloader(client, directory);

            var path = await downloader.DownloadAsync(
                CreatePackage(6),
                new Progress<double>(),
                CancellationToken.None);

            Assert.Equal(2, requestCount);
            Assert.Equal([7, 8, 9, 10, 11, 12], await File.ReadAllBytesAsync(path));
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

    [Fact]
    public async Task SignedVerifier_RequiresMatchingPackageHashAndTrustedSignature()
    {
        var path = Path.GetTempFileName();
        try
        {
            var data = Encoding.UTF8.GetBytes("signed GenshinPiano update package");
            await File.WriteAllBytesAsync(path, data);
            var hash = SHA256.HashData(data);
            using var trustedKey = RSA.Create(3072);
            using var otherKey = RSA.Create(3072);
            var canonical = Encoding.UTF8.GetBytes(
                $"GenshinPiano.Update.v1\napp.zip\n{Convert.ToHexString(hash)}\n");
            var signature = trustedKey.SignData(
                canonical,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var package = CreatePackage(data.Length) with
            {
                Sha256 = Convert.ToHexString(hash),
                Signature = Convert.ToBase64String(signature),
            };
            var verifier = new SignedUpdatePackageVerifier(trustedKey.ToXmlString(false));

            Assert.True(await verifier.VerifyAsync(package, path, CancellationToken.None));

            var untrustedSignature = otherKey.SignData(
                canonical,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            Assert.False(await verifier.VerifyAsync(
                package with { Signature = Convert.ToBase64String(untrustedSignature) },
                path,
                CancellationToken.None));
            Assert.False(await verifier.VerifyAsync(
                package with { FileName = "GenshinPiano-9.9.9-win-x64.zip" },
                path,
                CancellationToken.None));
            Assert.False(await verifier.VerifyAsync(
                package with { Sha256 = new string('0', 64) },
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
