using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using GenshinPiano.Application.Ocr;

namespace GenshinPiano.Ocr.Engine;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<int> Main(string[] args)
    {
        WindowsProcessIdentity.Configure();

        if (args.Contains("--launch-worker", StringComparer.Ordinal))
        {
            return LaunchBackgroundWorker(args);
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
            "This executable is an add-on and must be started with --stdio or file IPC arguments.");
        return 2;
    }

    private static int LaunchBackgroundWorker(string[] args)
    {
        var workerArguments = new List<string>(args.Length);
        string? workerPidPath = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--launch-worker":
                    break;
                case "--worker-pid" when index + 1 < args.Length:
                    workerPidPath = args[++index];
                    break;
                default:
                    workerArguments.Add(args[index]);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(workerPidPath) ||
            string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return 2;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            foreach (var argument in workerArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var worker = Process.Start(startInfo) ??
                               throw new InvalidOperationException("The OCR worker could not be started.");
            var pidPath = Path.GetFullPath(workerPidPath);
            Directory.CreateDirectory(Path.GetDirectoryName(pidPath)!);
            File.WriteAllText(
                pidPath,
                worker.Id.ToString(CultureInfo.InvariantCulture),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return 0;
        }
        catch
        {
            return 1;
        }
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
            var result = await AnalyzeAsync(request);
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

    private static async Task<OcrAnalysisResult> AnalyzeAsync(OcrAnalysisRequest request)
    {
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
