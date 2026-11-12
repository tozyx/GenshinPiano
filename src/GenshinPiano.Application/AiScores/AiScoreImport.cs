using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.AiScores;

public interface IAiScoreImporter
{
    AiScoreImportResult Parse(string json);
}

public sealed record AiScoreImportResult(
    ScoreDocument Score,
    int MeasureCount,
    IReadOnlyList<AiScoreImportWarning> Warnings);

public sealed record AiScoreImportWarning(string Code, string Location, string Detail = "");

public sealed class AiScoreImportException(string code, string location) : Exception($"{location}: {code}")
{
    public string Code { get; } = code;
    public string Location { get; } = location;
}

public static class AiScoreImportLimits
{
    public const int MaxTextLength = 2_000_000;
    public const int MaxMeasures = 10_000;
    public const int MaxNotes = 100_000;
    public const int MaxVoices = 64;
}
