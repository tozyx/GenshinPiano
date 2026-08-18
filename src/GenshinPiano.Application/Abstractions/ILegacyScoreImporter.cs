using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Abstractions;

public sealed record LegacyImportOptions(double Bpm = 120, int Ppq = 480);

public interface ILegacyScoreImporter
{
    Task<ScoreDocument> LoadAsync(
        string path,
        LegacyImportOptions? options = null,
        CancellationToken cancellationToken = default);
}
