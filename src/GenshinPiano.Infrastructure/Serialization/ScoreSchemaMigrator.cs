using System.Text.Json.Nodes;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Infrastructure.Serialization;

public sealed class ScoreSchemaMigrator
{
    private readonly IReadOnlyDictionary<int, IScoreSchemaMigration> _migrations;

    public ScoreSchemaMigrator(IEnumerable<IScoreSchemaMigration>? migrations = null)
    {
        var registered = (migrations ?? [new ScoreSchemaV0ToV1Migration()]).ToArray();
        foreach (var migration in registered)
        {
            if (migration.FromVersion < 0 || migration.ToVersion != migration.FromVersion + 1)
            {
                throw new ArgumentException(
                    $"Score migration {migration.GetType().Name} must advance exactly one version.",
                    nameof(migrations));
            }
        }

        try
        {
            _migrations = registered.ToDictionary(migration => migration.FromVersion);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Only one score migration may be registered for each source version.",
                nameof(migrations),
                exception);
        }
    }

    public JsonObject MigrateToCurrent(JsonNode? document)
    {
        if (document is not JsonObject root)
        {
            throw new InvalidDataException("曲谱 JSON 的根节点必须是对象。");
        }

        var version = ReadVersion(root);
        if (version < 0)
        {
            throw new InvalidDataException("曲谱 schemaVersion 不能为负数。");
        }
        if (version > ScoreDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"曲谱版本 {version} 高于当前支持的版本 {ScoreDocument.CurrentSchemaVersion}，请升级 GenshinPiano 后再打开。");
        }

        while (version < ScoreDocument.CurrentSchemaVersion)
        {
            if (!_migrations.TryGetValue(version, out var migration))
            {
                throw new InvalidDataException(
                    $"缺少从曲谱版本 {version} 到 {version + 1} 的迁移步骤。");
            }

            migration.Migrate(root);
            version = migration.ToVersion;
            SetCanonicalProperty(root, "schemaVersion", JsonValue.Create(version));
        }

        return root;
    }

    private static int ReadVersion(JsonObject root)
    {
        var property = FindProperty(root, "schemaVersion");
        if (property is null)
        {
            return 0;
        }

        if (property.Value.Value is JsonValue value && value.TryGetValue<int>(out var version))
        {
            return version;
        }

        throw new InvalidDataException("曲谱 schemaVersion 必须是整数。");
    }

    internal static KeyValuePair<string, JsonNode?>? FindProperty(
        JsonObject value,
        string propertyName)
    {
        foreach (var property in value)
        {
            if (string.Equals(property.Key, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        return null;
    }

    internal static void SetCanonicalProperty(
        JsonObject value,
        string propertyName,
        JsonNode? propertyValue)
    {
        var existing = FindProperty(value, propertyName);
        if (existing is { } property && !string.Equals(property.Key, propertyName, StringComparison.Ordinal))
        {
            value.Remove(property.Key);
        }

        value[propertyName] = propertyValue;
    }
}
