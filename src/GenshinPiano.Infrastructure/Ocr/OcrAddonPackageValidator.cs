using System.Text.Json;
using System.Text.Json.Serialization;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.Infrastructure.Ocr;

public static class OcrAddonPackageValidator
{
    private static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web)
    {
        Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)},
    };

    public static OcrAddonDescriptor Validate(string directory,string? expectedVersion=null)
    {
        directory=Path.GetFullPath(directory);
        OcrAddonManifest manifest;
        try
        {
            manifest=JsonSerializer.Deserialize<OcrAddonManifest>(File.ReadAllText(Path.Combine(directory,"manifest.json")),Options)
                ?? throw new InvalidDataException("OCR manifest is empty.");
        }
        catch (JsonException error) { throw new InvalidDataException("OCR manifest is invalid.",error); }
        if (manifest.SchemaVersion!=1 || manifest.ProtocolVersion!=OcrProtocol.CurrentVersion || !Enum.IsDefined(manifest.LaunchMode))
            throw new InvalidDataException("The OCR add-on protocol or launch mode is incompatible.");
        if (!SemanticVersion.TryParse(manifest.EngineVersion,out var version)) throw new InvalidDataException("OCR engine version is invalid.");
        if (expectedVersion is not null && (!SemanticVersion.TryParse(expectedVersion,out var expected) || version.CompareTo(expected)!=0))
            throw new InvalidDataException("OCR package version does not match its signed release metadata.");
        var executable=ResolveFile(directory,manifest.Executable);
        if (manifest.RequiredFiles is null) throw new InvalidDataException("OCR requiredFiles must be an array.");
        if (manifest.StaffBackend=="smt" && manifest.RequiredFiles.Length==0)
            throw new InvalidDataException("The SMT package does not declare its required runtime files.");
        foreach (var name in manifest.RequiredFiles)
        {
            var file=ResolveFile(directory,name);
            if (new FileInfo(file).Length==0) throw new InvalidDataException($"OCR runtime file is empty: {name}");
        }
        return new(manifest.EngineVersion,manifest.ProtocolVersion,executable,manifest.LaunchMode);
    }

    private static string ResolveFile(string root,string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid OCR runtime path.");
        var full=Path.GetFullPath(Path.Combine(root,relative));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full)) throw new InvalidDataException($"OCR runtime file is missing or outside the package: {relative}");
        return full;
    }
}
