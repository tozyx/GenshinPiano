namespace GenshinPiano.Application.Updates;

public sealed class RacingUpdateSource(IReadOnlyList<IUpdateSource> sources) : IUpdateSource
{
    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            return null;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = sources
            .Select(source => TryGetLatestAsync(source, channel, linkedCancellation.Token))
            .ToList();
        var errors = new List<Exception>();
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var attempt = await completed;
            if (attempt.Manifest is not null)
            {
                linkedCancellation.Cancel();
                return attempt.Manifest;
            }
            if (attempt.Error is not null)
            {
                errors.Add(attempt.Error);
            }
        }

        if (errors.Count == sources.Count)
        {
            throw new AggregateException("All update sources failed.", errors);
        }
        return null;
    }

    private static async Task<SourceAttempt> TryGetLatestAsync(
        IUpdateSource source,
        string channel,
        CancellationToken cancellationToken)
    {
        try
        {
            return new SourceAttempt(
                await source.GetLatestAsync(channel, cancellationToken),
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SourceAttempt(null, null);
        }
        catch (Exception exception)
        {
            return new SourceAttempt(null, exception);
        }
    }

    private sealed record SourceAttempt(UpdateManifest? Manifest, Exception? Error);
}
