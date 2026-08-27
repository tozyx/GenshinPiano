using System.Windows;

namespace GenshinPiano.App.Dialogs;

public partial class UpdateReadyDialog : Window
{
    public UpdateReadyDialog(string version)
    {
        InitializeComponent();
        MessageText.Text = string.Format((string)FindResource("Update_ReadyMessage"), version);
    }

    public UpdateReadyDialog(string titleKey, string messageKey, string primaryActionKey)
    {
        InitializeComponent();
        TitleText.Text = (string)FindResource(titleKey);
        MessageText.Text = (string)FindResource(messageKey);
        PrimaryButton.Content = FindResource(primaryActionKey);
    }

    public bool RestartRequested { get; private set; }
    private void Later_OnClick(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Restart_OnClick(object sender, RoutedEventArgs e) { RestartRequested = true; DialogResult = true; Close(); }
}
