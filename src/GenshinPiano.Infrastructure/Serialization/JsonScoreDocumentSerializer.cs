using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.Serialization;

public sealed class JsonScoreDocumentSerializer : IScoreDocumentSerializer
{
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
        var score = await JsonSerializer.DeserializeAsync<ScoreDocument>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (score is null)
        {
            throw new InvalidDataException("曲谱文件为空或不是有效的 JSON。");
        }

        EnsureValid(score);
        return score;
    }

    public async Task SaveAsync(
        ScoreDocument score,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(score);
        EnsureValid(score);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(score, SerializerOptions);
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
