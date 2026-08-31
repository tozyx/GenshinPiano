using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Ocr;

public static class OcrProtocol
{
    public const int CurrentVersion = 1;
}

public enum OcrNotationHint
{
    Auto,
    Numbered,
    Staff,
}

public enum OcrWatermarkMode
{
    Auto,
    Strong,
    Off,
}

public enum OcrProgressStage
{
    Preparing,
    WatermarkSuppression,
    TextDetection,
    SuperResolution,
    ScoreReconstruction,
}

public sealed record OcrProgressUpdate(OcrProgressStage Stage, double Progress);

public sealed record OcrAnalysisRequest(
    int ProtocolVersion,
    string ImagePath,
    OcrNotationHint NotationHint,
    string Language,
    OcrWatermarkMode WatermarkMode = OcrWatermarkMode.Auto,
    bool IncludeAccompaniment = true);

public sealed record OcrAnalysisResult(
    int ProtocolVersion,
    bool Success,
    ScoreDocument? Score,
    double Confidence = 0,
    IReadOnlyList<string>? Warnings = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record OcrAddonDescriptor(
    string EngineVersion,
    int ProtocolVersion,
    string ExecutablePath);

public interface IOcrAddonService
{
    OcrAddonDescriptor? FindInstalledAddon();

    Task<OcrAnalysisResult> AnalyzeAsync(
        string imagePath,
        OcrNotationHint notationHint,
        string language,
        OcrWatermarkMode watermarkMode = OcrWatermarkMode.Auto,
        bool includeAccompaniment = true,
        IProgress<OcrProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);
}
