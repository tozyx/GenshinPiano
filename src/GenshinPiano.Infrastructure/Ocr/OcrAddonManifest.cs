namespace GenshinPiano.Infrastructure.Ocr;

internal sealed record OcrAddonManifest
{
    public int SchemaVersion { get; init; }

    public int ProtocolVersion { get; init; }

    public string EngineVersion { get; init; } = string.Empty;

    public string Executable { get; init; } = string.Empty;
}
