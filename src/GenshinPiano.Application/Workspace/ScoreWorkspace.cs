using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Workspace;

public sealed class ScoreWorkspace(IScoreDocumentSerializer serializer)
{
    public ScoreDocument CurrentScore { get; private set; } = ScoreDocument.CreateEmpty();

    public string? CurrentPath { get; private set; }

    public bool IsDirty { get; private set; }

    public void CreateNew(string title = "未命名曲谱")
    {
        CurrentScore = ScoreDocument.CreateEmpty(title);
        CurrentPath = null;
        IsDirty = false;
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        CurrentScore = await serializer.LoadAsync(path, cancellationToken);
        CurrentPath = Path.GetFullPath(path);
        IsDirty = false;
    }

    public async Task SaveAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        var targetPath = path ?? CurrentPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new InvalidOperationException("首次保存曲谱时必须指定路径。");
        }

        await serializer.SaveAsync(CurrentScore, targetPath, cancellationToken);
        CurrentPath = Path.GetFullPath(targetPath);
        IsDirty = false;
    }

    public void ReplaceScore(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);
        CurrentScore = score;
        IsDirty = true;
    }
}
