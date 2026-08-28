using System.Security.Cryptography;
using System.Text;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.Infrastructure.Updates;

public sealed class SignedUpdatePackageVerifier(string publicKeyXml) : IUpdatePackageVerifier
{
    private readonly RSAParameters _publicKey = ParsePublicKey(publicKeyXml);

    public async Task<bool> VerifyAsync(
        UpdatePackage package,
        string downloadedPath,
        CancellationToken cancellationToken)
    {
        if (package.Sha256.Length != 64 ||
            string.IsNullOrWhiteSpace(package.Signature) ||
            !File.Exists(downloadedPath))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(package.Signature);
        }
        catch (FormatException)
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
        if (!Convert.ToHexString(hash).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var rsa = RSA.Create();
        rsa.ImportParameters(_publicKey);
        var canonical = Encoding.UTF8.GetBytes(
            $"GenshinPiano.Update.v1\n{package.FileName}\n{Convert.ToHexString(hash)}\n");
        return rsa.VerifyData(
            canonical,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    private static RSAParameters ParsePublicKey(string publicKeyXml)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.FromXmlString(publicKeyXml);
            return rsa.ExportParameters(false);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new InvalidDataException("The embedded update signing public key is invalid.", exception);
        }
    }
}
