using System.Text.Json.Nodes;

namespace GenshinPiano.Infrastructure.Serialization;

public interface IScoreSchemaMigration
{
    int FromVersion { get; }

    int ToVersion { get; }

    void Migrate(JsonObject document);
}
