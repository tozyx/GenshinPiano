using System.Diagnostics;
using System.Text;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Core.Scores;
using GenshinPiano.Infrastructure.MusicXml;

namespace GenshinPiano.Ocr.Engine;

internal sealed class StaffNotationRecognizer
{
    private const string BackendEnvironmentVariable = "GENSHINPIANO_OEMER_EXECUTABLE";

    public async Task<OcrAnalysisResult> RecognizeAsync(OcrAnalysisRequest request, CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ImagePath)) return Failure("image_not_found", "The source image does not exist.");
        var backend = FindBackend();
        if (backend is null) return Failure("staff_backend_missing", "The staff-notation backend is not installed. Install oemer in the OCR add-on's staff-omr environment.");

        var workDirectory = Path.Combine(Path.GetTempPath(), $"GenshinPiano-OMR-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        var musicXmlPath = Path.Combine(workDirectory, "recognized.musicxml");
        try
        {
            OcrProgressReporter.Report(OcrProgressStage.Preparing, 0.05);
            var output = await RunBackendAsync(
                backend,
                request.ImagePath,
                musicXmlPath,
                workDirectory,
                request.PreferGpuAcceleration,
                cancellationToken);
            if (!File.Exists(musicXmlPath)) return Failure("staff_backend_no_output", BuildFailureMessage(output));

            OcrProgressReporter.Report(OcrProgressStage.ScoreReconstruction, 0.88);
            var imported = await new MusicXmlScoreImporter().ImportAsync(musicXmlPath, cancellationToken);
            var score = request.IncludeAccompaniment ? imported.Score : KeepUpperStaff(imported.Score);
            score = score with { Playback = score.Playback with { Mapping = "full-pitch", OutOfRangePolicy = OutOfRangePolicy.Drop } };
            var warnings = imported.Report.Warnings.ToList();
            if (request.PreferGpuAcceleration && output.Contains("OCR_BACKEND|CPU", StringComparison.Ordinal))
                warnings.Add("CUDA acceleration was unavailable; staff recognition used the CPU.");
            if (!request.IncludeAccompaniment && imported.Score.Tracks.Count > score.Tracks.Count)
                warnings.Add("Lower staves were omitted because accompaniment recognition is disabled.");
            if (imported.Report.GraceNoteCount > 0)
                warnings.Add($"{imported.Report.GraceNoteCount} grace notes require manual timing review.");

            OcrProgressReporter.Report(OcrProgressStage.ScoreReconstruction, 1);
            return new OcrAnalysisResult(OcrProtocol.CurrentVersion, true, score, EstimateConfidence(imported.Report), warnings);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return Failure("staff_backend_failure", exception.Message); }
        finally { TryDeleteDirectory(workDirectory); }
    }

    private static BackendCommand? FindBackend()
    {
        var configured = Environment.GetEnvironmentVariable(BackendEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return CreateCommand(configured);
        var root = Path.Combine(AppContext.BaseDirectory, "staff-omr");
        var candidates = new[]
        {
            Path.Combine(root, "python", "python.exe"),
            Path.Combine(root, ".venv", "Scripts", "python.exe"),
            Path.Combine(root, "oemer.exe"),
            Path.Combine(root, ".venv", "Scripts", "oemer.exe"),
            Path.Combine(root, "python", "Scripts", "oemer.exe"),
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is not null) return CreateCommand(executable);
        return FindDevelopmentBackend();
    }

    private static BackendCommand? FindDevelopmentBackend()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 9; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "_research", "oemer", ".venv", "Scripts", "python.exe");
            if (File.Exists(candidate)) return CreateCommand(candidate);
        }
        return null;
    }

    private static BackendCommand CreateCommand(string executable) => new(executable, Path.GetFileName(executable).Equals("python.exe", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> RunBackendAsync(
        BackendCommand backend,
        string imagePath,
        string musicXmlPath,
        string workDirectory,
        bool preferGpuAcceleration,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = backend.Executable,
            WorkingDirectory = workDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (backend.UsePythonModule)
        {
            var bridge = Path.Combine(AppContext.BaseDirectory, "staff-omr", "oemer_bridge.py");
            if (!File.Exists(bridge)) throw new FileNotFoundException("The oemer bridge script is missing.", bridge);
            startInfo.ArgumentList.Add(bridge);
            if (preferGpuAcceleration) startInfo.ArgumentList.Add("--use-gpu");
        }
        else if (!preferGpuAcceleration)
        {
            startInfo.Environment["CUDA_VISIBLE_DEVICES"] = "-1";
        }
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("--output-path");
        startInfo.ArgumentList.Add(musicXmlPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Unable to start the staff-notation backend.");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stdoutTask = ReadOutputAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadOutputAsync(process.StandardError, cancellationToken);
        OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.25);
        await process.WaitForExitAsync(cancellationToken);
        var output = $"{await stdoutTask}\n{await stderrTask}";
        if (process.ExitCode != 0) throw new InvalidOperationException(BuildFailureMessage(output));
        return output;
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            output.AppendLine(line);
            ReportBackendProgress(line);
        }
        return output.ToString();
    }

    private static void ReportBackendProgress(string line)
    {
        var normalized = line.ToLowerInvariant();
        if (normalized.Contains("staffline and symbols")) OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.30);
        else if (normalized.Contains("layers of different symbols")) OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.40);
        else if (normalized.Contains("dewarping")) OcrProgressReporter.Report(OcrProgressStage.SuperResolution, 0.48);
        else if (normalized.Contains("extracting stafflines")) OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.56);
        else if (normalized.Contains("extracting noteheads")) OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.64);
        else if (normalized.Contains("grouping noteheads")) OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.70);
        else if (normalized.Contains("extracting symbols")) OcrProgressReporter.Report(OcrProgressStage.TextDetection, 0.76);
        else if (normalized.Contains("rhythm types")) OcrProgressReporter.Report(OcrProgressStage.ScoreReconstruction, 0.83);
        else if (normalized.Contains("musicxml")) OcrProgressReporter.Report(OcrProgressStage.ScoreReconstruction, 0.90);
    }

    private static ScoreDocument KeepUpperStaff(ScoreDocument score)
    {
        var upper = score.Tracks.Where(x => x.Name.EndsWith("Staff 1", StringComparison.OrdinalIgnoreCase)).ToList();
        if (upper.Count == 0 && score.Tracks.Count > 0) upper.Add(score.Tracks[0]);
        return score with { Tracks = upper };
    }

    private static double EstimateConfidence(MusicXmlImportReport report)
    {
        if (report.NoteCount == 0) return 0;
        var penalty = Math.Min(0.25, report.Warnings.Count * 0.03 + report.GraceNoteCount * 0.002);
        return Math.Clamp(0.82 - penalty, 0.35, 0.82);
    }

    private static string BuildFailureMessage(string output)
    {
        var text = output.Trim();
        if (text.Length > 1200) text = text[^1200..];
        return string.IsNullOrWhiteSpace(text) ? "The staff-notation backend did not produce MusicXML." : text;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static OcrAnalysisResult Failure(string code, string message) => new(OcrProtocol.CurrentVersion, false, null, ErrorCode: code, ErrorMessage: message);
    private sealed record BackendCommand(string Executable, bool UsePythonModule);
}
