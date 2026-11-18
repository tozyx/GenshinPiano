using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GenshinPiano.Application.TextScores;
using Microsoft.Win32;

namespace GenshinPiano.App.Dialogs;

public partial class TextScoreImportDialog : Window
{
    private sealed record SectionEditor(TextScoreSectionInfo Info, ComboBox Group, TextBox Meter, TextBox Width, ComboBox Tail);
    private readonly ITextScoreImporter _importer;
    private readonly List<SectionEditor> _sections = [];
    private bool _ready;
    private bool _busy;
    private bool _closed;
    private TextScoreImportResult? _validated;
    private TextScoreProfile? _resolvedProfile;

    public TextScoreImportDialog(ITextScoreImporter importer)
    {
        _importer = importer;
        InitializeComponent();
        foreach (var profile in Enum.GetValues<TextScoreProfile>()) AddChoice(ProfileBox, T("TextImport_Profile_" + profile), profile);
        foreach (var notation in Enum.GetValues<TextScoreNotation>()) AddChoice(NotationBox, T("TextImport_Notation_" + notation), notation);
        ProfileBox.SelectedIndex = NotationBox.SelectedIndex = 0;
        ScoreTitleBox.Text = T("Score_Untitled");
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 24);
        Height = Math.Min(Height, area.Height - 24);
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        Closed += (_, _) => _closed = true;
        _ready = true;
        ProfileHelpText.Text = T("TextImport_Help_Unspecified");
        RefreshButtons();
    }

    public TextScoreImportResult? Result { get; private set; }
    private string T(string key, params object?[] args)
    {
        var template = (string)FindResource(key);
        return args.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, args);
    }
    private TextScoreProfile Profile => Selected<TextScoreProfile>(ProfileBox);
    private TextScoreProfile EffectiveProfile => _resolvedProfile ?? Profile;
    private TextScoreNotation Notation => Selected<TextScoreNotation>(NotationBox);
    private static TValue Selected<TValue>(ComboBox box) => (TValue)((ComboBoxItem)box.SelectedItem).Tag;
    private static void AddChoice(ComboBox box, string label, object value) => box.Items.Add(new ComboBoxItem { Content = label, Tag = value });

    private void Source_OnChanged(object sender, TextChangedEventArgs e) => Invalidate(true);
    private void SourceOption_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ProfileHelpText.Text = T("TextImport_Help_" + Profile);
        SpacesBox.IsChecked = false;
        Invalidate(true);
    }
    private void Timing_OnChanged(object sender, RoutedEventArgs e) => Invalidate(false);

    private void Invalidate(bool clearSections)
    {
        if (!_ready) return;
        _validated = Result = null;
        ReviewBox.IsChecked = false;
        ReportBox.Clear();
        SummaryText.Text = T("TextImport_Ready");
        if (clearSections)
        {
            _resolvedProfile = null;
            _sections.Clear();
            SectionsPanel.Children.Clear();
            ProfileHelpText.Text = T("TextImport_Help_" + Profile);
        }
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        AnalyzeButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(SourceBox.Text);
        PreviewButton.IsEnabled = !_busy && _sections.Count > 0;
        ReviewBox.IsEnabled = !_busy && _validated is not null;
        ImportButton.IsEnabled = !_busy && _validated is not null && ReviewBox.IsChecked == true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SourceBox.IsEnabled = OptionsPanel.IsEnabled = SectionsPanel.IsEnabled = PasteButton.IsEnabled = FileButton.IsEnabled = !busy;
        RefreshButtons();
    }

    private async void Analyze_OnClick(object sender, RoutedEventArgs e)
    {
        var source = SourceBox.Text;
        var profile = Profile;
        var notation = Notation;
        Invalidate(true);
        SetBusy(true);
        try
        {
            string? detectionSummary = null;
            if (profile == TextScoreProfile.Unspecified)
            {
                var detection = await Task.Run(() => _importer.Detect(source, notation));
                if (_closed) return;
                if (detection.CertainProfile is not { } certain)
                {
                    SummaryText.Text = T("TextImport_DetectAmbiguous", T("TextImport_Notation_" + detection.Notation));
                    var suggestion = detection.SuggestedProfile is { } suggested
                        ? T("TextImport_DetectSuggestion", T("TextImport_Profile_" + suggested)) + Environment.NewLine : "";
                    ReportBox.Text = suggestion + T("TextImport_DetectCandidates",
                        string.Join(" / ", detection.Candidates.Select(candidate => T("TextImport_Profile_" + candidate)))) +
                        Environment.NewLine + T("TextImport_DetectEvidence_" + detection.EvidenceCode);
                    return;
                }
                profile = certain;
                _resolvedProfile = certain;
                detectionSummary = T("TextImport_DetectCertain", T("TextImport_Profile_" + certain));
                ProfileHelpText.Text = detectionSummary + Environment.NewLine + T("TextImport_Help_" + certain);
            }
            var analysis = await Task.Run(() => _importer.Analyze(source, profile, notation));
            if (_closed) return;
            foreach (var info in analysis.Sections) AddSection(info);
            SummaryText.Text = (detectionSummary is null ? "" : detectionSummary + " ") +
                T("TextImport_Analyzed", analysis.Sections.Count, T("TextImport_Notation_" + analysis.Notation));
            if (detectionSummary is not null) ReportBox.Text = T("TextImport_DetectEvidence_UniqueGrammar");
        }
        catch (TextScoreImportException ex) { if (!_closed) ShowError(ex); }
        catch (Exception ex) { Unexpected(ex); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void AddSection(TextScoreSectionInfo info)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 5) };
        var sectionName = string.IsNullOrEmpty(info.Name) ? T("TextImport_Section", info.Index + 1) : info.Name;
        row.Children.Add(new TextBlock
        {
            Text = sectionName,
            Width = 110, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = sectionName + Environment.NewLine + T("TextImport_Location", info.FirstLine, info.RowCount),
        });
        var group = new ComboBox { Width = 65, Height = 30 };
        foreach (var value in new[] { 1, 2, 4, 8, 16, 32 }) group.Items.Add(value);
        group.SelectedItem = 4;
        var meter = new TextBox { Text = "4/4", Width = 60, Height = 30, MaxLength = 6 };
        var width = new TextBox { Text = info.SuggestedGroupsPerRow.ToString(CultureInfo.InvariantCulture), Width = 45, Height = 30, MaxLength = 2 };
        var tail = new ComboBox { Width = 160, Height = 30 };
        foreach (var value in Enum.GetValues<TextScoreTrailingSlash>()) AddChoice(tail, T("TextImport_Tail_" + value), value);
        tail.SelectedIndex = 0;
        var grouped = EffectiveProfile is TextScoreProfile.Zhijian or TextScoreProfile.Xingli;
        group.IsEnabled = width.IsEnabled = grouped;
        AddField("TextImport_Group", group);
        AddField("TextImport_Meter", meter);
        AddField("TextImport_Width", width);
        AddField("TextImport_Tail", tail);
        _sections.Add(new(info, group, meter, width, tail));
        SectionsPanel.Children.Add(row);
        group.SelectionChanged += (_, _) => Invalidate(false);
        tail.SelectionChanged += (_, _) => Invalidate(false);
        meter.TextChanged += (_, _) => Invalidate(false);
        width.TextChanged += (_, _) => Invalidate(false);

        void AddField(string key, Control control)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            panel.Children.Add(new TextBlock { Text = T(key), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            control.ToolTip = T(key);
            panel.Children.Add(control);
            row.Children.Add(panel);
        }
    }

    private async void Preview_OnClick(object sender, RoutedEventArgs e)
    {
        Invalidate(false);
        SetBusy(true);
        try
        {
            if (!double.TryParse(BpmBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var bpm)) throw new TextScoreImportException("Options");
            var settings = _sections.Select(editor =>
            {
                var meter = editor.Meter.Text.Split('/');
                if (meter.Length != 2 || !int.TryParse(meter[0], out var numerator) || !int.TryParse(meter[1], out var denominator) ||
                    !int.TryParse(editor.Width.Text, out var width)) throw new TextScoreImportException("Options", editor.Info.FirstLine);
                return new TextScoreSectionOptions { Index = editor.Info.Index, Numerator = numerator, Denominator = denominator,
                    GroupDenominator = (int)editor.Group.SelectedItem, GroupsPerRow = width, TrailingSlash = Selected<TextScoreTrailingSlash>(editor.Tail) };
            }).ToArray();
            var options = new TextScoreImportOptions { Profile = EffectiveProfile, Notation = Notation, Title = ScoreTitleBox.Text,
                Bpm = bpm, SpacesAreRests = SpacesBox.IsChecked == true, Sections = settings };
            var source = SourceBox.Text;
            var result = await Task.Run(() => _importer.Parse(source, options));
            if (_closed) return;
            _validated = result;
            SummaryText.Text = T("TextImport_Summary", result.AttackCount, result.Score.Tracks.Sum(track => track.Notes.Count), result.EndTick / 480d);
            ReportBox.Text = string.Join(Environment.NewLine, result.Warnings.Select(warning =>
                T("TextImport_Warning_" + warning.Code, warning.Line, warning.Detail)));
        }
        catch (TextScoreImportException ex) { if (!_closed) ShowError(ex); }
        catch (Exception ex) { Unexpected(ex); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void ShowError(TextScoreImportException ex)
    {
        _validated = Result = null;
        SummaryText.Text = T("TextImport_Failed");
        ReportBox.Text = T("TextImport_ErrorLocation", ex.Line, ex.Column, T("TextImport_Error_" + ex.Code), ex.Detail);
        RefreshButtons();
    }

    private void Unexpected(Exception ex)
    {
        Services.AppLogger.Error("Text score import failed.", ex);
        if (_closed) return;
        _validated = Result = null;
        SummaryText.Text = T("TextImport_Failed");
        ReportBox.Text = T("AiImport_UnexpectedError");
    }

    private void Paste_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = Clipboard.GetText();
            if (text.Length > TextScoreImportLimits.MaxTextLength) { ShowError(new("Limit")); return; }
            SourceBox.Text = text;
        }
        catch (ExternalException) { SummaryText.Text = T("AiImport_ClipboardError"); }
    }

    private async void File_OnClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = T("TextImport_Open"), Filter = T("TextImport_FileFilter"), CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        Invalidate(true);
        SetBusy(true);
        try
        {
            if (new FileInfo(picker.FileName).Length > TextScoreImportLimits.MaxTextLength * 4L) throw new TextScoreImportException("Limit");
            var text = await File.ReadAllTextAsync(picker.FileName, new UTF8Encoding(false, true));
            if (_closed) return;
            if (text.Length > TextScoreImportLimits.MaxTextLength) throw new TextScoreImportException("Limit");
            SourceBox.Text = text;
            ScoreTitleBox.Text = Path.GetFileNameWithoutExtension(picker.FileName);
        }
        catch (DecoderFallbackException) { if (!_closed) ShowError(new("Encoding")); }
        catch (TextScoreImportException ex) { if (!_closed) ShowError(ex); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (!_closed) ShowError(new("File")); }
        finally { if (!_closed) SetBusy(false); }
    }

    private void Review_OnChanged(object sender, RoutedEventArgs e) { if (_ready) RefreshButtons(); }
    private void Import_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshButtons();
        if (!ImportButton.IsEnabled) return;
        Result = _validated;
        DialogResult = true;
    }
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();
    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
}
