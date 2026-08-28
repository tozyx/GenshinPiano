using System.Text.Json.Nodes;

namespace GenshinPiano.Infrastructure.Serialization;

public sealed class ScoreSchemaV0ToV1Migration : IScoreSchemaMigration
{
    public int FromVersion => 0;

    public int ToVersion => 1;

    public void Migrate(JsonObject document)
    {
        EnsureObject(document, "metadata");
        var timing = EnsureObject(document, "timing");
        EnsureArray(timing, "tempoMap");
        EnsureArray(timing, "timeSignatures");
        var tracks = EnsureArray(document, "tracks");
        foreach (var track in tracks.OfType<JsonObject>())
        {
            EnsureArray(track, "notes");
        }
        EnsureObject(document, "playback");
    }

    private static JsonObject EnsureObject(JsonObject parent, string propertyName)
    {
        var existing = ScoreSchemaMigrator.FindProperty(parent, propertyName);
        if (existing?.Value is JsonObject value)
        {
            ScoreSchemaMigrator.SetCanonicalProperty(parent, propertyName, value);
            return value;
        }
        if (existing?.Value is not null)
        {
            throw new InvalidDataException($"旧版曲谱字段 {propertyName} 必须是对象。");
        }

        var created = new JsonObject();
        ScoreSchemaMigrator.SetCanonicalProperty(parent, propertyName, created);
        return created;
    }

    private static JsonArray EnsureArray(JsonObject parent, string propertyName)
    {
        var existing = ScoreSchemaMigrator.FindProperty(parent, propertyName);
        if (existing?.Value is JsonArray value)
        {
            ScoreSchemaMigrator.SetCanonicalProperty(parent, propertyName, value);
            return value;
        }
        if (existing?.Value is not null)
        {
            throw new InvalidDataException($"旧版曲谱字段 {propertyName} 必须是数组。");
        }

        var created = new JsonArray();
        ScoreSchemaMigrator.SetCanonicalProperty(parent, propertyName, created);
        return created;
    }
}
