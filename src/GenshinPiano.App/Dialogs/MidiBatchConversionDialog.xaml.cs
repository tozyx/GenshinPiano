using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using GenshinPiano.Application.Conversion;
using Microsoft.Win32;

namespace GenshinPiano.App.Dialogs;

public partial class MidiBatchConversionDialog : Window
{
    private readonly MidiBatchConversionService _conversionService;
    private string? _sourceDirectory;
    private string? _outputDirectory;
    private CancellationTokenSource? _conversionCancellation;

    public MidiBatchConversionDialog(MidiBatchConversionService conversionService)
    {
        _conversionService = conversionService;
        InitializeComponent();
        Closing += OnClosing;
    }

    private void BrowseSource_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = GetText("MidiBatch_SourceDialogTitle"),
            InitialDirectory = _sourceDirectory,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _sourceDirectory = dialog.FolderName;
            ShowFolderName(SourceFolderText, _sourceDirectory);
            UpdateConvertState();
        }
    }

    private void BrowseOutput_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = GetText("MidiBatch_OutputDialogTitle"),
            InitialDirectory = _outputDirectory,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _outputDirectory = dialog.FolderName;
            ShowFolderName(OutputFolderText, _outputDirectory);
            UpdateConvertState();
        }
    }

    private async void ConvertButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_sourceDirectory is null || _outputDirectory is null)
        {
            return;
        }

        var fileCount = Directory.EnumerateFiles(_sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            .Count(path => Path.GetExtension(path) is { } extension &&
                (extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".midi", StringComparison.OrdinalIgnoreCase)));
        ConversionProgress.Maximum = Math.Max(1, fileCount);
        ConversionProgress.Value = 0;
        ResultText.Text = string.Format(GetText("MidiBatch_Converting"), fileCount);
        SetControlsEnabled(false);
        _conversionCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<int>(value => ConversionProgress.Value = value);
            var result = await _conversionService.ConvertDirectoryAsync(
                _sourceDirectory,
                _outputDirectory,
                progress: progress,
                cancellationToken: _conversionCancellation.Token);
            ResultText.Text = string.Format(
                GetText("MidiBatch_Completed"),
                result.ConvertedCount,
                result.SkippedCount,
                result.FailedCount);
        }
        catch (OperationCanceledException)
        {
            ResultText.Text = GetText("MidiBatch_Cancelled");
        }
        catch (Exception exception)
        {
            ResultText.Text = string.Format(GetText("MidiBatch_Failed"), exception.Message);
        }
        finally
        {
            _conversionCancellation?.Dispose();
            _conversionCancellation = null;
            SetControlsEnabled(true);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        ConvertButton.IsEnabled = enabled && _sourceDirectory is not null && _outputDirectory is not null;
    }

    private void UpdateConvertState() =>
        ConvertButton.IsEnabled = _conversionCancellation is null &&
            _sourceDirectory is not null && _outputDirectory is not null;

    private static void ShowFolderName(System.Windows.Controls.TextBlock target, string path)
    {
        target.Text = new DirectoryInfo(Path.TrimEndingDirectorySeparator(path)).Name;
        target.ToolTip = path;
    }

    private static string GetText(string key) =>
        System.Windows.Application.Current.TryFindResource(key) as string ?? key;

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e) => _conversionCancellation?.Cancel();
}
