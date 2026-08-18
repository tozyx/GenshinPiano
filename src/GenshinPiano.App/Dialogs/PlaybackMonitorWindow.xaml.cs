using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace GenshinPiano.App.Dialogs;

public partial class PlaybackMonitorWindow : Window
{
    private bool _restoreEditorOnClose = true;

    public PlaybackMonitorWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? ReturnToEditorRequested;

    public void CloseWithoutRestoringEditor()
    {
        _restoreEditorOnClose = false;
        Close();
    }

    private void PlaybackMonitorWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 24;
        Top = workArea.Top + 24;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ReturnButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void PlaybackMonitorWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_restoreEditorOnClose)
        {
            ReturnToEditorRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
