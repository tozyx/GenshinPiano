namespace GenshinPiano.Infrastructure.Ocr;

using GenshinPiano.Application.Ocr;

internal sealed record OcrAddonManifest
{
    public int SchemaVersion { get; init; }

    public int ProtocolVersion { get; init; }

    public string EngineVersion { get; init; } = string.Empty;

    public string Executable { get; init; } = string.Empty;

    public OcrAddonLaunchMode LaunchMode { get; init; } = OcrAddonLaunchMode.Stdio;
}
