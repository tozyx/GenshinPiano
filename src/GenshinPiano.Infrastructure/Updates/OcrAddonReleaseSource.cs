using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.Infrastructure.Updates;

/// <summary>Finds the newest independently-versioned OCR package attached to any release.</summary>
public sealed partial class OcrAddonReleaseSource(
    HttpClient httpClient,
    string sourceName,
    Uri releasesEndpoint) : IUpdateSource, INamedUpdateSource
{
    public string SourceName => sourceName;

    public async Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, releasesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("GenshinPiano-OcrAddon/3.0");
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{sourceName} returned an invalid releases response.");

        var packages = new List<(SemanticVersion Version, ReleaseAsset Zip, ReleaseAsset Sha, ReleaseAsset Sig)>();
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (GetBoolean(release, "draft")) continue;
            var assets = ParseAssets(release);
            foreach (var zip in assets)
            {
                var match = PackageNameRegex().Match(zip.Name);
                if (!match.Success || !SemanticVersion.TryParse(match.Groups["version"].Value, out var version))
                    continue;
                if (string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) &&
                    version.PreRelease is not null) continue;
                var sha = assets.FirstOrDefault(asset =>
                    string.Equals(asset.Name, zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
                var sig = assets.FirstOrDefault(asset =>
                    string.Equals(asset.Name, zip.Name + ".sig", StringComparison.OrdinalIgnoreCase));
                if (sha is not null && sig is not null) packages.Add((version, zip, sha, sig));
            }
        }

        var selected = packages.OrderByDescending(item => item.Version).FirstOrDefault();
        if (selected.Zip is null) return null;
        var checksum = await DownloadTextAsync(selected.Sha.Uri, cancellationToken);
        checksum = checksum.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
        if (checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
            throw new InvalidDataException($"{sourceName} returned an invalid OCR package checksum.");
        var signature = await DownloadTextAsync(selected.Sig.Uri, cancellationToken);
        try { _ = Convert.FromBase64String(signature); }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"{sourceName} returned an invalid OCR package signature.", exception);
        }

        return new UpdateManifest(1, channel, selected.Version, DateTimeOffset.UtcNow,
        [
            new UpdatePackage(
                "addon.ocr.win-x64", UpdatePackageKind.OptionalComponent, selected.Version,
                selected.Zip.Name, selected.Zip.Size, checksum, selected.Zip.Uri,
                Optional: true, Signature: signature),
        ], sourceName);
    }

    private async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("GenshinPiano-OcrAddon/3.0");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
    }

    private static IReadOnlyList<ReleaseAsset> ParseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return [];
        return assets.EnumerateArray().Select(asset =>
        {
            var name = GetString(asset, "name");
            var url = GetString(asset, "browser_download_url");
            var size = asset.TryGetProperty("size", out var value) && value.TryGetInt64(out var parsed)
                ? Math.Max(0, parsed) : 0;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && name.Length > 0
                ? new ReleaseAsset(name, uri, size) : null;
        }).Where(asset => asset is not null).Cast<ReleaseAsset>().ToArray();
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^ocr-addons-(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)-win-x64\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PackageNameRegex();

    private sealed record ReleaseAsset(string Name, Uri Uri, long Size);
}
