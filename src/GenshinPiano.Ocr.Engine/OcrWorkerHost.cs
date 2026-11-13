using System.Text.Json;
using System.Text.Json.Serialization;
using GenshinPiano.Application.Ocr;

namespace GenshinPiano.Ocr.Engine;

public static class OcrWorkerHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static string? _resourceRoot;

    public static async Task<int> RunAsync(string[] args)
    {
        WindowsProcessIdentity.Configure();

        var resourceIndex = Array.IndexOf(args, "--resources");
        if (resourceIndex >= 0 && resourceIndex + 1 < args.Length)
        {
            _resourceRoot = args[resourceIndex + 1];
            args = args.Take(resourceIndex).Concat(args.Skip(resourceIndex + 2)).ToArray();
        }

        if (args.Contains("--stdio", StringComparer.Ordinal))
        {
            return await RunStdioAsync();
        }

        if (TryParseFileMode(args, out var fileMode))
        {
            return await RunFileModeAsync(fileMode);
        }

        Console.Error.WriteLine(
            "OCR worker mode requires --resources and --stdio or file IPC arguments.");
        return 2;
    }

    private static async Task<int> RunStdioAsync()
    {
        try
        {
            var input = await Console.In.ReadToEndAsync();
            var request = DeserializeRequest(input);
            var result = await AnalyzeAsync(request);
            await Console.Out.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
            return result.Success ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            var result = CreateFailureResult(exception);
            await Console.Out.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
            return 1;
        }
    }

    private static async Task<int> RunFileModeAsync(FileModeArguments options)
    {
        OcrProgressReporter.WriteToFile(options.ProgressPath);
        try
        {
            var input = await File.ReadAllTextAsync(options.RequestPath);
            var request = DeserializeRequest(input);
            var result = await AnalyzeAsync(request, Path.GetDirectoryName(options.ResponsePath));
            await WriteJsonAsync(options.ResponsePath, result);
            return result.Success ? 0 : 1;
        }
        catch (Exception exception)
        {
            await WriteDiagnosticsAsync(options.DiagnosticsPath, exception);
            await WriteJsonAsync(options.ResponsePath, CreateFailureResult(exception));
            return 1;
        }
    }

    private static OcrAnalysisRequest DeserializeRequest(string input)
    {
        var request = JsonSerializer.Deserialize<OcrAnalysisRequest>(input, JsonOptions) ??
                      throw new InvalidDataException("The OCR request is empty.");
        if (request.ProtocolVersion != OcrProtocol.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported OCR protocol {request.ProtocolVersion}.");
        }

        return request;
    }

    private static async Task<OcrAnalysisResult> AnalyzeAsync(OcrAnalysisRequest request, string? workDirectory = null)
    {
        OcrResourceEnvironment.Configure(_resourceRoot ?? throw new InvalidDataException("The OCR resource directory was not specified."), workDirectory);
        var recognizer = new JianpuRecognizer();
        return await recognizer.RecognizeAsync(request, CancellationToken.None);
    }

    private static OcrAnalysisResult CreateFailureResult(Exception exception) =>
        new(
            OcrProtocol.CurrentVersion,
            Success: false,
            Score: null,
            ErrorCode: "engine_failure",
            ErrorMessage: exception.Message);

    private static async Task WriteJsonAsync(string path, OcrAnalysisResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(result, JsonOptions));
    }

    private static async Task WriteDiagnosticsAsync(string path, Exception exception)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, exception.ToString());
    }

    private static bool TryParseFileMode(string[] args, out FileModeArguments options)
    {
        options = default!;
        string? requestPath = null;
        string? responsePath = null;
        string? progressPath = null;
        string? diagnosticsPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length)
            {
                return false;
            }

            switch (args[i])
            {
                case "--request":
                    requestPath = args[++i];
                    break;
                case "--response":
                    responsePath = args[++i];
                    break;
                case "--progress":
                    progressPath = args[++i];
                    break;
                case "--diagnostics":
                    diagnosticsPath = args[++i];
                    break;
                default:
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(requestPath) ||
            string.IsNullOrWhiteSpace(responsePath) ||
            string.IsNullOrWhiteSpace(progressPath) ||
            string.IsNullOrWhiteSpace(diagnosticsPath))
        {
            return false;
        }

        options = new FileModeArguments(
            Path.GetFullPath(requestPath),
            Path.GetFullPath(responsePath),
            Path.GetFullPath(progressPath),
            Path.GetFullPath(diagnosticsPath));
        return true;
    }

    private sealed record FileModeArguments(
        string RequestPath,
        string ResponsePath,
        string ProgressPath,
        string DiagnosticsPath);
}
