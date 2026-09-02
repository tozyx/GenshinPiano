using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.Ocr;

public sealed class ExternalOcrAddonService : IOcrAddonService
{
    private const int ManifestSchemaVersion = 1;
    private const int ProgressPollMilliseconds = 100;
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
        _timeout = timeout ?? TimeSpan.FromMinutes(30);
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
            string.IsNullOrWhiteSpace(manifest.Executable) ||
            !Enum.IsDefined(manifest.LaunchMode))
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
            executablePath,
            manifest.LaunchMode);
    }

    public async Task<OcrAnalysisResult> AnalyzeAsync(
        string imagePath,
        OcrNotationHint notationHint,
        string language,
        OcrWatermarkMode watermarkMode = OcrWatermarkMode.Auto,
        bool includeAccompaniment = true,
        bool preferGpuAcceleration = true,
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
            includeAccompaniment,
            preferGpuAcceleration);

        var result = addon.LaunchMode switch
        {
            OcrAddonLaunchMode.File => await AnalyzeWithFileIpcAsync(
                addon,
                request,
                progress,
                cancellationToken),
            _ => await AnalyzeWithStdioAsync(
                addon,
                request,
                progress,
                cancellationToken),
        };

        ValidateResult(result);
        progress?.Report(new OcrProgressUpdate(OcrProgressStage.ScoreReconstruction, 1));
        return result;
    }

    private async Task<OcrAnalysisResult> AnalyzeWithStdioAsync(
        OcrAddonDescriptor addon,
        OcrAnalysisRequest request,
        IProgress<OcrProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
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

            return DeserializeResult(output);
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

    private async Task<OcrAnalysisResult> AnalyzeWithFileIpcAsync(
        OcrAddonDescriptor addon,
        OcrAnalysisRequest request,
        IProgress<OcrProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"GenshinPiano-Ocr-{Guid.NewGuid():N}");
        var requestPath = Path.Combine(tempDirectory, "request.json");
        var responsePath = Path.Combine(tempDirectory, "response.json");
        var progressPath = Path.Combine(tempDirectory, "progress.log");
        var diagnosticsPath = Path.Combine(tempDirectory, "diagnostics.log");
        var workerPidPath = Path.Combine(tempDirectory, "worker.pid");

        Process? launcher = null;
        Process? process = null;
        try
        {
            Directory.CreateDirectory(tempDirectory);
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = addon.ExecutablePath,
                WorkingDirectory = _addonDirectory,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false,
            };
            startInfo.ArgumentList.Add("--launch-worker");
            startInfo.ArgumentList.Add("--worker-pid");
            startInfo.ArgumentList.Add(workerPidPath);
            startInfo.ArgumentList.Add("--request");
            startInfo.ArgumentList.Add(requestPath);
            startInfo.ArgumentList.Add("--response");
            startInfo.ArgumentList.Add(responsePath);
            startInfo.ArgumentList.Add("--progress");
            startInfo.ArgumentList.Add(progressPath);
            startInfo.ArgumentList.Add("--diagnostics");
            startInfo.ArgumentList.Add(diagnosticsPath);

            progress?.Report(new OcrProgressUpdate(OcrProgressStage.Preparing, 0));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);

            launcher = Process.Start(startInfo) ??
                       throw new InvalidOperationException("The OCR add-on launcher could not be started.");
            await launcher.WaitForExitAsync(timeout.Token);
            if (launcher.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The OCR add-on launcher exited with code {launcher.ExitCode}.");
            }

            var workerPid = await ReadWorkerPidAsync(workerPidPath, timeout.Token);
            try
            {
                process = Process.GetProcessById(workerPid);
            }
            catch (ArgumentException)
            {
                // A very small request can finish before the host obtains its
                // Process object. The response file is authoritative below.
                process = null;
            }

            var progressOffset = 0L;
            var waitTask = process?.WaitForExitAsync(timeout.Token) ?? Task.CompletedTask;
            while (!waitTask.IsCompleted)
            {
                progressOffset = await ReadProgressFileAsync(
                    progressPath,
                    progressOffset,
                    progress,
                    timeout.Token);

                var delayTask = Task.Delay(ProgressPollMilliseconds, timeout.Token);
                var completed = await Task.WhenAny(waitTask, delayTask);
                if (completed == waitTask)
                {
                    break;
                }
            }

            await waitTask;
            _ = await ReadProgressFileAsync(
                progressPath,
                progressOffset,
                progress,
                CancellationToken.None);

            var diagnostics = await TryReadTextAsync(diagnosticsPath, CancellationToken.None);
            if (!File.Exists(responsePath))
            {
                throw new InvalidOperationException(
                    "The OCR add-on exited without writing a response." +
                    FormatDiagnostics(diagnostics));
            }

            var output = await File.ReadAllTextAsync(responsePath, timeout.Token);
            var result = DeserializeResult(output);
            if (process is { ExitCode: not 0 } && result.Success)
            {
                throw new InvalidOperationException(
                    $"OCR add-on exited with code {process.ExitCode}." +
                    FormatDiagnostics(diagnostics));
            }

            return result;
        }
        catch
        {
            if (launcher is { HasExited: false })
            {
                launcher.Kill(entireProcessTree: true);
            }

            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
        finally
        {
            launcher?.Dispose();
            process?.Dispose();
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static async Task<int> ReadWorkerPidAsync(
        string workerPidPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(workerPidPath))
                {
                    var text = await File.ReadAllTextAsync(workerPidPath, cancellationToken);
                    if (int.TryParse(
                            text,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var processId) &&
                        processId > 0)
                    {
                        return processId;
                    }
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(20, cancellationToken);
        }

        throw new InvalidDataException("The OCR add-on launcher did not report its worker process ID.");
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

    private static OcrAnalysisResult DeserializeResult(string output) =>
        JsonSerializer.Deserialize<OcrAnalysisResult>(output, JsonOptions) ??
        throw new InvalidDataException("The OCR add-on returned an empty response.");

    private static void ValidateResult(OcrAnalysisResult result)
    {
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
    }

    private static async Task<long> ReadProgressFileAsync(
        string progressPath,
        long offset,
        IProgress<OcrProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(progressPath))
        {
            return offset;
        }

        try
        {
            await using var stream = new FileStream(
                progressPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous);
            if (offset > stream.Length)
            {
                offset = 0;
            }

            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (TryParseProgress(line, out var update))
                {
                    progress?.Report(update);
                }
            }

            return stream.Position;
        }
        catch (IOException)
        {
            return offset;
        }
        catch (UnauthorizedAccessException)
        {
            return offset;
        }
    }

    private static async Task<string> TryReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path)
                ? await File.ReadAllTextAsync(path, cancellationToken)
                : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string FormatDiagnostics(string diagnostics) =>
        string.IsNullOrWhiteSpace(diagnostics)
            ? string.Empty
            : $" Diagnostics: {diagnostics.Trim()}";

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
