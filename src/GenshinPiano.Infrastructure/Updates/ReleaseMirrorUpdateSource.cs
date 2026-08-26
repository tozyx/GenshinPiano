using System.Net.Http.Headers;
using System.Text.Json;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.Infrastructure.Updates;

public sealed class ReleaseMirrorUpdateSource(
    HttpClient httpClient,
    string sourceName,
    Uri releasesEndpoint,
    bool frameworkDependent,
    SemanticVersion currentVersion) : IUpdateSource
{
    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, releasesEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("GenshinPiano-Updater/3.0");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{sourceName} returned an invalid releases response.");
        }

        var candidates = new List<ReleaseCandidate>();
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (GetBoolean(release, "draft") ||
                !release.TryGetProperty("tag_name", out var tagElement) ||
                !SemanticVersion.TryParse(tagElement.GetString(), out var version))
            {
                continue;
            }

            var isPreRelease = GetBoolean(release, "prerelease") ||
                               string.Equals(
                                   GetString(release, "release_status"),
                                   "pre",
                                   StringComparison.OrdinalIgnoreCase) ||
                               version.PreRelease is not null;
            if (string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) && isPreRelease)
            {
                continue;
            }
            if (version.CompareTo(currentVersion) <= 0)
            {
                continue;
            }

            var assets = ParseAssets(release);
            candidates.Add(new ReleaseCandidate(version, assets));
        }

        foreach (var candidate in candidates.OrderByDescending(item => item.Version))
        {
            var suffix = frameworkDependent ? "-win-x64-framework.zip" : "-win-x64.zip";
            var fileName = $"GenshinPiano-{candidate.Version}{suffix}";
            var packageAsset = candidate.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, fileName, StringComparison.OrdinalIgnoreCase));
            var checksumAsset = candidate.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, fileName + ".sha256", StringComparison.OrdinalIgnoreCase));
            if (packageAsset is null)
            {
                continue;
            }
            if (checksumAsset is null)
            {
                throw new InvalidDataException(
                    $"{sourceName} release {candidate.Version} is missing {fileName}.sha256.");
            }

            var sha256 = await DownloadChecksumAsync(checksumAsset.DownloadUri, cancellationToken);
            return new UpdateManifest(
                1,
                channel,
                candidate.Version,
                DateTimeOffset.UtcNow,
                [new UpdatePackage(
                    frameworkDependent
                        ? "app.win-x64.framework-dependent"
                        : "app.win-x64.self-contained",
                    UpdatePackageKind.Application,
                    candidate.Version,
                    fileName,
                    packageAsset.Size,
                    sha256,
                    packageAsset.DownloadUri)],
                sourceName);
        }

        return null;
    }

    private async Task<string> DownloadChecksumAsync(
        Uri downloadUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        request.Headers.UserAgent.ParseAdd("GenshinPiano-Updater/3.0");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var hash = content.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException($"{sourceName} returned an invalid SHA-256 checksum.");
        }
        return hash.ToUpperInvariant();
    }

    private static IReadOnlyList<ReleaseAsset> ParseAssets(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return assets.EnumerateArray()
            .Select(asset =>
            {
                var name = GetString(asset, "name");
                var url = GetString(asset, "browser_download_url");
                var size = asset.TryGetProperty("size", out var sizeElement) &&
                           sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0;
                return Uri.TryCreate(url, UriKind.Absolute, out var uri) && name.Length > 0
                    ? new ReleaseAsset(name, uri, Math.Max(0, size))
                    : null;
            })
            .Where(asset => asset is not null)
            .Cast<ReleaseAsset>()
            .ToArray();
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True;

    private sealed record ReleaseCandidate(
        SemanticVersion Version,
        IReadOnlyList<ReleaseAsset> Assets);

    private sealed record ReleaseAsset(string Name, Uri DownloadUri, long Size);
}
