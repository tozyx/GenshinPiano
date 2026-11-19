using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenshinPiano.Application.Updates;
using GenshinPiano.Application.Ocr;

namespace GenshinPiano.Infrastructure.Updates;

/// <summary>Finds the newest independently-versioned OCR package attached to any release.</summary>
public sealed partial class OcrAddonReleaseSource(
    HttpClient httpClient,
    string sourceName,
    Uri releasesEndpoint,
    bool resourcesOnly = false,
    bool deferVerification = false) : IUpdateSource, INamedUpdateSource
{
    private readonly OcrPackageMetadataResolver _metadataResolver = new(httpClient);
    public string SourceName => sourceName;

    public async Task<UpdateManifest?> GetLatestAsync(string channel, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, releasesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("GenshinPiano-OcrAddon/3.0");
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode && GitHubReleasePageFallback.Supports(releasesEndpoint))
        {
            var fallback = await GitHubReleasePageFallback.GetReleasesAsync(
                httpClient, releasesEndpoint, cancellationToken);
            var fallbackPackages = fallback.SelectMany(release =>
                CollectPackages(release.Assets.Select(asset =>
                    new ReleaseAsset(asset.Name, asset.DownloadUri, asset.Size)).ToArray(), channel));
            return await SelectManifestAsync(fallbackPackages, channel, cancellationToken);
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{sourceName} returned an invalid releases response.");

        var packages = new List<(SemanticVersion Version, ReleaseAsset Zip, ReleaseAsset Sha, ReleaseAsset Sig)>();
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (GetBoolean(release, "draft")) continue;
            packages.AddRange(CollectPackages(ParseAssets(release), channel));
        }

        return await SelectManifestAsync(packages, channel, cancellationToken);
    }

    private async Task<UpdateManifest?> SelectManifestAsync(
        IEnumerable<(SemanticVersion Version, ReleaseAsset Zip, ReleaseAsset Sha, ReleaseAsset Sig)> packages,
        string channel,
        CancellationToken cancellationToken)
    {
        var selected = packages.OrderByDescending(item => item.Version).FirstOrDefault();
        if (selected.Zip is null) return null;
        var package = new UpdatePackage(
            resourcesOnly ? OcrResourceContract.PackageId : OcrResourceContract.LegacyPackageId,
            UpdatePackageKind.OptionalComponent, selected.Version,
            selected.Zip.Name, selected.Zip.Size, string.Empty, selected.Zip.Uri,
            Optional: true, VerificationSources:
            [new(selected.Zip.Uri, selected.Zip.Size, selected.Sha.Uri, selected.Sig.Uri,
                $"{selected.Zip.Revision}\n{selected.Sha.Revision}\n{selected.Sig.Revision}")]);
        if (!deferVerification) package = await _metadataResolver.ResolveAsync(package, cancellationToken);
        return new UpdateManifest(1, channel, selected.Version, DateTimeOffset.UtcNow, [package], sourceName);
    }

    private IEnumerable<(SemanticVersion Version, ReleaseAsset Zip, ReleaseAsset Sha, ReleaseAsset Sig)>
        CollectPackages(IReadOnlyList<ReleaseAsset> assets, string channel)
    {
        foreach (var zip in assets)
        {
            var match = (resourcesOnly ? ResourcePackageNameRegex() : PackageNameRegex()).Match(zip.Name);
            if (!match.Success || !SemanticVersion.TryParse(match.Groups["version"].Value, out var version))
                continue;
            if (string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) &&
                version.PreRelease is not null) continue;
            var sha = assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            var sig = assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, zip.Name + ".sig", StringComparison.OrdinalIgnoreCase));
            if (sha is not null && sig is not null) yield return (version, zip, sha, sig);
        }
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
                ? new ReleaseAsset(name, uri, size, string.Join("|", new[] { "id", "updated_at", "digest" }
                    .Select(key => asset.TryGetProperty(key, out var revision) ? revision.ToString() : string.Empty))) : null;
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

    [GeneratedRegex(@"^ocr-resources-(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)-win-x64\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResourcePackageNameRegex();

    private sealed record ReleaseAsset(string Name, Uri Uri, long Size, string Revision = "");
}
