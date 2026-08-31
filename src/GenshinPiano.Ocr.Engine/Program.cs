using System.Text.Json;
using System.Text.Json.Serialization;
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

        if (!args.Contains("--stdio", StringComparer.Ordinal))
        {
            Console.Error.WriteLine("This executable is an add-on and must be started with --stdio.");
            return 2;
        }

        try
        {
            var input = await Console.In.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<OcrAnalysisRequest>(input, JsonOptions) ??
                          throw new InvalidDataException("The OCR request is empty.");
            if (request.ProtocolVersion != OcrProtocol.CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported OCR protocol {request.ProtocolVersion}.");
            }

            var recognizer = new JianpuRecognizer();
            var result = await recognizer.RecognizeAsync(request, CancellationToken.None);
            await Console.Out.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
            return result.Success ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            var result = new OcrAnalysisResult(
                OcrProtocol.CurrentVersion,
                Success: false,
                Score: null,
                ErrorCode: "engine_failure",
                ErrorMessage: exception.Message);
            await Console.Out.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
            return 1;
        }
    }
}
