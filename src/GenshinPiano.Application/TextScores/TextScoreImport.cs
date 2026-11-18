using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.TextScores;

public enum TextScoreProfile { Unspecified, Zhijian, Xingli, Yujun, Touming }
public enum TextScoreNotation { Auto, Keys, Numbers }
public enum TextScoreTrailingSlash { Auto, Separator, Closing }

public sealed record TextScoreSectionOptions
{
    public int Index { get; init; }
    public int GroupDenominator { get; init; } = 4;
    public int Numerator { get; init; } = 4;
    public int Denominator { get; init; } = 4;
    public int GroupsPerRow { get; init; }
    public TextScoreTrailingSlash TrailingSlash { get; init; }
}

public sealed record TextScoreImportOptions
{
    public TextScoreProfile Profile { get; init; }
    public TextScoreNotation Notation { get; init; }
    public string Title { get; init; } = "Text score";
    public double Bpm { get; init; } = 120;
    public bool SpacesAreRests { get; init; }
    public IReadOnlyList<TextScoreSectionOptions> Sections { get; init; } = [];
}

public sealed record TextScoreSectionInfo(int Index, string Name, int FirstLine, int RowCount, int SuggestedGroupsPerRow);
public sealed record TextScoreAnalysis(TextScoreNotation Notation, IReadOnlyList<TextScoreSectionInfo> Sections);
public sealed record TextScoreDetection(TextScoreNotation Notation, IReadOnlyList<TextScoreProfile> Candidates,
    TextScoreProfile? CertainProfile, TextScoreProfile? SuggestedProfile, string EvidenceCode);
public sealed record TextScoreWarning(string Code, int Line = 0, string Detail = "");
public sealed record TextScoreImportResult(ScoreDocument Score, int AttackCount, long EndTick,
    IReadOnlyList<TextScoreWarning> Warnings);

public sealed class TextScoreImportException(string code, int line = 0, int column = 0, string detail = "")
    : Exception($"{code} ({line}:{column}) {detail}")
{
    public string Code { get; } = code;
    public int Line { get; } = line;
    public int Column { get; } = column;
    public string Detail { get; } = detail;
}

public interface ITextScoreImporter
{
    TextScoreDetection Detect(string text, TextScoreNotation notation = TextScoreNotation.Auto);
    TextScoreAnalysis Analyze(string text, TextScoreProfile profile, TextScoreNotation notation = TextScoreNotation.Auto);
    TextScoreImportResult Parse(string text, TextScoreImportOptions options);
}

public static class TextScoreImportLimits
{
    public const int MaxTextLength = 500_000;
    public const int MaxRows = 10_000;
    public const int MaxSections = 128;
    public const int MaxNotes = 100_000;
    public const int MaxGroups = 100_000;
}
