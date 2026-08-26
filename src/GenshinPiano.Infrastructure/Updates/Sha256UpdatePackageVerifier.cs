using System.Security.Cryptography;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.Infrastructure.Updates;

public sealed class Sha256UpdatePackageVerifier : IUpdatePackageVerifier
{
    public async Task<bool> VerifyAsync(
        UpdatePackage package,
        string downloadedPath,
        CancellationToken cancellationToken)
    {
        if (package.Sha256.Length != 64 || !File.Exists(downloadedPath))
        {
            return false;
        }

        await using var stream = new FileStream(
            downloadedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(
            Convert.ToHexString(hash),
            package.Sha256,
            StringComparison.OrdinalIgnoreCase);
    }
}
