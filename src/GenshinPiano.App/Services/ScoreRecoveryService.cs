using System.IO;
using System.Text.Json;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Services;

public sealed record ScoreRecoverySnapshot(
    ScoreDocument Score,
    string? OriginalPath,
    DateTimeOffset SavedAt);

public sealed class ScoreRecoveryService(IScoreDocumentSerializer serializer)
{
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "data", "recovery");

    private string ScorePath => Path.Combine(_directory, "autosave.gpiano");

    private string MetadataPath => Path.Combine(_directory, "autosave.json");

    public bool HasRecovery => File.Exists(ScorePath);

    public async Task SaveAsync(
        ScoreDocument score,
        string? originalPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var temporaryScorePath = ScorePath + ".tmp";
        var temporaryMetadataPath = MetadataPath + ".tmp";

        await serializer.SaveAsync(score, temporaryScorePath, cancellationToken);
        var metadata = new RecoveryMetadata(originalPath, DateTimeOffset.Now);
        await File.WriteAllTextAsync(
            temporaryMetadataPath,
            JsonSerializer.Serialize(metadata, MetadataOptions),
            cancellationToken);

        File.Move(temporaryScorePath, ScorePath, true);
        File.Move(temporaryMetadataPath, MetadataPath, true);
    }

    public async Task<ScoreRecoverySnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!HasRecovery)
        {
            return null;
        }

        var score = await serializer.LoadAsync(ScorePath, cancellationToken);
        RecoveryMetadata? metadata = null;
        if (File.Exists(MetadataPath))
        {
            var json = await File.ReadAllTextAsync(MetadataPath, cancellationToken);
            metadata = JsonSerializer.Deserialize<RecoveryMetadata>(json, MetadataOptions);
        }

        return new ScoreRecoverySnapshot(
            score,
            metadata?.OriginalPath,
            metadata?.SavedAt ?? File.GetLastWriteTime(ScorePath));
    }

    public void Discard()
    {
        TryDelete(ScorePath);
        TryDelete(MetadataPath);
        TryDelete(ScorePath + ".tmp");
        TryDelete(MetadataPath + ".tmp");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            AppLogger.Warning($"Could not delete recovery file '{path}': {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLogger.Warning($"Could not delete recovery file '{path}': {exception.Message}");
        }
    }

    private sealed record RecoveryMetadata(string? OriginalPath, DateTimeOffset SavedAt);
}
