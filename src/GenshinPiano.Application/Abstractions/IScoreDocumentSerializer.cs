using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Abstractions;

public interface IScoreDocumentSerializer
{
    Task<ScoreDocument> LoadAsync(string path, CancellationToken cancellationToken = default);

    Task SaveAsync(ScoreDocument score, string path, CancellationToken cancellationToken = default);
}
