using System.Diagnostics;

namespace GenshinPiano.Application.Updates;

public sealed class RacingUpdateSource(
    IReadOnlyList<IUpdateSource> sources,
    TimeSpan? gracePeriod = null,
    Action<string>? diagnostic = null) : IUpdateSource
{
    private readonly TimeSpan _gracePeriod = gracePeriod ?? TimeSpan.FromMilliseconds(750);

    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0) return null;

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = sources.Select((source, index) =>
            TryGetLatestAsync(
                source,
                GetSourceName(source, index),
                channel,
                linkedCancellation.Token)).ToList();
        var errors = new List<Exception>();
        var candidates = new List<SourceAttempt>();

        while (pending.Count > 0 && candidates.Count == 0)
        {
            var attempt = await TakeNextAsync(pending);
            LogAttempt(attempt);
            Collect(attempt, candidates, errors);
        }

        if (candidates.Count == 0)
        {
            if (errors.Count == sources.Count)
                throw new AggregateException("All update sources failed.", errors);
            return null;
        }

        if (pending.Count > 0 && _gracePeriod > TimeSpan.Zero)
        {
            var graceDelay = Task.Delay(_gracePeriod, cancellationToken);
            while (pending.Count > 0)
            {
                var nextAttempt = Task.WhenAny(pending);
                var completed = await Task.WhenAny(nextAttempt, graceDelay);
                if (ReferenceEquals(completed, graceDelay))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    diagnostic?.Invoke(
                        $"Update race grace window expired with {pending.Count} " +
                        "source(s) still pending; using completed candidates.");
                    break;
                }

                var task = await nextAttempt;
                pending.Remove(task);
                var attempt = await task;
                LogAttempt(attempt);
                Collect(attempt, candidates, errors);
            }
        }

        var selected = candidates
            .OrderByDescending(attempt => attempt.Manifest!.Version)
            .ThenBy(attempt => attempt.Elapsed)
            .First();
        diagnostic?.Invoke(
            $"Update race selected {selected.SourceName} {selected.Manifest!.Version} " +
            $"after {selected.Elapsed.TotalMilliseconds:F0} ms; " +
            $"grace window {_gracePeriod.TotalMilliseconds:F0} ms.");
        linkedCancellation.Cancel();
        return selected.Manifest;
    }

    private static async Task<SourceAttempt> TakeNextAsync(List<Task<SourceAttempt>> pending)
    {
        var completed = await Task.WhenAny(pending);
        pending.Remove(completed);
        return await completed;
    }

    private static void Collect(
        SourceAttempt attempt,
        ICollection<SourceAttempt> candidates,
        ICollection<Exception> errors)
    {
        if (attempt.Manifest is not null) candidates.Add(attempt);
        else if (attempt.Error is not null) errors.Add(attempt.Error);
    }

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
            return new SourceAttempt(sourceName, null, null, stopwatch.Elapsed);
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
