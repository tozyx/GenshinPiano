using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using GenshinPiano.Application.AiScores;

namespace GenshinPiano.App.Dialogs;

public partial class AiScoreImportDialog : Window
{
    private readonly IAiScoreImporter _importer;
    private AiScoreImportResult? _validated;
    private bool _busy;
    private bool _closed;

    public AiScoreImportDialog(IAiScoreImporter importer)
    {
        _importer = importer;
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 24);
        Height = Math.Min(Height, area.Height - 24);
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        Closed += (_, _) => _closed = true;
    }

    public AiScoreImportResult? Result { get; private set; }

    private string Text(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, (string)FindResource(key), args);

    private void CopyPrompt_OnClick(object sender, RoutedEventArgs e) =>
        Copy(AiScorePrompt.Create(CultureInfo.CurrentUICulture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)), "AiImport_PromptCopied");

    private void CopyReport_OnClick(object sender, RoutedEventArgs e) =>
        Copy(ReviewTextBox.Text, "AiImport_ReportCopied");

    private void Copy(string text, string successKey)
    {
        try
        {
            Clipboard.SetText(text);
            SummaryText.Text = Text(successKey);
        }
        catch (ExternalException) { SummaryText.Text = Text("AiImport_ClipboardError"); }
    }

    private void Paste_OnClick(object sender, RoutedEventArgs e)
    {
        try { JsonTextBox.Text = Clipboard.GetText(); }
        catch (ExternalException) { SummaryText.Text = Text("AiImport_ClipboardError"); }
    }

    private async void OpenFile_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Text("AiImport_OpenFile"),
            Filter = Text("AiImport_FileFilter"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;
        _validated = null;
        Result = null;
        ReviewCheckBox.Visibility = Visibility.Collapsed;
        SummaryText.Text = Text("AiImport_Checking");
        ReviewTextBox.Clear();
        CopyReportButton.IsEnabled = false;
        SetBusy(true);
        try
        {
            if (new FileInfo(dialog.FileName).Length > AiScoreImportLimits.MaxTextLength * 4L)
                throw new AiScoreImportException("Limit", "$");
            var json = await File.ReadAllTextAsync(dialog.FileName, new UTF8Encoding(false, true));
            if (_closed) return;
            if (json.Length > AiScoreImportLimits.MaxTextLength) throw new AiScoreImportException("Limit", "$");
            JsonTextBox.Text = json;
        }
        catch (AiScoreImportException exception) { ShowError(exception); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            if (!_closed)
            {
                SummaryText.Text = Text("AiImport_Failed");
                ReviewTextBox.Text = Text("AiImport_FileError", exception.Message);
                CopyReportButton.IsEnabled = true;
            }
        }
        finally { if (!_closed) SetBusy(false); }
    }

    private void JsonText_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (ImportButton is null) return;
        _validated = null;
        Result = null;
        ImportButton.IsEnabled = false;
        ReviewCheckBox.IsChecked = false;
        ReviewCheckBox.Visibility = Visibility.Collapsed;
        ReviewTextBox.Clear();
        CopyReportButton.IsEnabled = false;
        SummaryText.Text = Text("AiImport_Ready");
        ValidateButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(JsonTextBox.Text);
    }

    private async void Validate_OnClick(object sender, RoutedEventArgs e)
    {
        var json = JsonTextBox.Text;
        SetBusy(true);
        _validated = null;
        SummaryText.Text = Text("AiImport_Checking");
        ReviewCheckBox.IsChecked = false;
        try
        {
            var result = await Task.Run(() => _importer.Parse(json));
            if (_closed) return;
            _validated = result;
            SummaryText.Text = Text("AiImport_Summary", result.Score.Metadata.Title, result.MeasureCount,
                result.Score.Tracks.Count, result.Score.Tracks.Sum(track => track.Notes.Count));
            ReviewTextBox.Text = result.Warnings.Count == 0 ? Text("AiImport_Valid") :
                string.Join(Environment.NewLine, result.Warnings.Select(warning =>
                    Text("AiImport_Warning_" + warning.Code, warning.Location, warning.Detail)));
            ReviewCheckBox.Visibility = result.Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            CopyReportButton.IsEnabled = true;
        }
        catch (AiScoreImportException exception) { if (!_closed) ShowError(exception); }
        catch (Exception exception)
        {
            Services.AppLogger.Error("AI score import failed.", exception);
            if (!_closed)
            {
                SummaryText.Text = Text("AiImport_Failed");
                ReviewTextBox.Text = Text("AiImport_UnexpectedError");
                CopyReportButton.IsEnabled = true;
            }
        }
        finally { if (!_closed) SetBusy(false); }
    }

    private void ShowError(AiScoreImportException exception)
    {
        _validated = null;
        ReviewCheckBox.Visibility = Visibility.Collapsed;
        SummaryText.Text = Text("AiImport_Failed");
        ReviewTextBox.Text = Text("AiImport_Problem", exception.Location, Text("AiImport_Error_" + exception.Code));
        CopyReportButton.IsEnabled = true;
        ImportButton.IsEnabled = false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        JsonTextBox.IsEnabled = PasteButton.IsEnabled = FileButton.IsEnabled = !busy;
        ValidateButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(JsonTextBox.Text);
        UpdateImportState();
    }

    private void Review_OnChanged(object sender, RoutedEventArgs e) => UpdateImportState();

    private void UpdateImportState()
    {
        if (ImportButton is not null)
            ImportButton.IsEnabled = !_busy && _validated is not null &&
                (_validated.Warnings.Count == 0 || ReviewCheckBox.IsChecked == true);
    }

    private void Import_OnClick(object sender, RoutedEventArgs e)
    {
        UpdateImportState();
        if (!ImportButton.IsEnabled) return;
        Result = _validated;
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
