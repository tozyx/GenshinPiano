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
        var resources = manifest.SchemaVersion == OcrResourceContract.SchemaVersion;
        if ((!resources && manifest.SchemaVersion!=1) || manifest.ProtocolVersion!=OcrProtocol.CurrentVersion || !Enum.IsDefined(manifest.LaunchMode))
            throw new InvalidDataException("The OCR add-on protocol or launch mode is incompatible.");
        var componentVersion = resources ? manifest.ResourceVersion : manifest.EngineVersion;
        if (!SemanticVersion.TryParse(componentVersion,out var version)) throw new InvalidDataException("OCR component version is invalid.");
        if (expectedVersion is not null && (!SemanticVersion.TryParse(expectedVersion,out var expected) || version.CompareTo(expected)!=0))
            throw new InvalidDataException("OCR package version does not match its signed release metadata.");
        var executable = resources ? string.Empty : ResolveFile(directory,manifest.Executable);
        if (resources && (manifest.EngineCompatibilityVersion != OcrResourceContract.EngineCompatibilityVersion ||
            manifest.StaffBackend != "smt" || manifest.SmtModelSha256.Length != 64 || !manifest.SmtModelSha256.All(Uri.IsHexDigit)))
            throw new InvalidDataException("The OCR resources are not compatible with this engine.");
        if (manifest.RequiredFiles is null) throw new InvalidDataException("OCR requiredFiles must be an array.");
        if (manifest.StaffBackend=="smt" && manifest.RequiredFiles.Length==0)
            throw new InvalidDataException("The SMT package does not declare its required runtime files.");
        foreach (var name in manifest.RequiredFiles)
        {
            var file=ResolveFile(directory,name);
            if (new FileInfo(file).Length==0) throw new InvalidDataException($"OCR runtime file is empty: {name}");
        }
        if (resources)
        {
            // The host defines the minimum complete resource set, not the downloaded manifest.
            foreach (var name in RequiredResources)
            {
                var file = ResolveFile(directory, name);
                if (new FileInfo(file).Length == 0) throw new InvalidDataException($"OCR resource is empty: {name}");
            }
        }
        return new(componentVersion,manifest.ProtocolVersion,executable,
            resources ? OcrAddonLaunchMode.File : manifest.LaunchMode, resources, resources);
    }

    public static readonly IReadOnlyList<string> RequiredResources = Array.AsReadOnly(new[]
    {
        "native/onnxruntime.dll", "native/onnxruntime_providers_shared.dll", "native/libSkiaSharp.dll",
        "models/v5/ch_PP-OCRv5_mobile_det.onnx", "models/v5/ch_ppocr_mobile_v2.0_cls_infer.onnx",
        "models/v5/latin_PP-OCRv5_rec_mobile_infer.onnx", "models/v5/ppocrv5_latin_dict.txt",
        "models/orpheusnet/orpheusnet-srcnn-x3.onnx", "models/orpheusnet/orpheusnet-middle.onnx", "models/orpheusnet/orpheusnet-metadata.onnx",
        "staff-omr/smt/crispembed.exe", "staff-omr/smt/crispembed.dll", "staff-omr/smt/ggml.dll",
        "staff-omr/smt/ggml-base.dll", "staff-omr/smt/ggml-cpu.dll", "staff-omr/smt/msvcp140.dll",
        "staff-omr/smt/vcruntime140.dll", "staff-omr/smt/vcruntime140_1.dll", "staff-omr/smt/smt-grandstaff-q8_0.gguf",
    });

    private static string ResolveFile(string root,string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid OCR runtime path.");
        var full=Path.GetFullPath(Path.Combine(root,relative));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full)) throw new InvalidDataException($"OCR runtime file is missing or outside the package: {relative}");
        return full;
    }
}
