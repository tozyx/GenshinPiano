using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Workspace;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreWorkspaceTests
{
    [Fact]
    public async Task EditingAndSaving_UpdatesDirtyState()
    {
        var serializer = new MemorySerializer();
        var workspace = new ScoreWorkspace(serializer);

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
            ScoreToLoad = ScoreDocument.CreateEmpty("Loaded"),
        };
        var workspace = new ScoreWorkspace(serializer);

        await workspace.LoadAsync("loaded.gpiano");
        Assert.False(workspace.IsDirty);

        workspace.ReplaceScore(workspace.CurrentScore with
        {
            Tracks = [.. workspace.CurrentScore.Tracks, new ScoreTrack { Id = "second" }],
        });
        Assert.True(workspace.IsDirty);
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
