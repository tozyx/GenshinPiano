using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.Application.Workspace;

public sealed class ScoreWorkspace(IScoreDocumentSerializer serializer, string initialTitle)
{
    public ScoreDocument CurrentScore { get; private set; } = ScoreDocument.CreateEmpty(initialTitle);

    public string? CurrentPath { get; private set; }

    public bool IsDirty { get; private set; }

    public void CreateNew(string title)
    {
        CurrentScore = ScoreDocument.CreateEmpty(title);
        CurrentPath = null;
        IsDirty = false;
    }

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var score = await serializer.LoadAsync(path, cancellationToken);
        var fileTitle = Path.GetFileNameWithoutExtension(path);
        var titleChanged = !string.Equals(
            score.Metadata.Title,
            fileTitle,
            StringComparison.Ordinal);
        CurrentScore = titleChanged
            ? score with { Metadata = score.Metadata with { Title = fileTitle } }
            : score;
        CurrentPath = Path.GetFullPath(path);
        IsDirty = titleChanged;
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

    public async Task RenameStoredScoreAsync(
        string sourcePath,
        string destinationPath,
        string title,
        CancellationToken cancellationToken = default)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var destinationFullPath = Path.GetFullPath(destinationPath);
        var isCurrentScore = CurrentPath is not null && string.Equals(
            Path.GetFullPath(CurrentPath),
            sourceFullPath,
            StringComparison.OrdinalIgnoreCase);
        var score = isCurrentScore
            ? CurrentScore
            : await serializer.LoadAsync(sourceFullPath, cancellationToken);
        var renamedScore = score with
        {
            Metadata = score.Metadata with { Title = title },
        };

        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.Ordinal))
        {
            await serializer.SaveAsync(renamedScore, sourceFullPath, cancellationToken);
        }
        else if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            var temporaryPath = Path.Combine(
                Path.GetDirectoryName(sourceFullPath)!,
                $".{Guid.NewGuid():N}{Path.GetExtension(sourceFullPath)}");
            File.Move(sourceFullPath, temporaryPath);
            try
            {
                File.Move(temporaryPath, destinationFullPath);
                await serializer.SaveAsync(renamedScore, destinationFullPath, cancellationToken);
            }
            catch
            {
                if (File.Exists(temporaryPath) && !File.Exists(sourceFullPath))
                {
                    File.Move(temporaryPath, sourceFullPath);
                }

                throw;
            }
        }
        else
        {
            await serializer.SaveAsync(renamedScore, destinationFullPath, cancellationToken);
            File.Delete(sourceFullPath);
        }

        if (isCurrentScore)
        {
            CurrentScore = renamedScore;
            CurrentPath = destinationFullPath;
            IsDirty = false;
        }
    }

    public void ReplaceScore(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);
        CurrentScore = score;
        IsDirty = true;
    }

    public void ImportScore(ScoreDocument score)
    {
        ArgumentNullException.ThrowIfNull(score);
        CurrentScore = score;
        CurrentPath = null;
        IsDirty = true;
    }

    public void RelabelCurrentScore(string title)
    {
        CurrentScore = CurrentScore with
        {
            Metadata = CurrentScore.Metadata with { Title = title },
        };
    }

    public void RestoreScore(ScoreDocument score, string? originalPath)
    {
        ArgumentNullException.ThrowIfNull(score);
        CurrentScore = score;
        CurrentPath = string.IsNullOrWhiteSpace(originalPath)
            ? null
            : Path.GetFullPath(originalPath);
        IsDirty = true;
    }
}
