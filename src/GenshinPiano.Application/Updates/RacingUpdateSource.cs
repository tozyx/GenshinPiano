using System.Diagnostics;

namespace GenshinPiano.Application.Updates;

public sealed class RacingUpdateSource(
    IReadOnlyList<IUpdateSource> sources,
    TimeSpan? gracePeriod = null,
    Action<string>? diagnostic = null) : IUpdateSource
{
    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        // Kept in the constructor for source compatibility. Metadata discovery now
        // waits for every source, because an early response may expose an older release.
        _ = gracePeriod;
        if (sources.Count == 0) return null;

        var tasks = sources.Select((source, index) =>
            TryGetLatestAsync(
                source,
                GetSourceName(source, index),
                channel,
                cancellationToken)).ToArray();
        var attempts = await Task.WhenAll(tasks);
        foreach (var attempt in attempts) LogAttempt(attempt);

        var errors = attempts.Where(attempt => attempt.Error is not null)
            .Select(attempt => attempt.Error!).ToArray();
        var candidates = attempts.Where(attempt => attempt.Manifest is not null).ToArray();
        if (candidates.Length == 0)
        {
            if (errors.Length == sources.Count)
                throw new AggregateException("All update sources failed.", errors);
            return null;
        }

        var newestVersion = candidates.Max(attempt => attempt.Manifest!.Version);
        var newest = candidates
            .Where(attempt => attempt.Manifest!.Version.CompareTo(newestVersion) == 0)
            .OrderBy(attempt => attempt.Elapsed)
            .ToArray();
        var selected = newest[0];
        var manifest = MergeEquivalentMirrors(selected, newest);
        var downloadUrlCount = manifest.Packages
            .SelectMany(package => package.GetDownloadUris()).Count();
        diagnostic?.Invoke(
            newest.Length > 1
                ? $"Update discovery found {newestVersion} on {newest.Length} source(s); " +
                  $"{downloadUrlCount} equivalent package URL(s) are eligible for download racing."
                : $"Update discovery selected {selected.SourceName} {newestVersion}; " +
                  "other sources returned an older version, no compatible update, or an error.");
        return manifest;
    }

    private static UpdateManifest MergeEquivalentMirrors(
        SourceAttempt selected,
        IReadOnlyList<SourceAttempt> newest)
    {
        var primary = selected.Manifest!;
        if (newest.Count == 1) return primary;

        var mergedPackages = primary.Packages.Select(package =>
        {
            var mirrors = newest.Skip(1)
                .SelectMany(attempt => attempt.Manifest!.Packages)
                .Where(candidate => PackagesAreEquivalent(package, candidate))
                .SelectMany(candidate => candidate.GetDownloadUris())
                .ToArray();
            return mirrors.Length == 0
                ? package
                : package with { MirrorDownloadUris = mirrors };
        }).ToArray();
        return primary with
        {
            Packages = mergedPackages,
            SourceName = string.Join(" + ", newest.Select(attempt => attempt.SourceName)),
        };
    }

    private static bool PackagesAreEquivalent(UpdatePackage left, UpdatePackage right) =>
        left.Id == right.Id &&
        left.Kind == right.Kind &&
        left.Version.CompareTo(right.Version) == 0 &&
        string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Signature, right.Signature, StringComparison.Ordinal);

    private void LogAttempt(SourceAttempt attempt)
    {
        var result = attempt.Manifest is not null
            ? $"version {attempt.Manifest.Version}"
            : attempt.Error is not null
                ? $"failed: {attempt.Error.Message}"
                : "no compatible update";
        diagnostic?.Invoke(
            $"Update source {attempt.SourceName} responded in " +
            $"{attempt.Elapsed.TotalMilliseconds:F0} ms: {result}.");
    }

    private static string GetSourceName(IUpdateSource source, int index) =>
        source is INamedUpdateSource named
            ? named.SourceName
            : $"{source.GetType().Name}#{index + 1}";

    private static async Task<SourceAttempt> TryGetLatestAsync(
        IUpdateSource source,
        string sourceName,
        string channel,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var manifest = await source.GetLatestAsync(channel, cancellationToken);
            return new SourceAttempt(sourceName, manifest, null, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SourceAttempt(sourceName, null, exception, stopwatch.Elapsed);
        }
    }

    private sealed record SourceAttempt(
        string SourceName,
        UpdateManifest? Manifest,
        Exception? Error,
        TimeSpan Elapsed);
}
