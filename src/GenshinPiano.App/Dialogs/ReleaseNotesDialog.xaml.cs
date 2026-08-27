using System;
using System.Windows;

namespace GenshinPiano.App.Dialogs;

public partial class ReleaseNotesDialog : Window
{
    public ReleaseNotesDialog(string version, string? notes)
        : this("Update_InstalledTitle", version, notes)
    {
    }

    public ReleaseNotesDialog(string titleKey, string version, string? notes)
    {
        InitializeComponent();
        var titleFormat = (string)FindResource(titleKey);
        TitleText.Text = titleFormat.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(titleFormat, version)
            : titleFormat;
        var content = string.IsNullOrWhiteSpace(notes)
            ? (string)FindResource("Update_NoReleaseNotes")
            : notes;
        NotesViewer.Document = LightweightMarkdownRenderer.Render(
            content,
            (System.Windows.Media.Brush)FindResource("PrimaryTextBrush"),
            (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"),
            (System.Windows.Media.Brush)FindResource("AccentBrush"),
            (System.Windows.Media.Brush)FindResource("PanelBackgroundBrush"));
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
