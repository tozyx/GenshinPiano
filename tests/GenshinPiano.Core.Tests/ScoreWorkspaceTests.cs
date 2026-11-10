using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Workspace;
using GenshinPiano.Core.Scores;
using GenshinPiano.Infrastructure.Serialization;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreWorkspaceTests
{
    [Fact]
    public void DefaultCreatedScore_UsesProvidedLocalizedTitle()
    {
        var workspace = new ScoreWorkspace(new MemorySerializer(), "Untitled score");

        Assert.Equal("Untitled score", workspace.CurrentScore.Metadata.Title);
    }

    [Fact]
    public async Task EditingAndSaving_UpdatesDirtyState()
    {
        var serializer = new MemorySerializer();
        var workspace = new ScoreWorkspace(serializer, "Untitled score");

        workspace.CreateNew("Test");
        Assert.False(workspace.IsDirty);

        workspace.ReplaceScore(workspace.CurrentScore with
        {
            Metadata = workspace.CurrentScore.Metadata with { Title = "Changed" },
        });
        Assert.True(workspace.IsDirty);

        await workspace.SaveAsync("score.gpiano");
        Assert.False(workspace.IsDirty);
    }

    [Fact]
    public async Task LoadedScore_BecomesDirtyOnlyAfterEditing()
    {
        var serializer = new MemorySerializer
        {
            ScoreToLoad = ScoreDocument.CreateEmpty("loaded"),
        };
        var workspace = new ScoreWorkspace(serializer, "Untitled score");

        await workspace.LoadAsync("loaded.gpiano");
        Assert.False(workspace.IsDirty);

        workspace.ReplaceScore(workspace.CurrentScore with
        {
            Tracks = [.. workspace.CurrentScore.Tracks, new ScoreTrack { Id = "second" }],
        });
        Assert.True(workspace.IsDirty);
    }

    [Fact]
    public async Task LoadedScore_WithDifferentInternalTitle_UsesFileNameAndBecomesDirty()
    {
        var serializer = new MemorySerializer
        {
            ScoreToLoad = ScoreDocument.CreateEmpty("Encoded title"),
        };
        var workspace = new ScoreWorkspace(serializer, "Untitled score");

        await workspace.LoadAsync("File title.gpiano");

        Assert.Equal("File title", workspace.CurrentScore.Metadata.Title);
        Assert.True(workspace.IsDirty);
    }

    [Fact]
    public async Task ImportedScore_IsDirtyAndDoesNotReuseSourcePath()
    {
        var workspace = new ScoreWorkspace(new MemorySerializer(), "Untitled score");
        await workspace.LoadAsync("source.gpiano");

        workspace.ImportScore(ScoreDocument.CreateEmpty("Imported MIDI"));

        Assert.True(workspace.IsDirty);
        Assert.Null(workspace.CurrentPath);
        Assert.Equal("Imported MIDI", workspace.CurrentScore.Metadata.Title);
    }

    [Fact]
    public async Task SaveInProgress_DoesNotMarkNewEditsAsSaved()
    {
        var write = new TaskCompletionSource();
        var serializer = new MemorySerializer { SaveCompletion = write.Task };
        var workspace = new ScoreWorkspace(serializer, "Untitled");
        workspace.ReplaceScore(ScoreDocument.CreateEmpty("Snapshot"));

        var save = workspace.SaveAsync("score.gpiano");
        workspace.ReplaceScore(ScoreDocument.CreateEmpty("New edit"));
        write.SetResult();

        Assert.True(await save);
        Assert.Equal("Snapshot", serializer.LastSavedScore!.Metadata.Title);
        Assert.Equal("New edit", workspace.CurrentScore.Metadata.Title);
        Assert.Equal(Path.GetFullPath("score.gpiano"), workspace.CurrentPath);
        Assert.True(workspace.IsDirty);
        Assert.True(await workspace.SaveAsync());
        Assert.False(workspace.IsDirty);
    }

    [Fact]
    public async Task SaveInProgress_DoesNotAttachOldPathToNewDocument()
    {
        var write = new TaskCompletionSource();
        var workspace = new ScoreWorkspace(new MemorySerializer { SaveCompletion = write.Task }, "Untitled");

        var save = workspace.SaveAsync("old.gpiano");
        workspace.ImportScore(ScoreDocument.CreateEmpty("Imported"));
        write.SetResult();

        Assert.False(await save);
        Assert.Null(workspace.CurrentPath);
        Assert.True(workspace.IsDirty);
    }

    [Fact]
    public async Task BlockedReplacement_PreservesOriginalAndRemovesTemporaryFile()
    {
        var directory = Directory.CreateTempSubdirectory("gpiano-save-");
        try
        {
            var path = Path.Combine(directory.FullName, "score.gpiano");
            var serializer = new JsonScoreDocumentSerializer();
            await serializer.SaveAsync(ScoreDocument.CreateEmpty("Original"), path);

            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var error = await Record.ExceptionAsync(() =>
                    serializer.SaveAsync(ScoreDocument.CreateEmpty("Replacement"), path));
                Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
                Assert.Equal("Original", (await serializer.LoadAsync(path)).Metadata.Title);
                Assert.Equal(path, Assert.Single(Directory.GetFiles(directory.FullName)));
            }

            await serializer.SaveAsync(ScoreDocument.CreateEmpty("Replacement"), path);
            Assert.Equal("Replacement", (await serializer.LoadAsync(path)).Metadata.Title);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class MemorySerializer : IScoreDocumentSerializer
    {
        public ScoreDocument ScoreToLoad { get; init; } = ScoreDocument.CreateEmpty();
        public Task SaveCompletion { get; init; } = Task.CompletedTask;
        public ScoreDocument? LastSavedScore { get; private set; }

        public Task<ScoreDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScoreToLoad);

        public Task SaveAsync(
            ScoreDocument score,
            string path,
            CancellationToken cancellationToken = default)
        {
            LastSavedScore = score;
            return SaveCompletion;
        }
    }
}
