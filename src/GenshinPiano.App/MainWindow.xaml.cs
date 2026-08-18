using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using GenshinPiano.App.Dialogs;
using GenshinPiano.App.ViewModels;

namespace GenshinPiano.App;

public partial class MainWindow : Window
{
    private const int WindowStyleIndex = -16;
    private const int NativeCaption = 0x00C00000;
    private const int NativeSystemMenu = 0x00080000;
    private const int NativeThickFrame = 0x00040000;
    private const int NativeMinimizeBox = 0x00020000;
    private const int NativeMaximizeBox = 0x00010000;
    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint FrameChanged = 0x0020;
    private bool _allowClose;
    private bool _closePromptActive;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        EnableNativeWindowAnimations();
    }

    private void EnableNativeWindowAnimations()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLong(handle, WindowStyleIndex);
        style |= NativeCaption |
                 NativeSystemMenu |
                 NativeThickFrame |
                 NativeMinimizeBox |
                 NativeMaximizeBox;
        SetWindowLong(handle, WindowStyleIndex, style);
        SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NoSize | NoMove | NoZOrder | NoActivate | FrameChanged);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr windowHandle, int index, int newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) == 0 || (modifiers & ModifierKeys.Alt) != 0)
        {
            return;
        }

        ICommand? command = e.Key switch
        {
            Key.N when (modifiers & ModifierKeys.Shift) == 0 => viewModel.NewCommand,
            Key.O when (modifiers & ModifierKeys.Shift) == 0 => viewModel.OpenCommand,
            Key.S when (modifiers & ModifierKeys.Shift) != 0 => viewModel.SaveAsCommand,
            _ => null,
        };
        if (command?.CanExecute(null) != true)
        {
            return;
        }

        FileMenuItem.IsSubmenuOpen = false;
        command.Execute(null);
        e.Handled = true;
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e) =>
        ToggleMaximizeRestore();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximizeRestore() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeRestoreButton is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeRestoreButton.SetResourceReference(
            ToolTipProperty,
            maximized ? "Window_Restore" : "Window_Maximize");
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || DataContext is not MainWindowViewModel viewModel || !viewModel.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        if (_closePromptActive)
        {
            return;
        }

        _closePromptActive = true;
        try
        {
            var dialog = new UnsavedChangesDialog { Owner = this };
            dialog.ShowDialog();

            if (dialog.Choice == UnsavedChangesChoice.Cancel)
            {
                return;
            }

            if (dialog.Choice == UnsavedChangesChoice.Save &&
                !await viewModel.SavePendingChangesAsync())
            {
                return;
            }

            _allowClose = true;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(Close));
        }
        finally
        {
            _closePromptActive = false;
        }
    }

    private void MenuItem_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || !ReferenceEquals(e.OriginalSource, menuItem))
        {
            return;
        }

        menuItem.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => RemovePopupShadow(menuItem));
    }

    private static void RemovePopupShadow(MenuItem menuItem)
    {
        if (menuItem.Template.FindName("PART_Popup", menuItem) is not Popup { Child: DependencyObject popupContent })
        {
            return;
        }

        var submenuBorder = FindDescendant<Border>(popupContent, "SubmenuBorder");
        if (submenuBorder is not null)
        {
            submenuBorder.Effect = null;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T matchingElement && matchingElement.Name == name)
        {
            return matchingElement;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var match = FindDescendant<T>(VisualTreeHelper.GetChild(root, index), name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
