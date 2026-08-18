using System.Windows;
using System.Windows.Input;

namespace GenshinPiano.App.Dialogs;

public enum UnsavedChangesChoice
{
    Cancel,
    DontSave,
    Save,
}

public partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    public UnsavedChangesChoice Choice { get; private set; } = UnsavedChangesChoice.Cancel;

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e) => CloseWith(UnsavedChangesChoice.Save);

    private void DontSaveButton_OnClick(object sender, RoutedEventArgs e) =>
        CloseWith(UnsavedChangesChoice.DontSave);

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => CloseWith(UnsavedChangesChoice.Cancel);

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseWith(UnsavedChangesChoice.Cancel);
        }
    }

    private void CloseWith(UnsavedChangesChoice choice)
    {
        Choice = choice;
        Close();
    }
}
