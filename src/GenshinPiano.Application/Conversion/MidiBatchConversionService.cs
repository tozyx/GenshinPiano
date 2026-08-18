using GenshinPiano.Application.Abstractions;

namespace GenshinPiano.Application.Conversion;

public sealed record MidiFileConversionResult(
    string SourcePath,
    string? OutputPath,
    bool IsSuccess,
    bool IsSkipped,
    string? ErrorMessage = null);

public sealed record MidiBatchConversionResult(IReadOnlyList<MidiFileConversionResult> Files)
{
    public int ConvertedCount => Files.Count(file => file.IsSuccess);

    public int SkippedCount => Files.Count(file => file.IsSkipped);

    public int FailedCount => Files.Count(file => !file.IsSuccess && !file.IsSkipped);
}

public sealed class MidiBatchConversionService(
    IMidiScoreImporter importer,
    IScoreDocumentSerializer serializer)
{
    public async Task<MidiBatchConversionResult> ConvertDirectoryAsync(
        string sourceDirectory,
        string outputDirectory,
        MidiImportOptions? options = null,
        bool overwrite = false,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(sourceRoot);
        }

        Directory.CreateDirectory(outputRoot);
        var sourceFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path) is { } extension &&
                (extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".midi", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var results = new List<MidiFileConversionResult>(sourceFiles.Length);

        for (var index = 0; index < sourceFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = sourceFiles[index];
            var outputPath = Path.Combine(
                outputRoot,
                Path.ChangeExtension(Path.GetFileName(sourcePath), ".gpiano"));

            if (!overwrite && File.Exists(outputPath))
            {
                results.Add(new MidiFileConversionResult(sourcePath, outputPath, false, true));
                progress?.Report(index + 1);
                continue;
            }

            try
            {
                var imported = await importer.ImportAsync(sourcePath, options, cancellationToken);
                await serializer.SaveAsync(imported.Score, outputPath, cancellationToken);
                results.Add(new MidiFileConversionResult(sourcePath, outputPath, true, false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new MidiFileConversionResult(sourcePath, null, false, false, exception.Message));
            }

            progress?.Report(index + 1);
        }

        return new MidiBatchConversionResult(results);
    }
}
