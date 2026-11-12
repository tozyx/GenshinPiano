namespace GenshinPiano.Infrastructure.Ocr;

using GenshinPiano.Application.Ocr;

internal sealed record OcrAddonManifest
{
    public int SchemaVersion { get; init; }

    public int ProtocolVersion { get; init; }

    public string EngineVersion { get; init; } = string.Empty;

    public string ResourceVersion { get; init; } = string.Empty;

    public int EngineCompatibilityVersion { get; init; }

    public string SmtModelSha256 { get; init; } = string.Empty;

    public string Executable { get; init; } = string.Empty;

    public OcrAddonLaunchMode LaunchMode { get; init; } = OcrAddonLaunchMode.Stdio;

    public string StaffBackend { get; init; } = string.Empty;

    public string[] RequiredFiles { get; init; } = [];
}
