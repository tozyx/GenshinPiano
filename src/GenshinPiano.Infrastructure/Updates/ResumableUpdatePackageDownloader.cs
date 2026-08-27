using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.Infrastructure.Updates;

public sealed class ResumableUpdatePackageDownloader(
    HttpClient httpClient,
    string cacheDirectory) : IUpdatePackageDownloader
{
    public async Task<string> DownloadAsync(
        UpdatePackage package,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectory);
        var partialPath = Path.Combine(cacheDirectory, package.FileName + ".partial");
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (existingLength > 0 && package.Size > 0 && existingLength >= package.Size)
        {
            if (existingLength == package.Size && await MatchesChecksumAsync(
                    partialPath, package.Sha256, cancellationToken))
            {
                progress.Report(1);
                return partialPath;
            }

            File.Delete(partialPath);
            existingLength = 0;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, package.DownloadUri);
        request.Headers.UserAgent.ParseAdd("GenshinPiano-Updater/3.0");
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
            existingLength > 0)
        {
            // The cached prefix no longer matches the remote asset (for example,
            // a release asset was replaced under the same name), or the mirror
            // cannot resume this range. Discard it and retry once as a full GET.
            response.Dispose();
            File.Delete(partialPath);
            return await DownloadAsync(package, progress, cancellationToken);
        }
        response.EnsureSuccessStatusCode();
        var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append)
        {
            existingLength = 0;
        }

        var responseLength = response.Content.Headers.ContentLength ?? 0;
        var totalLength = package.Size > 0
            ? package.Size
            : existingLength + responseLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            partialPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        var downloaded = existingLength;
        var lastReported = -1;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            var percent = totalLength > 0
                ? (int)Math.Clamp(downloaded * 100 / totalLength, 0, 100)
                : 0;
            if (percent != lastReported)
            {
                lastReported = percent;
                progress.Report(totalLength > 0 ? downloaded / (double)totalLength : 0);
            }
        }
        await destination.FlushAsync(cancellationToken);
        progress.Report(1);
        return partialPath;
    }

    private static async Task<bool> MatchesChecksumAsync(
        string path, string expectedChecksum, CancellationToken cancellationToken)
    {
        if (expectedChecksum.Length != 64) return false;
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase);
    }
}
