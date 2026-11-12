using System.IO;
using GenshinPiano.App.Dialogs;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Workspace;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class NewScoreCommandTests
{
    [Fact]
    public async Task NewCommand_ProtectsUnsavedScoreAcrossConfirmationAndSaveOutcomes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GenshinPiano-new-score-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var serializer = new RecordingSerializer();
            var workspace = new ScoreWorkspace(serializer, "Original");
            var choice = UnsavedChangesChoice.Cancel;
            var prompts = 0;
            MainWindowViewModel? viewModel = null;
            // Simulate user choices without creating windows or touching the user's desktop.
            UnsavedChangesChoice Confirm()
            {
                prompts++;
                Assert.False(viewModel!.NewCommand.CanExecute(null));
                viewModel.NewCommand.Execute(null); // Repeated clicks must not open a second prompt.
                return choice;
            }
            // Playback, conversion and update services are not used by the new-score command.
            viewModel = new MainWindowViewModel(workspace, new ThemeService(), new TestLocalization(),
                new UserSettingsService(Path.Combine(directory, "settings.json")), null!, null!, null!,
                new ScoreRecoveryService(serializer), null!, Confirm);
            var original = ScoreDocument.CreateEmpty("Edited score");
            var path = Path.Combine(directory, "original.gpiano");

            void Edit() => workspace.RestoreScore(original, path);

            async Task NewAsync(UnsavedChangesChoice? response)
            {
                choice = response ?? UnsavedChangesChoice.Cancel;
                prompts = 0;
                viewModel.NewCommand.Execute(null);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!viewModel.NewCommand.CanExecute(null) && DateTime.UtcNow < deadline)
                    await Task.Delay(10);
                Assert.True(viewModel.NewCommand.CanExecute(null));
                Assert.Equal(response is null ? 0 : 1, prompts);
            }

            await NewAsync(null);
            Assert.False(workspace.IsDirty);
            Assert.NotEqual("Original", workspace.CurrentScore.Metadata.Title);

            Edit();
            await NewAsync(UnsavedChangesChoice.Cancel);
            Assert.Same(original, workspace.CurrentScore);
            Assert.Equal(path, workspace.CurrentPath);
            Assert.True(workspace.IsDirty);
            Assert.Null(serializer.SavedScore);

            await NewAsync(UnsavedChangesChoice.DontSave);
            Assert.NotSame(original, workspace.CurrentScore);
            Assert.Null(workspace.CurrentPath);
            Assert.False(workspace.IsDirty);
            Assert.Null(serializer.SavedScore);

            Edit();
            await NewAsync(UnsavedChangesChoice.Save);
            Assert.Same(original, serializer.SavedScore);
            Assert.Equal(path, serializer.SavedPath);
            Assert.NotSame(original, workspace.CurrentScore);
            Assert.Null(workspace.CurrentPath);
            Assert.False(workspace.IsDirty);

            Edit();
            serializer.OnSave = () => throw new IOException("Simulated write failure");
            await NewAsync(UnsavedChangesChoice.Save);
            Assert.Same(original, workspace.CurrentScore);
            Assert.Equal(path, workspace.CurrentPath);
            Assert.True(workspace.IsDirty);

            // A newer edit made while saving must not be cleared by the pending new command.
            var newer = ScoreDocument.CreateEmpty("Newer edit");
            serializer.OnSave = async () =>
            {
                await Task.Yield();
                workspace.ReplaceScore(newer);
            };
            await NewAsync(UnsavedChangesChoice.Save);
            Assert.Same(newer, workspace.CurrentScore);
            Assert.True(workspace.IsDirty);
            await NewAsync(UnsavedChangesChoice.DontSave); // Cancel autosave scheduled for the newer edit.
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class TestLocalization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.SimplifiedChinese;
        public event EventHandler? LanguageChanged { add { } remove { } }
        public string GetString(string key, params object?[] arguments) => key;
        public void Apply(AppLanguage language) => throw new NotSupportedException();
    }

    private sealed class RecordingSerializer : IScoreDocumentSerializer
    {
        public ScoreDocument? SavedScore { get; private set; }
        public string? SavedPath { get; private set; }
        public Func<Task>? OnSave { get; set; }

        public Task<ScoreDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task SaveAsync(ScoreDocument score, string path, CancellationToken cancellationToken = default)
        {
            if (OnSave is not null) await OnSave();
            SavedScore = score;
            SavedPath = path;
        }
    }
}
