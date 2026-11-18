using System.Collections.Specialized;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using GenshinPiano.App.Dialogs;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Application.Workspace;
using GenshinPiano.Core.Scores;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class ScoreFolderSelectionTests
{
    [Fact]
    public Task RefreshRetainsSelectionAndItemIdentity_WithoutResetOrReopening() => OnDispatcher(async () =>
    {
        using var fixture = new Fixture();
        var path = fixture.Add("library", "Middle");
        fixture.Add("library", "Last");
        await fixture.ViewModel.SetScoreFolderAsync(Path.GetDirectoryName(path)!);
        await fixture.ViewModel.OpenPathAsync(path);
        var selected = fixture.List.SelectedItem;
        Assert.NotNull(selected);
        var resets = 0;
        fixture.ViewModel.ScoreFolderFiles.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) resets++;
        };

        fixture.Add("library", "First");
        await fixture.ViewModel.RefreshScoreFolderAsync();
        Assert.Same(selected, fixture.List.SelectedItem);
        Assert.Same(selected, fixture.ViewModel.CurrentScoreFolderFile);
        Assert.Equal(["First", "Last", "Middle"], fixture.ViewModel.ScoreFolderFiles.Select(file => file.DisplayName));
        Assert.Equal(0, resets);
        Assert.Equal(1, fixture.Serializer.Loads);
        Assert.False(fixture.Workspace.IsDirty);

        // Selection restoration must keep the binding alive after a cancelled or completed open.
        var other = fixture.ViewModel.ScoreFolderFiles[0];
        fixture.List.SetCurrentValue(Selector.SelectedItemProperty, other);
        fixture.List.SetCurrentValue(Selector.SelectedItemProperty, fixture.ViewModel.CurrentScoreFolderFile);
        Assert.True(BindingOperations.IsDataBound(fixture.List, Selector.SelectedItemProperty));
        await fixture.ViewModel.OpenPathAsync(other.Path);
        Assert.Same(other, fixture.List.SelectedItem);
    });

    [Fact]
    public Task FolderChangesDoNotSelectSameNamedFiles_AndReturningRestoresCurrentScore() => OnDispatcher(async () =>
    {
        using var fixture = new Fixture();
        var original = fixture.Add("first", "Song");
        var sameName = fixture.Add("second", "Song");
        await fixture.ViewModel.SetScoreFolderAsync(Path.GetDirectoryName(original)!);
        await fixture.ViewModel.OpenPathAsync(original);
        var score = fixture.Workspace.CurrentScore;

        // An older refresh is still allowed to complete, but cannot repopulate the old folder.
        var oldRefresh = fixture.ViewModel.RefreshScoreFolderAsync();
        var change = fixture.ViewModel.SetScoreFolderAsync(Path.GetDirectoryName(sameName)!);
        await Task.WhenAll(oldRefresh, change);
        Assert.Equal(sameName, Assert.Single(fixture.ViewModel.ScoreFolderFiles).Path);
        Assert.Null(fixture.List.SelectedItem);
        Assert.Null(fixture.ViewModel.CurrentScoreFolderFile);
        Assert.Equal(original, fixture.ViewModel.CurrentSourcePath);
        Assert.Same(score, fixture.Workspace.CurrentScore);
        Assert.Equal(1, fixture.Serializer.Loads);

        var empty = Path.Combine(fixture.Directory, "empty");
        System.IO.Directory.CreateDirectory(empty);
        await fixture.ViewModel.SetScoreFolderAsync(empty);
        Assert.Empty(fixture.List.Items);
        Assert.Null(fixture.List.SelectedItem);
        await fixture.ViewModel.SetScoreFolderAsync(Path.Combine(fixture.Directory, "missing"));
        Assert.Empty(fixture.List.Items);

        await fixture.ViewModel.SetScoreFolderAsync(Path.GetDirectoryName(original)!);
        Assert.Equal(original, Assert.IsType<ScoreFolderFile>(fixture.List.SelectedItem).Path);
        var selected = fixture.List.SelectedItem;
        await fixture.ViewModel.SetScoreFolderAsync(Path.GetDirectoryName(original)! + Path.DirectorySeparatorChar);
        Assert.Same(selected, fixture.List.SelectedItem);
        Assert.Equal(1, fixture.Serializer.Loads);
    });

    [Fact]
    public Task FilteringDeletionAndNewScoreKeepSelectionConsistentWithTheVisibleLibrary() => OnDispatcher(async () =>
    {
        using var fixture = new Fixture();
        var path = fixture.Add("library", "Song");
        await fixture.ViewModel.SetScoreFolderAsync(Path.GetDirectoryName(path)!);
        await fixture.ViewModel.OpenPathAsync(path);
        fixture.ViewModel.ScoreFolderSearch = "unmatched";
        Assert.Null(fixture.List.SelectedItem);
        fixture.ViewModel.ScoreFolderSearch = "";
        Assert.Equal(path, Assert.IsType<ScoreFolderFile>(fixture.List.SelectedItem).Path);

        File.Delete(path);
        await fixture.ViewModel.RefreshScoreFolderAsync();
        Assert.Null(fixture.List.SelectedItem);
        Assert.Equal(path, fixture.ViewModel.CurrentSourcePath); // The editor keeps its loaded document.
        fixture.Add("library", "Song");
        await fixture.ViewModel.RefreshScoreFolderAsync();
        Assert.Equal(path, Assert.IsType<ScoreFolderFile>(fixture.List.SelectedItem).Path);
        Assert.Equal(1, fixture.Serializer.Loads);

        fixture.ViewModel.NewCommand.Execute(null);
        Assert.Null(fixture.ViewModel.CurrentSourcePath);
        Assert.Null(fixture.List.SelectedItem);
        Assert.True(BindingOperations.IsDataBound(fixture.List, Selector.SelectedItemProperty));
    });

    [Fact]
    public Task RenamingCurrentScoreRestoresSelectionToTheNewPath() => OnDispatcher(async () =>
    {
        using var fixture = new Fixture();
        var path = fixture.Add("library", "Song");
        await fixture.ViewModel.SetScoreFolderAsync(Path.GetDirectoryName(path)!);
        await fixture.ViewModel.OpenPathAsync(path);
        Assert.True(await fixture.ViewModel.RenameScoreFileAsync((ScoreFolderFile)fixture.List.SelectedItem, "Renamed"));
        var selected = Assert.IsType<ScoreFolderFile>(fixture.List.SelectedItem);
        Assert.Equal("Renamed", selected.DisplayName);
        Assert.Equal(fixture.ViewModel.CurrentSourcePath, selected.Path);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(selected.Path));
    });

    // Real WPF bindings on an STA dispatcher, without showing windows or assigning keyboard focus.
    private static Task OnDispatcher(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "GPiano-Library-" + Guid.NewGuid().ToString("N"));
        public Serializer Serializer { get; } = new();
        public ScoreWorkspace Workspace { get; }
        public MainWindowViewModel ViewModel { get; }
        public ListBox List { get; } = new();

        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Workspace = new(Serializer, "Untitled");
            ViewModel = new(Workspace, new ThemeService(), new Localization(),
                new UserSettingsService(Path.Combine(Directory, "settings.json")), null!, null!, null!,
                new ScoreRecoveryService(Serializer), null!, () => UnsavedChangesChoice.Cancel);
            List.ItemsSource = ViewModel.ScoreFolderFiles;
            List.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(MainWindowViewModel.CurrentScoreFolderFile))
            { Source = ViewModel, Mode = BindingMode.OneWay });
        }

        public string Add(string folder, string name)
        {
            var path = Path.Combine(Directory, folder, name + ".gpiano");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "test score");
            return path;
        }

        public void Dispose()
        {
            BindingOperations.ClearAllBindings(List);
            System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed class Localization : ILocalizationService
    {
        public AppLanguage CurrentLanguage => AppLanguage.English;
        public event EventHandler? LanguageChanged { add { } remove { } }
        public string GetString(string key, params object?[] arguments) => key;
        public void Apply(AppLanguage language) => throw new NotSupportedException();
    }

    private sealed class Serializer : IScoreDocumentSerializer
    {
        public int Loads { get; private set; }
        public Task<ScoreDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            Loads++;
            return Task.FromResult(ScoreDocument.CreateEmpty(Path.GetFileNameWithoutExtension(path)));
        }
        public Task SaveAsync(ScoreDocument score, string path, CancellationToken cancellationToken = default) =>
            File.WriteAllTextAsync(path, "test score", cancellationToken);
    }
}
