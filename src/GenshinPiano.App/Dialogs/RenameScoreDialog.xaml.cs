using System.IO;
using System.Windows;
using System.Windows.Input;

namespace GenshinPiano.App.Dialogs;

public partial class RenameScoreDialog : Window
{
    public RenameScoreDialog(string currentTitle)
    {
        InitializeComponent();
        NameTextBox.Text = currentTitle;
        NameTextBox.SelectAll();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    public string NewTitle => NameTextBox.Text.Trim();

    private void NameTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        var title = NameTextBox.Text.Trim();
        var invalid = title.Length == 0 || title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
        RenameButton.IsEnabled = !invalid;
        ErrorText.Text = invalid && title.Length > 0
            ? (string)FindResource("Dialog_RenameScoreInvalid")
            : string.Empty;
    }

    private void RenameButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
