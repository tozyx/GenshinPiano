using System.Collections.Concurrent;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.Infrastructure.Updates;

/// <summary>Resolves OCR verification files on installation, keeping only equivalent download mirrors.</summary>
public sealed class OcrPackageMetadataResolver(HttpClient httpClient, TimeProvider? timeProvider = null)
    : IUpdatePackageMetadataResolver
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);
    private readonly ConcurrentDictionary<UpdateVerificationSource, Verification> _cache = new();

    public async Task<UpdatePackage> ResolveAsync(UpdatePackage package, CancellationToken cancellationToken)
    {
        if (package.VerificationSources is not { Count: > 0 } sources) return package;
        await _resolutionLock.WaitAsync(cancellationToken);
        try
        {
            var attempts = await Task.WhenAll(sources.Distinct().Select(async source =>
            {
                try { return (Source: source, Value: await GetVerificationAsync(source, cancellationToken), Error: (Exception?)null); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception error) { return (Source: source, Value: (Verification?)null, Error: error); }
            }));
            cancellationToken.ThrowIfCancellationRequested();
            var available = attempts.Where(attempt => attempt.Value is not null).ToArray();
            if (available.Length == 0)
                throw new AggregateException("Could not retrieve OCR checksum and signature files.",
                    attempts.Select(attempt => attempt.Error!));
            var selected = available[0];
            var verification = selected.Value!;
            var mirrors = available.Skip(1)
                .Where(attempt => attempt.Value!.Checksum == verification.Checksum &&
                    attempt.Value.Signature == verification.Signature)
                .Select(attempt => attempt.Source.DownloadUri).ToArray();
            return package with
            {
                Sha256 = verification.Checksum,
                Signature = verification.Signature,
                DownloadUri = selected.Source.DownloadUri,
                Size = selected.Source.Size,
                MirrorDownloadUris = mirrors,
                VerificationSources = null,
            };
        }
        finally { _resolutionLock.Release(); }
    }

    private async Task<Verification> GetVerificationAsync(UpdateVerificationSource source, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(source, out var cached) && _clock.GetUtcNow() - cached.CheckedAt < TimeSpan.FromMinutes(5))
            return cached;
        var checksumText = await DownloadTextAsync(source.ChecksumUri, cancellationToken);
        var checksum = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant();
        if (checksum is null || checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid OCR package checksum.");
        var signature = await DownloadTextAsync(source.SignatureUri, cancellationToken);
        try
        {
            if (Convert.FromBase64String(signature).Length == 0) throw new FormatException("Empty signature.");
        }
        catch (FormatException error) { throw new InvalidDataException("Invalid OCR package signature.", error); }
        var verification = new Verification(checksum, signature, _clock.GetUtcNow());
        // Bound the in-memory cache; failures and cancellation are never cached.
        if (_cache.Count >= 64) _cache.Clear();
        _cache[source] = verification;
        return verification;
    }

    private async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("GenshinPiano-OcrAddon/3.0");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
    }

    private sealed record Verification(string Checksum, string Signature, DateTimeOffset CheckedAt);
}
