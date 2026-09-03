using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GenshinPiano.Infrastructure.Updates;

internal static partial class GitHubReleasePageFallback
{
    internal sealed record Asset(string Name, Uri DownloadUri, long Size = 0);
    internal sealed record Release(
        string TagName,
        IReadOnlyList<Asset> Assets,
        string ReleaseNotes);

    public static bool Supports(Uri endpoint) =>
        endpoint.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) &&
        TryGetRepository(endpoint, out _, out _);

    public static async Task<IReadOnlyList<Release>> GetReleasesAsync(
        HttpClient httpClient,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        if (!TryGetRepository(endpoint, out var owner, out var repository)) return [];
        var root = new Uri($"https://github.com/{owner}/{repository}/");
        using var atomRequest = CreateRequest(new Uri(root, "releases.atom"));
        using var atomResponse = await httpClient.SendAsync(atomRequest, cancellationToken);
        atomResponse.EnsureSuccessStatusCode();
        var atom = XDocument.Parse(await atomResponse.Content.ReadAsStringAsync(cancellationToken));
        XNamespace ns = "http://www.w3.org/2005/Atom";
        var entries = atom.Root?.Elements(ns + "entry")
            .Select(entry =>
            {
                var href = entry.Elements(ns + "link")
                    .Select(link => (string?)link.Attribute("href"))
                    .FirstOrDefault(value =>
                        value?.Contains("/releases/tag/", StringComparison.Ordinal) == true);
                var tag = string.IsNullOrWhiteSpace(href)
                    ? null
                    : Uri.UnescapeDataString(
                        href![(href.LastIndexOf("/tag/", StringComparison.Ordinal) + 5)..]);
                var notes = ConvertHtmlToMarkdown(entry.Element(ns + "content")?.Value ?? string.Empty);
                return new { Tag = tag, Notes = notes };
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Tag))
            .DistinctBy(entry => entry.Tag, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var releases = new List<Release>();
        foreach (var entry in entries)
        {
            var tag = entry.Tag!;
            var assetsUri = new Uri(root, $"releases/expanded_assets/{Uri.EscapeDataString(tag)}");
            using var assetsRequest = CreateRequest(assetsUri);
            using var assetsResponse = await httpClient.SendAsync(assetsRequest, cancellationToken);
            if (!assetsResponse.IsSuccessStatusCode) continue;
            var html = await assetsResponse.Content.ReadAsStringAsync(cancellationToken);
            var assets = AssetLinkRegex().Matches(html)
                .Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value))
                .Select(href => Uri.TryCreate(root, href, out var uri) ? uri : null)
                .Where(uri => uri is not null)
                .Select(uri => new Asset(
                    Uri.UnescapeDataString(uri!.Segments[^1]),
                    uri))
                .DistinctBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            releases.Add(new Release(tag, assets, entry.Notes));
        }
        return releases;
    }

    private static string ConvertHtmlToMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = Regex.Replace(html, @"<a\b[^>]*href\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<text>.*?)</a>",
            match => $"[{InlineText(match.Groups["text"].Value)}]({WebUtility.HtmlDecode(match.Groups["url"].Value)})",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<code\b[^>]*>(.*?)</code>",
            match => $"`{InlineText(match.Groups[1].Value)}`",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<h(?<level>[1-6])\b[^>]*>(?<text>.*?)</h\k<level>>",
            match => $"\n{new string('#', int.Parse(match.Groups["level"].Value))} {InlineText(match.Groups["text"].Value)}\n",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<li\b[^>]*>(.*?)</li>",
            match => $"\n- {InlineText(match.Groups[1].Value)}",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<blockquote\b[^>]*>", "\n> ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</blockquote>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<hr\b[^>]*>", "\n---\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(?:p|div|ul|ol)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty, RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string InlineText(string html) =>
        WebUtility.HtmlDecode(Regex.Replace(html, @"<[^>]+>", string.Empty))
            .Replace('\u00a0', ' ')
            .Trim();

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("GenshinPiano-Updater/3.0");
        return request;
    }

    private static bool TryGetRepository(Uri endpoint, out string owner, out string repository)
    {
        var parts = endpoint.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(parts, part => part.Equals("repos", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && parts.Length > index + 2)
        {
            owner = parts[index + 1];
            repository = parts[index + 2];
            return true;
        }
        owner = repository = string.Empty;
        return false;
    }

    [GeneratedRegex("href=\\\"(?<href>[^\\\"]*/releases/download/[^\\\"]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetLinkRegex();
}
