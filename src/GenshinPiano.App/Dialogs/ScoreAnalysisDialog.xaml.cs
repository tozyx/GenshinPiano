using System.Windows;
using System.Windows.Controls;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Dialogs;

public partial class ScoreAnalysisDialog : Window
{
    private readonly ScoreQualityReport _report;

    public ScoreAnalysisDialog(ScoreQualityReport report)
    {
        InitializeComponent();
        _report = report;
        TotalNotesText.Text = report.TotalNotes.ToString();
        UnmappedNotesText.Text = report.UnmappedNotes.ToString();
        DuplicateNotesText.Text = report.DuplicateNotes.ToString();
        OverlappingNotesText.Text = report.OverlappingNotes.ToString();
        VeryShortNotesText.Text = report.VeryShortNotes.ToString();
        RemoveDuplicatesCheckBox.IsEnabled = report.DuplicateNotes > 0;
        RemoveDuplicatesCheckBox.IsChecked = report.DuplicateNotes > 0;
        TrimOverlapsCheckBox.IsEnabled = report.OverlappingNotes > 0;
        RemoveVeryShortCheckBox.IsEnabled = report.VeryShortNotes > 0;
        UpdateCleanupButton();
        DownOctaveButton.IsEnabled = report.CanShiftKeySteps(-7);
        DownSemitoneButton.IsEnabled = report.CanShiftKeySteps(-1);
        UpSemitoneButton.IsEnabled = report.CanShiftKeySteps(1);
        UpOctaveButton.IsEnabled = report.CanShiftKeySteps(7);
    }

    public int? KeySteps { get; private set; }

    public ScoreCleanupOptions CleanupOptions { get; private set; }

    private void CleanupOption_OnChanged(object sender, RoutedEventArgs e) => UpdateCleanupButton();

    private void UpdateCleanupButton()
    {
        if (ApplyCleanupButton is not null)
        {
            ApplyCleanupButton.IsEnabled = RemoveDuplicatesCheckBox.IsChecked == true ||
                                           TrimOverlapsCheckBox.IsChecked == true ||
                                           RemoveVeryShortCheckBox.IsChecked == true;
        }
    }

    private void ApplyCleanup_OnClick(object sender, RoutedEventArgs e)
    {
        CleanupOptions = ScoreCleanupOptions.None;
        if (RemoveDuplicatesCheckBox.IsChecked == true)
        {
            CleanupOptions |= ScoreCleanupOptions.RemoveExactDuplicates;
        }

        if (TrimOverlapsCheckBox.IsChecked == true)
        {
            CleanupOptions |= ScoreCleanupOptions.TrimSamePitchOverlaps;
        }

        if (RemoveVeryShortCheckBox.IsChecked == true)
        {
            CleanupOptions |= ScoreCleanupOptions.RemoveVeryShortNotes;
        }

        if (CleanupOptions == ScoreCleanupOptions.None)
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Transpose_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } || !int.TryParse(value, out var keySteps) ||
            !_report.CanShiftKeySteps(keySteps))
        {
            return;
        }

        KeySteps = keySteps;
        DialogResult = true;
        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
