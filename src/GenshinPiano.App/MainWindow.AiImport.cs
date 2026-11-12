using System.Windows;
using GenshinPiano.App.Controls;
using GenshinPiano.App.Dialogs;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Infrastructure.AiScores;

namespace GenshinPiano.App;

public partial class MainWindow
{
    private async void ImportAiScoreMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        var dialog = new AiScoreImportDialog(new AiScoreJsonImporter()) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } result) return;
        if (viewModel.IsDirty)
        {
            var unsaved = new UnsavedChangesDialog { Owner = this };
            unsaved.ShowDialog();
            if (unsaved.Choice == UnsavedChangesChoice.Cancel) return;
            if (unsaved.Choice == UnsavedChangesChoice.Save && !await viewModel.SavePendingChangesAsync()) return;
            if (unsaved.Choice == UnsavedChangesChoice.DontSave) viewModel.DiscardRecovery();
        }
        viewModel.ImportAiScore(result.Score);
        PianoRollEditor.SetPitchLayoutMode(PianoRollPitchLayoutMode.Piano88);
        ShowWorkspacePage(showPractice: false);
    }
}
