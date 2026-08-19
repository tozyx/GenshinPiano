using System.Windows;

namespace GenshinPiano.App.Dialogs;

public partial class RecoveryDialog : Window
{
    public RecoveryDialog(DateTimeOffset savedAt)
    {
        InitializeComponent();
        MessageText.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            (string)FindResource("Dialog_RecoveryMessage"),
            savedAt.LocalDateTime.ToString("g"));
    }

    public bool ShouldRestore { get; private set; }

    private void RestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShouldRestore = true;
        Close();
    }

    private void DiscardButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
