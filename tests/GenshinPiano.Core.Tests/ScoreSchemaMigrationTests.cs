using System.Text.Json;
using System.Text.Json.Nodes;
using GenshinPiano.Core.Scores;
using GenshinPiano.Infrastructure.Serialization;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreSchemaMigrationTests
{
    [Fact]
    public async Task Serializer_MigratesSchemaLessDocumentToCurrentVersion()
    {
        var path = CreateTemporaryPath();
        try
        {
            await File.WriteAllTextAsync(path, "{}");

            var score = await new JsonScoreDocumentSerializer().LoadAsync(path);

            Assert.Equal(ScoreDocument.CurrentSchemaVersion, score.SchemaVersion);
            Assert.NotNull(score.Metadata);
            Assert.NotNull(score.Timing);
            Assert.NotNull(score.Playback);
            Assert.Empty(score.Tracks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Serializer_LoadsCurrentVersionWithoutMigration()
    {
        var path = CreateTemporaryPath();
        try
        {
            var serializer = new JsonScoreDocumentSerializer();
            await serializer.SaveAsync(ScoreDocument.CreateEmpty("Current"), path);

            var score = await serializer.LoadAsync(path);

            Assert.Equal(ScoreDocument.CurrentSchemaVersion, score.SchemaVersion);
            Assert.Equal("Current", score.Metadata.Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Serializer_RejectsDocumentFromNewerApplication()
    {
        var path = CreateTemporaryPath();
        try
        {
            await File.WriteAllTextAsync(
                path,
                $$"""{"schemaVersion":{{ScoreDocument.CurrentSchemaVersion + 1}}}""");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonScoreDocumentSerializer().LoadAsync(path));

            Assert.Contains("请升级 GenshinPiano", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Serializer_RejectsNonIntegerSchemaVersion()
    {
        var path = CreateTemporaryPath();
        try
        {
            await File.WriteAllTextAsync(path, "{\"schemaVersion\":\"1\"}");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonScoreDocumentSerializer().LoadAsync(path));

            Assert.Contains("必须是整数", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Serializer_ReportsNullCurrentSchemaSectionsAsInvalidData()
    {
        var path = CreateTemporaryPath();
        try
        {
            await File.WriteAllTextAsync(
                path,
                $$"""{"schemaVersion":{{ScoreDocument.CurrentSchemaVersion}},"metadata":null,"timing":null,"tracks":null,"playback":null}""");

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => new JsonScoreDocumentSerializer().LoadAsync(path));

            Assert.Contains("metadata 不能为空", exception.Message);
            Assert.Contains("timing 不能为空", exception.Message);
            Assert.Contains("tracks 不能为空", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Serializer_SaveAlwaysWritesCurrentSchemaVersion()
    {
        var path = CreateTemporaryPath();
        try
        {
            var oldScore = ScoreDocument.CreateEmpty("Old") with { SchemaVersion = 0 };

            await new JsonScoreDocumentSerializer().SaveAsync(oldScore, path);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

            Assert.Equal(
                ScoreDocument.CurrentSchemaVersion,
                json["schemaVersion"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Migrator_RejectsMissingMigrationStep()
    {
        var migrator = new ScoreSchemaMigrator([]);

        var exception = Assert.Throws<InvalidDataException>(
            () => migrator.MigrateToCurrent(new JsonObject()));

        Assert.Contains("缺少从曲谱版本 0", exception.Message);
    }

    [Fact]
    public void Migrator_RejectsNonSequentialMigration()
    {
        Assert.Throws<ArgumentException>(
            () => new ScoreSchemaMigrator([new InvalidMigration()]));
    }

    private static string CreateTemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"GenshinPiano-{Guid.NewGuid():N}.gpiano");

    private sealed class InvalidMigration : IScoreSchemaMigration
    {
        public int FromVersion => 0;

        public int ToVersion => 2;

        public void Migrate(JsonObject document)
        {
        }
    }
}
