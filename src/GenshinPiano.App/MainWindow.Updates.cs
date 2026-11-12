using System.Windows;
using GenshinPiano.App.Dialogs;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.App;

public partial class MainWindow
{
    private UpdateSelectionDialog? _updateSelectionDialog;

    private async Task UpdateStatus_OnManualCheckRequested(CancellationToken cancellationToken)
    {
        if (DataContext is not MainWindowViewModel model || !model.UpdateStatus.NetworkAccessEnabled) return;
        if (_updateSelectionDialog is not null) { _updateSelectionDialog.Activate(); return; }
        var app = (App)System.Windows.Application.Current;
        var applicationCheck = CheckApplicationAsync();
        var ocrCheck = app.OcrAddonPackageManager.CheckForUpdatesAsync(cancellationToken);
        await Task.WhenAll(applicationCheck, ocrCheck);
        cancellationToken.ThrowIfCancellationRequested();
        model.UpdateStatus.FinishVersionCheck();
        var (manifest, error) = await applicationCheck;
        var dialog = new UpdateSelectionDialog(model.UpdateStatus, manifest, error,
            app.OcrAddonPackageManager, await ocrCheck) { Owner = this };
        _updateSelectionDialog = dialog;
        try { dialog.ShowDialog(); }
        finally { _updateSelectionDialog = null; }
        if (!dialog.UpdateApplicationRequested) return;
        if (manifest is not null)
            await model.UpdateStatus.DownloadSelectedUpdateAsync(manifest, cancellationToken);
        if (model.UpdateStatus.CanInstall) await ShowReadyUpdateDialogAsync(model);

        async Task<(UpdateManifest? Manifest, string? Error)> CheckApplicationAsync()
        {
            try
            {
                var latest = await model.UpdateStatus.CheckApplicationVersionAsync(cancellationToken);
                return latest is null ? (null, (string)FindResource("Update_NoCompatibleRelease")) : (latest, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                AppLogger.Warning($"Manual application update check failed: {exception}");
                return (null, exception.Message);
            }
        }
    }
}
