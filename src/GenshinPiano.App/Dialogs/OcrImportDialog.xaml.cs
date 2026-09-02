using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GenshinPiano.Application.Ocr;
using GenshinPiano.App.Services;
using Microsoft.Win32;

namespace GenshinPiano.App.Dialogs;

public partial class OcrImportDialog : Window
{
    private readonly IOcrAddonService _service;
    private readonly OcrAddonPackageManager _packageManager;
    private readonly IUserSettingsService _settings;
    private readonly WindowsNotificationService _notifications;
    private readonly WindowsTaskbarProgressService _taskbarProgress;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _downloadCancellation;
    private readonly TaskCompletionSource<bool> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _accepted;

    public OcrImportDialog(
        IOcrAddonService service,
        OcrAddonPackageManager packageManager,
        IUserSettingsService settings,
        WindowsNotificationService notifications,
        WindowsTaskbarProgressService taskbarProgress)
    {
        _service = service;
        _packageManager = packageManager;
        _settings = settings;
        _notifications = notifications;
        _taskbarProgress = taskbarProgress;
        InitializeComponent();
        Closing += OnClosing;
        Closed += OnClosed;
        LoadOptions();
        UpdateAddonStatus();
    }

    public OcrAnalysisResult? Result { get; private set; }

    public string? ImagePath { get; private set; }

    public bool AutoMapTo21Keys => AutoMapTo21CheckBox.IsChecked == true;

    private bool IsAddonAvailable => _service.FindInstalledAddon() is not null;

    public Task<bool> ShowAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (IsVisible)
        {
            Activate();
            return _completion.Task;
        }

        Owner = owner;
        Show();
        Activate();
        return _completion.Task;
    }

    private void UpdateAddonStatus()
    {
        var addon = _service.FindInstalledAddon();
        AddonStatusText.Text = addon is null
            ? string.Format(GetText("Ocr_AddonMissing"), Path.Combine(AppContext.BaseDirectory, "addons", "ocr"))
            : string.Format(GetText("Ocr_AddonReady"), addon.EngineVersion);
        DownloadAddonButton.Content = GetText(addon is null ? "Ocr_DownloadAddon" : "Ocr_CheckAddonUpdate");
        DownloadAddonButton.IsEnabled = _downloadCancellation is null && _settings.Current.Update.NetworkAccessEnabled;
        UpdateAnalyzeState();
    }

    private async void DownloadAddonButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_settings.Current.Update.NetworkAccessEnabled)
        {
            ResultText.Text = GetText("Ocr_NetworkDisabled");
            return;
        }

        _downloadCancellation = new CancellationTokenSource();
        DownloadAddonButton.IsEnabled = false;
        AnalyzeButton.IsEnabled = false;
        AnalysisProgress.Value = 0;
        ResultText.Text = GetText("Ocr_AddonChecking");
        try
        {
            var progress = new Progress<double>(value =>
            {
                AnalysisProgress.Value = value;
                _taskbarProgress.SetProgress(Math.Max(0.01, value));
                ResultText.Text = string.Format(GetText("Ocr_AddonDownloading"), value);
            });
            var result = await _packageManager.DownloadAndInstallAsync(
                progress, _downloadCancellation.Token);
            ResultText.Text = string.Format(
                GetText(result.Updated ? "Ocr_AddonInstalled" : "Ocr_AddonCurrent"),
                result.Version, result.SourceName);
        }
        catch (OperationCanceledException)
        {
            ResultText.Text = GetText("Ocr_AddonDownloadCancelled");
        }
        catch (Exception exception)
        {
            ResultText.Text = string.Format(GetText("Ocr_AddonDownloadFailed"), exception.Message);
            AppLogger.Warning($"OCR add-on download failed: {exception}");
        }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
            _taskbarProgress.Clear();
            UpdateAddonStatus();
        }
    }

    private void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = GetText("Ocr_SelectImage"),
            Filter = GetText("Ocr_ImageFilter"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ImagePath = dialog.FileName;
        ImagePathText.Text = Path.GetFileName(dialog.FileName);
        ImagePathText.ToolTip = dialog.FileName;
        Result = null;
        ImportButton.Visibility = Visibility.Collapsed;
        UpdateAnalyzeState();
    }

    private async void AnalyzeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ImagePath is null ||
            NotationComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<OcrNotationHint>(tag, out var hint) ||
            WatermarkComboBox.SelectedItem is not ComboBoxItem { Tag: string watermarkTag } ||
            !Enum.TryParse<OcrWatermarkMode>(watermarkTag, out var watermarkMode))
        {
            return;
        }

        SetBusy(true);
        AnalysisProgress.IsIndeterminate = false;
        AnalysisProgress.Value = 0;
        _taskbarProgress.SetProgress(0.01);
        ResultText.Text = GetText("Ocr_StagePreparing");
        _analysisCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<OcrProgressUpdate>(OnOcrProgress);
            Result = await _service.AnalyzeAsync(
                ImagePath,
                hint,
                System.Globalization.CultureInfo.CurrentUICulture.Name,
                watermarkMode,
                AccompanimentCheckBox.IsChecked == true,
                progress,
                cancellationToken: _analysisCancellation.Token);
            if (!Result.Success || Result.Score is null)
            {
                var errorText = Result.ErrorMessage ?? Result.ErrorCode ?? GetText("Ocr_UnknownError");
                ResultText.Text = string.Format(
                    GetText("Ocr_Failed"),
                    errorText);
                NotifyCompletionIfInactive(success: false, errorText);
                return;
            }

            var noteCount = Result.Score.Tracks.Sum(track => track.Notes.Count);
            ResultText.Text = string.Format(
                GetText("Ocr_Completed"),
                noteCount,
                Result.Confidence);
            ImportButton.Visibility = Visibility.Visible;
            NotifyCompletionIfInactive(
                success: true,
                string.Format(GetText("Ocr_NotificationCompletedBody"), noteCount));
        }
        catch (OperationCanceledException)
        {
            ResultText.Text = GetText("Ocr_Cancelled");
        }
        catch (Exception exception)
        {
            ResultText.Text = string.Format(GetText("Ocr_Failed"), exception.Message);
            NotifyCompletionIfInactive(success: false, exception.Message);
        }
        finally
        {
            _analysisCancellation?.Dispose();
            _analysisCancellation = null;
            AnalysisProgress.IsIndeterminate = false;
            AnalysisProgress.Value = Result?.Success == true ? 1 : 0;
            _taskbarProgress.Clear();
            SetBusy(false);
        }
    }

    private void NotifyCompletionIfInactive(bool success, string message)
    {
        if (!_settings.Current.Notifications.NotifyWhenOcrCompletes ||
            IsActive || Owner?.IsActive == true)
        {
            return;
        }

        _notifications.Show(
            GetText(success ? "Ocr_NotificationCompletedTitle" : "Ocr_NotificationFailedTitle"),
            message);
    }

    private void SetBusy(bool busy)
    {
        AnalyzeButton.IsEnabled = !busy && _downloadCancellation is null && IsAddonAvailable && ImagePath is not null;
        NotationComboBox.IsEnabled = !busy;
        WatermarkComboBox.IsEnabled = !busy;
        AccompanimentCheckBox.IsEnabled = !busy;
        AutoMapTo21CheckBox.IsEnabled = !busy;
        CancelButton.Content = GetText(busy ? "Ocr_CancelAnalysis" : "Common_Cancel");
    }

    private void OnOcrProgress(OcrProgressUpdate update)
    {
        AnalysisProgress.Value = update.Progress;
        _taskbarProgress.SetProgress(update.Progress);
        ResultText.Text = GetText(update.Stage switch
        {
            OcrProgressStage.Preparing => "Ocr_StagePreparing",
            OcrProgressStage.WatermarkSuppression => "Ocr_StageWatermark",
            OcrProgressStage.TextDetection => "Ocr_StageTextDetection",
            OcrProgressStage.SuperResolution => "Ocr_StageSuperResolution",
            OcrProgressStage.ScoreReconstruction => "Ocr_StageReconstruction",
            _ => "Ocr_Analyzing",
        });
    }

    private void UpdateAnalyzeState() =>
        AnalyzeButton.IsEnabled = _analysisCancellation is null && _downloadCancellation is null &&
                                  IsAddonAvailable && ImagePath is not null;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_analysisCancellation is not null)
        {
            _analysisCancellation.Cancel();
            return;
        }

        if (_downloadCancellation is not null)
        {
            _downloadCancellation.Cancel();
            return;
        }

        Close();
    }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Result?.Success != true || Result.Score is null)
        {
            return;
        }

        _accepted = true;
        Close();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => CancelButton_OnClick(sender, e);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_analysisCancellation is null && _downloadCancellation is null)
        {
            return;
        }

        e.Cancel = true;
        _analysisCancellation?.Cancel();
        _downloadCancellation?.Cancel();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SaveOptions();
        _completion.TrySetResult(_accepted);
    }

    private void LoadOptions()
    {
        var options = _settings.Current.Ocr;
        SelectComboItem(NotationComboBox, options.NotationHint);
        SelectComboItem(WatermarkComboBox, options.WatermarkMode);
        AccompanimentCheckBox.IsChecked = options.IncludeAccompaniment;
        AutoMapTo21CheckBox.IsChecked = options.AutoMapTo21Keys;
    }

    private void SaveOptions()
    {
        var notation = (NotationComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Numbered";
        var watermark = (WatermarkComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "Auto";
        _settings.SetOcrOptions(
            notation,
            watermark,
            AccompanimentCheckBox.IsChecked == true,
            AutoMapTo21Keys);
    }

    private static void SelectComboItem(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal))
            ?? comboBox.Items[0];
    }

    private static string GetText(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as string ?? key;
}
