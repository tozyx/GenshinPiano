using System.ComponentModel;
using System.Windows;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.App.Dialogs;

public partial class UpdateSelectionDialog : Window
{
    private readonly UpdateStatusViewModel _updates;
    private readonly UpdateManifest? _application;
    private readonly string? _applicationError;
    private readonly OcrAddonPackageManager _ocr;
    private OcrAddonUpdateCheck _ocrCheck;
    private CancellationTokenSource? _installation;
    private bool _closeAfterCancellation;
    private string? _ocrOperationMessage;

    public UpdateSelectionDialog(UpdateStatusViewModel updates, UpdateManifest? application,
        string? applicationError, OcrAddonPackageManager ocr, OcrAddonUpdateCheck ocrCheck)
    {
        _updates = updates;
        _application = application;
        _applicationError = applicationError;
        _ocr = ocr;
        _ocrCheck = ocrCheck;
        InitializeComponent();
        _updates.PropertyChanged += Status_OnChanged;
        _ocr.StatusChanged += Ocr_OnChanged;
        Closing += OnClosing;
        Activated += (_, _) => Refresh();
        Closed += (_, _) => { _updates.PropertyChanged -= Status_OnChanged; _ocr.StatusChanged -= Ocr_OnChanged; };
        Refresh();
    }

    public bool UpdateApplicationRequested { get; private set; }
    private static string Text(string key, params object?[] values) =>
        string.Format((string)System.Windows.Application.Current.FindResource(key), values);

    private void Status_OnChanged(object? sender, PropertyChangedEventArgs e) => RefreshOnDispatcher();
    private void Ocr_OnChanged(object? sender, EventArgs e) => RefreshOnDispatcher();
    private void RefreshOnDispatcher()
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Refresh()
    {
        var state = _updates.CurrentState;
        var busyApplication = state.Stage is UpdateStage.Downloading or UpdateStage.Verifying or UpdateStage.Ready;
        var applicationUpdateAvailable = _application is not null && _application.Version.CompareTo(_updates.CurrentVersion) > 0;
        var latest = _application?.Version.ToString() ?? (busyApplication ? state.AvailableVersion?.ToString() : null);
        ApplicationVersionText.Text = Text("Update_VersionPair", _updates.CurrentVersionText,
            latest ?? (_applicationError is null ? _updates.CurrentVersionText : Text("Update_UnknownVersion")));
        ApplicationStatusText.Text = _applicationError is not null ? Text("Update_CheckFailed", _applicationError) :
            !applicationUpdateAvailable ? Text("Update_AlreadyCurrent") : Text("Update_NewVersionAvailable");
        if (busyApplication)
            ApplicationStatusText.Text += Environment.NewLine + _updates.StatusText;
        var sameBackgroundVersion = _application is null || state.AvailableVersion?.CompareTo(_application.Version) >= 0;
        if (state.Stage is UpdateStage.Downloading or UpdateStage.Verifying)
            ApplicationStatusText.Text += Environment.NewLine + Text(sameBackgroundVersion
                ? "Update_ReuseBackground" : "Update_WaitBackground");
        ApplicationButton.Content = Text(_updates.CanInstall && sameBackgroundVersion
            ? "Update_InstallApplication" : "Update_UpdateApplication");
        ApplicationButton.IsEnabled = _installation is null && (applicationUpdateAvailable || _updates.CanInstall)
            && (_updates.NetworkAccessEnabled || _updates.CanInstall);

        var check = _ocr.LastCheck ?? _ocrCheck;
        OcrVersionText.Text = Text("Update_VersionPair", check.InstalledVersion ?? Text("Update_NotInstalled"),
            check.Package?.Version.ToString() ?? Text("Update_UnknownVersion"));
        OcrStatusText.Text = _ocrOperationMessage ?? (check.ErrorMessage is not null
            ? Text("Update_CheckFailed", check.ErrorMessage)
            : check.RequiresMigration ? Text("Ocr_ResourceMigrationAvailable", check.Package?.Version)
            : Text(check.IsCurrent ? "Update_AlreadyCurrent" : "Update_NewVersionAvailable"));
        if (_ocr.IsRecognizing) OcrStatusText.Text += Environment.NewLine + Text("Update_OcrRecognitionBusy");
        else if (_ocr.IsInstalling && _installation is null) OcrStatusText.Text += Environment.NewLine + Text("Update_OcrInstalling");
        OcrButton.Content = Text(check.InstalledVersion is null ? "Update_InstallOcr" : "Update_UpdateOcr");
        OcrButton.IsEnabled = _installation is null && !_ocr.IsInstalling && !_ocr.IsRecognizing
            && _updates.NetworkAccessEnabled && check.CanUpdate;
    }

    private void Application_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateApplicationRequested = true;
        DialogResult = true;
    }

    private async void Ocr_OnClick(object sender, RoutedEventArgs e)
    {
        using var cancellation = new CancellationTokenSource();
        _installation = cancellation;
        OcrProgress.Visibility = Visibility.Visible;
        _ocrOperationMessage = Text("Update_OcrInstalling");
        Refresh();
        try
        {
            var progress = new Progress<double>(value => {
                OcrProgress.Value = value;
                _ocrOperationMessage = Text("Ocr_AddonDownloading", value);
                Refresh();
            });
            var result = await _ocr.InstallCheckedUpdateAsync(_ocr.LastCheck ?? _ocrCheck, progress, cancellation.Token);
            _ocrCheck = _ocr.LastCheck ?? _ocrCheck;
            _ocrOperationMessage = Text(result.Updated ? "Ocr_AddonInstalled" : "Ocr_AddonCurrent", result.Version, result.SourceName);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { _ocrOperationMessage = Text("Ocr_AddonDownloadCancelled"); }
        catch (Exception error)
        {
            _ocrOperationMessage = Text("Ocr_AddonDownloadFailed", error.Message);
            AppLogger.Warning($"OCR update from overview failed: {error}");
        }
        finally
        {
            _installation = null;
            OcrProgress.Visibility = Visibility.Collapsed;
            Refresh();
            if (_closeAfterCancellation) Close();
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_installation is null) return;
        e.Cancel = true;
        _closeAfterCancellation = true;
        _installation.Cancel();
    }
}
