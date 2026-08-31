using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.Ocr;

public sealed class ExternalOcrAddonService : IOcrAddonService
{
    private const int ManifestSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _addonDirectory;
    private readonly TimeSpan _timeout;

    public ExternalOcrAddonService(string addonDirectory, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonDirectory);
        _addonDirectory = Path.GetFullPath(addonDirectory);
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public OcrAddonDescriptor? FindInstalledAddon()
    {
        var manifestPath = Path.Combine(_addonDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        OcrAddonManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<OcrAddonManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (manifest is not
            {
                SchemaVersion: ManifestSchemaVersion,
                ProtocolVersion: OcrProtocol.CurrentVersion,
            } ||
            string.IsNullOrWhiteSpace(manifest.EngineVersion) ||
            string.IsNullOrWhiteSpace(manifest.Executable))
        {
            return null;
        }

        var executablePath = Path.GetFullPath(
            Path.Combine(_addonDirectory, manifest.Executable));
        var directoryPrefix = _addonDirectory.TrimEnd(Path.DirectorySeparatorChar) +
                              Path.DirectorySeparatorChar;
        if (!executablePath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(executablePath))
        {
            return null;
        }

        return new OcrAddonDescriptor(
            manifest.EngineVersion,
            manifest.ProtocolVersion,
            executablePath);
    }

    public async Task<OcrAnalysisResult> AnalyzeAsync(
        string imagePath,
        OcrNotationHint notationHint,
        string language,
        OcrWatermarkMode watermarkMode = OcrWatermarkMode.Auto,
        bool includeAccompaniment = true,
        IProgress<OcrProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("The OCR source image was not found.", imagePath);
        }

        var addon = FindInstalledAddon() ??
                    throw new InvalidOperationException("A compatible OCR add-on is not installed.");
        var request = new OcrAnalysisRequest(
            OcrProtocol.CurrentVersion,
            Path.GetFullPath(imagePath),
            notationHint,
            string.IsNullOrWhiteSpace(language) ? "auto" : language,
            watermarkMode,
            includeAccompaniment);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = addon.ExecutablePath,
                Arguments = "--stdio",
                WorkingDirectory = _addonDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("The OCR add-on process could not be started.");
        }

        progress?.Report(new OcrProgressUpdate(OcrProgressStage.Preparing, 0));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await process.StandardInput.WriteLineAsync(
                JsonSerializer.Serialize(request, JsonOptions).AsMemory(),
                timeout.Token);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = ReadErrorAsync(process, progress, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"OCR add-on exited with code {process.ExitCode}: {error.Trim()}");
            }

            var result = JsonSerializer.Deserialize<OcrAnalysisResult>(output, JsonOptions) ??
                         throw new InvalidDataException("The OCR add-on returned an empty response.");
            if (result.ProtocolVersion != OcrProtocol.CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported OCR response protocol {result.ProtocolVersion}.");
            }

            if (result.Success && result.Score is null)
            {
                throw new InvalidDataException("The OCR add-on reported success without a score.");
            }

            if (result.Confidence is < 0 or > 1)
            {
                throw new InvalidDataException("OCR confidence must be between 0 and 1.");
            }

            if (result.Success)
            {
                var validationErrors = ScoreValidator.Validate(result.Score!);
                if (validationErrors.Count > 0)
                {
                    throw new InvalidDataException(
                        $"The OCR add-on returned an invalid score: {string.Join("; ", validationErrors)}");
                }
            }

            progress?.Report(new OcrProgressUpdate(OcrProgressStage.ScoreReconstruction, 1));
            return result;
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }

    private static async Task<string> ReadErrorAsync(
        Process process,
        IProgress<OcrProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var diagnostics = new System.Text.StringBuilder();
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            if (TryParseProgress(line, out var update))
            {
                progress?.Report(update);
            }
            else
            {
                diagnostics.AppendLine(line);
            }
        }

        return diagnostics.ToString();
    }

    private static bool TryParseProgress(string line, out OcrProgressUpdate update)
    {
        update = default!;
        const string prefix = "OCR_PROGRESS|";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = line[prefix.Length..].Split('|');
        if (parts.Length != 2 ||
            !Enum.TryParse(parts[0], ignoreCase: true, out OcrProgressStage stage) ||
            !double.TryParse(
                parts[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            return false;
        }

        update = new OcrProgressUpdate(stage, Math.Clamp(value, 0, 1));
        return true;
    }
}
