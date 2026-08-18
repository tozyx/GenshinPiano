using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Conversion;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class MidiBatchConversionTests
{
    [Fact]
    public async Task ConvertDirectory_ProcessesOnlyTopLevelMidiFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"GenshinPiano-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var nested = Path.Combine(source, "nested");
        var output = Path.Combine(root, "output");
        Directory.CreateDirectory(nested);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(source, "first.mid"), "test");
            await File.WriteAllTextAsync(Path.Combine(source, "second.MIDI"), "test");
            await File.WriteAllTextAsync(Path.Combine(source, "ignore.txt"), "test");
            await File.WriteAllTextAsync(Path.Combine(nested, "nested.mid"), "test");
            var service = new MidiBatchConversionService(new StubImporter(), new StubSerializer());

            var result = await service.ConvertDirectoryAsync(source, output);

            Assert.Equal(2, result.ConvertedCount);
            Assert.True(File.Exists(Path.Combine(output, "first.gpiano")));
            Assert.True(File.Exists(Path.Combine(output, "second.gpiano")));
            Assert.False(File.Exists(Path.Combine(output, "nested.gpiano")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubImporter : IMidiScoreImporter
    {
        public Task<MidiImportResult> ImportAsync(
            string path,
            MidiImportOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MidiImportResult(
                ScoreDocument.CreateEmpty(Path.GetFileNameWithoutExtension(path)),
                new MidiImportReport(1, 1, 1, 0, 0, 0)));
    }

    private sealed class StubSerializer : IScoreDocumentSerializer
    {
        public Task<ScoreDocument> LoadAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            ScoreDocument score,
            string path,
            CancellationToken cancellationToken = default) =>
            File.WriteAllTextAsync(path, score.Metadata.Title, cancellationToken);
    }
}
