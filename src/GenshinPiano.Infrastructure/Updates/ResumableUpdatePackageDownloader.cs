using System.Net;
using System.Net.Http.Headers;
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
}
