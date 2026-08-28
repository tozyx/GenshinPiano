using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.Serialization;

public sealed class JsonScoreDocumentSerializer(
    ScoreSchemaMigrator? schemaMigrator = null) : IScoreDocumentSerializer
{
    private readonly ScoreSchemaMigrator _schemaMigrator = schemaMigrator ?? new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<ScoreDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        JsonNode? json;
        try
        {
            json = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("曲谱文件不是有效的 JSON。", exception);
        }

        var migrated = _schemaMigrator.MigrateToCurrent(json);
        ScoreDocument? score;
        try
        {
            score = migrated.Deserialize<ScoreDocument>(SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("曲谱 JSON 包含无法识别的数据。", exception);
        }

        if (score is null)
        {
            throw new InvalidDataException("曲谱文件为空或不是有效的 JSON。");
        }

        EnsureValid(score);
        return NoteDurationCalculator.ApplyAutoDurations(score);
    }

    public async Task SaveAsync(
        ScoreDocument score,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        var currentScore = score.SchemaVersion == ScoreDocument.CurrentSchemaVersion
            ? score
            : score with { SchemaVersion = ScoreDocument.CurrentSchemaVersion };
        var materializedScore = NoteDurationCalculator.ApplyAutoDurations(currentScore);
        EnsureValid(materializedScore);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(materializedScore, SerializerOptions);
        await File.WriteAllTextAsync(
            fullPath,
            json + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }

    private static void EnsureValid(ScoreDocument score)
    {
        var errors = ScoreValidator.Validate(score);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
    }
}
