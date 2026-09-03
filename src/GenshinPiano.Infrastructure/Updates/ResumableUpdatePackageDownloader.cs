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
        if (await IsCompletedCacheAsync(partialPath, package, cancellationToken))
        {
            progress.Report(1);
            return partialPath;
        }

        var downloadUris = package.GetDownloadUris();
        if (downloadUris.Count == 1)
        {
            return await DownloadFromUriAsync(
                package, downloadUris[0], partialPath, progress, cancellationToken);
        }

        return await RaceMirrorsAsync(
            package, downloadUris, partialPath, progress, cancellationToken);
    }

    private async Task<string> RaceMirrorsAsync(
        UpdatePackage package,
        IReadOnlyList<Uri> downloadUris,
        string destinationPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressValues = new double[downloadUris.Count];
        var progressLock = new object();
        var attempts = downloadUris.Select((uri, index) =>
        {
            var mirrorPath = destinationPath + $".mirror-{index}";
            var mirrorProgress = new Progress<double>(value =>
            {
                lock (progressLock)
                {
                    progressValues[index] = Math.Clamp(value, 0, 0.99);
                    progress.Report(progressValues.Max());
                }
            });
            return DownloadAndValidateMirrorAsync(
                package, uri, mirrorPath, mirrorProgress, raceCancellation.Token);
        }).ToList();
        var failures = new List<Exception>();

        while (attempts.Count > 0)
        {
            var completed = await Task.WhenAny(attempts);
            attempts.Remove(completed);
            try
            {
                var winnerPath = await completed;
                raceCancellation.Cancel();
                await IgnoreCancelledAttemptsAsync(attempts);
                File.Move(winnerPath, destinationPath, overwrite: true);
                DeleteMirrorFiles(destinationPath, downloadUris.Count);
                progress.Report(1);
                return destinationPath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                raceCancellation.Cancel();
                await IgnoreCancelledAttemptsAsync(attempts);
                DeleteMirrorFiles(destinationPath, downloadUris.Count);
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        DeleteMirrorFiles(destinationPath, downloadUris.Count);
        throw new AggregateException("All update download mirrors failed.", failures);
    }

    private async Task<string> DownloadAndValidateMirrorAsync(
        UpdatePackage package,
        Uri downloadUri,
        string partialPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var path = await DownloadFromUriAsync(
            package, downloadUri, partialPath, progress, cancellationToken);
        if (!await MatchesChecksumAsync(path, package.Sha256, cancellationToken))
        {
            File.Delete(path);
            throw new InvalidDataException(
                $"Update mirror {downloadUri.Host} returned a package with an invalid checksum.");
        }
        return path;
    }

    private async Task<string> DownloadFromUriAsync(
        UpdatePackage package,
        Uri downloadUri,
        string partialPath,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
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

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            request.Headers.UserAgent.ParseAdd("GenshinPiano-Updater/3.0");
            if (existingLength > 0)
                request.Headers.Range = new RangeHeaderValue(existingLength, null);

            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
                existingLength > 0 && attempt == 0)
            {
                File.Delete(partialPath);
                existingLength = 0;
                continue;
            }
            response.EnsureSuccessStatusCode();
            var append = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!append) existingLength = 0;

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
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                var percent = totalLength > 0
                    ? (int)Math.Clamp(downloaded * 100 / totalLength, 0, 100)
                    : 0;
                if (percent == lastReported) continue;
                lastReported = percent;
                progress.Report(totalLength > 0 ? downloaded / (double)totalLength : 0);
            }
            await destination.FlushAsync(cancellationToken);
            progress.Report(1);
            return partialPath;
        }

        throw new InvalidOperationException("The update download could not be restarted.");
    }

    private static async Task<bool> IsCompletedCacheAsync(
        string path,
        UpdatePackage package,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || package.Size <= 0) return false;
        var length = new FileInfo(path).Length;
        if (length != package.Size)
        {
            if (length > package.Size) File.Delete(path);
            return false;
        }
        return await MatchesChecksumAsync(path, package.Sha256, cancellationToken);
    }

    private static async Task IgnoreCancelledAttemptsAsync(IEnumerable<Task<string>> attempts)
    {
        foreach (var attempt in attempts)
        {
            try { await attempt; }
            catch { /* The winning mirror cancels and supersedes remaining attempts. */ }
        }
    }

    private static void DeleteMirrorFiles(string destinationPath, int mirrorCount)
    {
        for (var index = 0; index < mirrorCount; index++)
        {
            var path = destinationPath + $".mirror-{index}";
            if (File.Exists(path)) File.Delete(path);
        }
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
