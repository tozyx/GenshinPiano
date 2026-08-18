using GenshinPiano.Application.Conversion;
using GenshinPiano.Core.Scores;
using GenshinPiano.Infrastructure.Legacy;
using GenshinPiano.Infrastructure.Serialization;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class LegacyConversionTests
{
    [Theory]
    [InlineData('b', 60)]
    [InlineData('1', 120)]
    [InlineData('2', 240)]
    [InlineData('4', 480)]
    [InlineData('8', 960)]
    [InlineData('s', 1440)]
    [InlineData('c', 90)]
    [InlineData('y', 180)]
    [InlineData('3', 360)]
    [InlineData('5', 720)]
    [InlineData('9', 1440)]
    [InlineData('d', 2160)]
    public async Task Importer_ConvertsLegacyTimingUnitsToBeats(char code, long expectedRhythmTick)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.GenshinPiano");
        try
        {
            await File.WriteAllTextAsync(path, $"A {code}\n");

            var score = await new LegacyGenshinPianoImporter().LoadAsync(path);

            Assert.Equal(expectedRhythmTick, Assert.Single(score.Tracks[0].Notes).RhythmTick);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Importer_ConvertsChordsDurationsAndRests()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.GenshinPiano");
        try
        {
            await File.WriteAllTextAsync(path, "|1\n\nQA 2\t\t1+ 1\nP 1\t\t\\0\nD b\t\t3\n");
            var importer = new LegacyGenshinPianoImporter();

            var score = await importer.LoadAsync(path);

            Assert.Equal(3, score.Tracks[0].Notes.Count);
            Assert.Equal([72, 60], score.Tracks[0].Notes.Take(2).Select(note => note.Pitch));
            Assert.All(score.Tracks[0].Notes.Take(2), note => Assert.Equal(0, note.StartTick));
            Assert.All(score.Tracks[0].Notes, note => Assert.Equal(DurationMode.Auto, note.DurationMode));
            Assert.All(score.Tracks[0].Notes, note => Assert.Equal(NoteArticulation.Natural, note.Articulation));
            Assert.All(score.Tracks[0].Notes, note => Assert.Equal(0.80, note.GateRatio));
            Assert.Equal(240, score.Tracks[0].Notes[0].RhythmTick);
            Assert.Equal(192, score.Tracks[0].Notes[0].DurationTick);
            Assert.Equal(360, score.Tracks[0].Notes[2].StartTick);
            Assert.Equal(60, score.Tracks[0].Notes[2].RhythmTick);
            Assert.Equal(48, score.Tracks[0].Notes[2].DurationTick);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BatchConverter_PreservesRelativeFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"GenshinPiano-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var output = Path.Combine(root, "output");
        var nested = Path.Combine(source, "album");
        Directory.CreateDirectory(nested);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(nested, "song.GenshinPiano"), "|1\n\nA 1\n");
            var service = new LegacyBatchConversionService(
                new LegacyGenshinPianoImporter(),
                new JsonScoreDocumentSerializer());

            var result = await service.ConvertDirectoryAsync(source, output);

            Assert.Equal(1, result.ConvertedCount);
            var outputPath = Path.Combine(output, "album", "song.gpiano");
            Assert.True(File.Exists(outputPath));
            var json = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("\"durationMode\": \"auto\"", json);
            Assert.Contains("\"rhythmTick\": 120", json);
            Assert.Contains("\"gateRatio\": 0.8", json);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
