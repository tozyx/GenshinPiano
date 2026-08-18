using GenshinPiano.Application.Abstractions;

namespace GenshinPiano.Application.Conversion;

public sealed record LegacyFileConversionResult(
    string SourcePath,
    string? OutputPath,
    bool IsSuccess,
    bool IsSkipped,
    string? ErrorMessage = null);

public sealed record LegacyBatchConversionResult(IReadOnlyList<LegacyFileConversionResult> Files)
{
    public int ConvertedCount => Files.Count(file => file.IsSuccess);

    public int SkippedCount => Files.Count(file => file.IsSkipped);

    public int FailedCount => Files.Count(file => !file.IsSuccess && !file.IsSkipped);
}

public sealed class LegacyBatchConversionService(
    ILegacyScoreImporter importer,
    IScoreDocumentSerializer serializer)
{
    public async Task<LegacyBatchConversionResult> ConvertDirectoryAsync(
        string sourceDirectory,
        string outputDirectory,
        LegacyImportOptions? options = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(sourceRoot);
        }

        var sourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".GenshinPiano", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var results = new List<LegacyFileConversionResult>(sourceFiles.Length);

        foreach (var sourcePath in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var outputPath = Path.Combine(outputRoot, Path.ChangeExtension(relativePath, ".gpiano"));

            if (!overwrite && File.Exists(outputPath))
            {
                results.Add(new LegacyFileConversionResult(sourcePath, outputPath, false, true));
                continue;
            }

            try
            {
                var score = await importer.LoadAsync(sourcePath, options, cancellationToken);
                await serializer.SaveAsync(score, outputPath, cancellationToken);
                results.Add(new LegacyFileConversionResult(sourcePath, outputPath, true, false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new LegacyFileConversionResult(sourcePath, null, false, false, exception.Message));
            }
        }

        return new LegacyBatchConversionResult(results);
    }
}
