using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Workspace;
using GenshinPiano.Core.Scores;
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

    private sealed class MemorySerializer : IScoreDocumentSerializer
    {
        public ScoreDocument ScoreToLoad { get; init; } = ScoreDocument.CreateEmpty();

        public Task<ScoreDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(ScoreToLoad);

        public Task SaveAsync(
            ScoreDocument score,
            string path,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
