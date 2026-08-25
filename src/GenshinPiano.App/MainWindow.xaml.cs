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
using GenshinPiano.App.Controls;
using GenshinPiano.App.ViewModels;

namespace GenshinPiano.App;

public partial class MainWindow : Window
{
    private static readonly DependencyProperty AnimatedVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedVerticalOffset",
            typeof(double),
            typeof(MainWindow),
            new PropertyMetadata(0d, OnAnimatedVerticalOffsetChanged));
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
    private bool _isLocalAuditionPlaying;
    private MainWindowViewModel? _subscribedViewModel;
    private PlaybackMonitorWindow? _playbackMonitorWindow;
    private bool _workspaceFileSelectionBusy;
    private bool _suppressWorkspaceFileOpen;
    private bool _scoreSearchClosing;
    private readonly DispatcherTimer _scoreSearchCloseTimer;

    public MainWindow()
    {
        InitializeComponent();
        _scoreSearchCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _scoreSearchCloseTimer.Tick += ScoreSearchCloseTimer_OnTick;
        InputManager.Current.PreProcessInput += InputManager_OnPreProcessInput;
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

    private void MainWindow_OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetSupportedDroppedFile(e.Data, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void MainWindow_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainWindowViewModel viewModel &&
            TryGetSupportedDroppedFile(e.Data, out var path))
        {
            await viewModel.OpenPathAsync(path);
        }
    }

    private static bool TryGetSupportedDroppedFile(IDataObject data, out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return false;
        }

        path = paths.FirstOrDefault(MainWindowViewModel.IsSupportedScorePath) ?? string.Empty;
        return path.Length > 0;
    }

    private async void WorkspaceFileList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_workspaceFileSelectionBusy ||
            _suppressWorkspaceFileOpen ||
            e.AddedItems.OfType<ScoreFolderFile>().FirstOrDefault() is not { } file ||
            DataContext is not MainWindowViewModel viewModel ||
            string.Equals(file.Path, viewModel.CurrentSourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _workspaceFileSelectionBusy = true;
        try
        {
            // A ListBox changes its selection on mouse-down. Opening immediately can
            // change the wrapped current-title height while the same click is still
            // in progress, moving the library underneath the pointer. Keep the item
            // captured above and defer the layout-changing open until mouse-up.
            await WaitForLeftMouseButtonReleaseAsync();

            if (viewModel.IsDirty)
            {
                var dialog = new UnsavedChangesDialog { Owner = this };
                dialog.ShowDialog();
                if (dialog.Choice == UnsavedChangesChoice.Cancel)
                {
                    WorkspaceFileList.SelectedValue = viewModel.CurrentSourcePath;
                    return;
                }

                if (dialog.Choice == UnsavedChangesChoice.Save &&
                    !await viewModel.SavePendingChangesAsync())
                {
                    WorkspaceFileList.SelectedValue = viewModel.CurrentSourcePath;
                    return;
                }

                if (dialog.Choice == UnsavedChangesChoice.DontSave)
                {
                    viewModel.DiscardRecovery();
                }
            }

            await viewModel.OpenPathAsync(file.Path);
            WorkspaceFileList.SelectedValue = viewModel.CurrentSourcePath;
        }
        finally
        {
            _workspaceFileSelectionBusy = false;
        }
    }

    private static Task WaitForLeftMouseButtonReleaseAsync()
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        PreProcessInputEventHandler? inputHandler = null;
        inputHandler = (_, args) =>
        {
            if (args.StagingItem.Input is not MouseButtonEventArgs
                {
                    ChangedButton: MouseButton.Left,
                    ButtonState: MouseButtonState.Released,
                })
            {
                return;
            }

            InputManager.Current.PreProcessInput -= inputHandler;
            completion.TrySetResult();
        };
        InputManager.Current.PreProcessInput += inputHandler;
        return completion.Task;
    }

    private void WorkspaceFileList_OnPreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { } item)
        {
            return;
        }

        _suppressWorkspaceFileOpen = true;
        try
        {
            item.IsSelected = true;
            item.Focus();
        }
        finally
        {
            _suppressWorkspaceFileOpen = false;
        }
    }

    private void WorkspaceFileList_OnContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(Mouse.DirectlyOver as DependencyObject) is null)
        {
            e.Handled = true;
        }
    }

    private async void RenameScoreMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        await RenameSelectedScoreAsync();

    private async void WorkspaceFileList_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2)
        {
            return;
        }

        e.Handled = true;
        await RenameSelectedScoreAsync();
    }

    private async Task RenameSelectedScoreAsync()
    {
        if (WorkspaceFileList.SelectedItem is not ScoreFolderFile file ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new RenameScoreDialog(file.DisplayName)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await viewModel.RenameScoreFileAsync(file, dialog.NewTitle);
    }

    private void CurrentScoreTitle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var currentFile = viewModel.ScoreFolderFiles.FirstOrDefault(file =>
            string.Equals(file.Path, viewModel.CurrentSourcePath, StringComparison.OrdinalIgnoreCase));
        if (currentFile is not null)
        {
            AnimateWorkspaceFileIntoView(currentFile);
        }

        e.Handled = true;
    }

    private void AnimateWorkspaceFileIntoView(ScoreFolderFile currentFile)
    {
        WorkspaceFileList.UpdateLayout();
        var scrollViewer = FindDescendant<ScrollViewer>(WorkspaceFileList);
        if (scrollViewer is null)
        {
            WorkspaceFileList.ScrollIntoView(currentFile);
            WorkspaceFileList.SelectedItem = currentFile;
            WorkspaceFileList.Focus();
            return;
        }

        var index = WorkspaceFileList.Items.IndexOf(currentFile);
        if (index < 0)
        {
            return;
        }

        var itemHeight = 38d;
        if (WorkspaceFileList.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement firstItem &&
            firstItem.ActualHeight > 0)
        {
            itemHeight = firstItem.ActualHeight;
        }

        var target = Math.Clamp(
            index * itemHeight - (scrollViewer.ViewportHeight - itemHeight) / 2,
            0,
            scrollViewer.ScrollableHeight);
        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
        scrollViewer.SetValue(AnimatedVerticalOffsetProperty, scrollViewer.VerticalOffset);
        var animation = new System.Windows.Media.Animation.DoubleAnimation(
                scrollViewer.VerticalOffset,
                target,
                TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };
        animation.Completed += (_, _) =>
        {
            WorkspaceFileList.SelectedItem = currentFile;
            WorkspaceFileList.Focus();
        };
        scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, animation);
    }

    private static void OnAnimatedVerticalOffsetChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ScrollViewer scrollViewer && e.NewValue is double offset)
        {
            scrollViewer.ScrollToVerticalOffset(offset);
        }
    }

    private void ScoreFolderSearchButton_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _scoreSearchCloseTimer.Stop();
        _scoreSearchClosing = false;
        ScoreFolderSearchPopup.IsOpen = true;
    }

    private void ScoreFolderSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        _scoreSearchCloseTimer.Stop();
        ScoreFolderSearchPopup.IsOpen = true;
    }

    private void RefreshScoreFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (RefreshScoreFolderIcon.RenderTransform is not RotateTransform transform)
        {
            return;
        }

        transform.BeginAnimation(RotateTransform.AngleProperty, null);
        transform.Angle = 0;
        transform.BeginAnimation(
            RotateTransform.AngleProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0,
                360,
                TimeSpan.FromMilliseconds(380))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
    }

    private void ScoreFolderSearchArea_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _scoreSearchCloseTimer.Stop();
        _scoreSearchClosing = false;
    }

    private void ScoreFolderSearchArea_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _scoreSearchCloseTimer.Stop();
        _scoreSearchCloseTimer.Start();
    }

    private void ScoreSearchCloseTimer_OnTick(object? sender, EventArgs e)
    {
        _scoreSearchCloseTimer.Stop();
        if (!ScoreFolderSearchButton.IsMouseOver &&
            !ScoreFolderSearchPopupContent.IsMouseOver)
        {
            BeginCloseScoreFolderSearch();
        }
    }

    private void BeginCloseScoreFolderSearch()
    {
        if (!ScoreFolderSearchPopup.IsOpen || _scoreSearchClosing)
        {
            return;
        }

        _scoreSearchClosing = true;
        if (ScoreFolderSearchTextBox.RenderTransform is not ScaleTransform transform)
        {
            ScoreFolderSearchPopup.IsOpen = false;
            _scoreSearchClosing = false;
            return;
        }

        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            transform.ScaleX,
            0.05,
            TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn,
            },
        };
        animation.Completed += (_, _) =>
        {
            if (_scoreSearchClosing)
            {
                ScoreFolderSearchPopup.IsOpen = false;
                _scoreSearchClosing = false;
            }
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        ScoreFolderSearchTextBox.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                ScoreFolderSearchTextBox.Opacity,
                0.82,
                TimeSpan.FromMilliseconds(125)));
    }

    private void ScoreFolderSearchPopup_OnOpened(object? sender, EventArgs e)
    {
        _scoreSearchClosing = false;
        ScoreFolderSearchTextBox.BeginAnimation(OpacityProperty, null);
        ScoreFolderSearchTextBox.Opacity = 0.82;
        ScoreFolderSearchTextBox.BeginAnimation(
            OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.82,
                1,
                TimeSpan.FromMilliseconds(160)));

        if (ScoreFolderSearchTextBox.RenderTransform is ScaleTransform transform)
        {
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.ScaleX = 0.05;
            transform.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0.05,
                    1,
                    TimeSpan.FromMilliseconds(190))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                    },
                });
        }
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

    private void OptimizeDurationsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var optimizedCount = PianoRollEditor.OptimizeAllNoteDurations();
        if (optimizedCount > 0 && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotifyDurationsOptimized(optimizedCount);
        }
    }

    private void GenerateShortPressDurationsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var optimizedCount = PianoRollEditor.GenerateShortPressDurations();
        if (optimizedCount > 0 && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotifyShortPressDurationsGenerated(optimizedCount);
        }
    }

    private void ImportMidiBatchMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is not App app)
        {
            return;
        }

        new MidiBatchConversionDialog(app.MidiBatchConversionService)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        TryHandleFileShortcut(e);
    }

    private void InputManager_OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (!IsActive)
        {
            return;
        }

        if (e.StagingItem.Input is MouseButtonEventArgs
            {
                RoutedEvent: var mouseEvent,
                ChangedButton: MouseButton.Left,
            } &&
            mouseEvent == Mouse.PreviewMouseDownEvent)
        {
            if (ScoreFolderSearchPopup.IsOpen &&
                !ScoreFolderSearchButton.IsMouseOver &&
                !ScoreFolderSearchPopupContent.IsMouseOver)
            {
                BeginCloseScoreFolderSearch();
            }

            return;
        }

        if (e.StagingItem.Input is not KeyEventArgs { RoutedEvent: var routedEvent } keyEventArgs ||
            routedEvent != Keyboard.PreviewKeyDownEvent)
        {
            return;
        }

        TryHandleFileShortcut(keyEventArgs);
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        BeginCloseScoreFolderSearch();
    }

    private void TryHandleFileShortcut(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (_isLocalAuditionPlaying || e.IsRepeat || DataContext is not MainWindowViewModel viewModel)
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

    private void PianoRollEditor_OnAuditionStateChanged(
        object? sender,
        AuditionStateChangedEventArgs e)
    {
        _isLocalAuditionPlaying = e.IsPlaying;
        FileMenuItem.IsEnabled = !e.IsPlaying;
        EditMenuItem.IsEnabled = !e.IsPlaying;
        ImportMenuItem.IsEnabled = !e.IsPlaying;
        NewScoreButton.IsEnabled = !e.IsPlaying;
        OpenScoreFolderButton.IsEnabled = !e.IsPlaying;
        WorkspaceLibraryPanel.IsEnabled = !e.IsPlaying;
        GamePlaybackControls.IsEnabled = !e.IsPlaying;
    }

    private void MainWindow_OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        _subscribedViewModel = e.NewValue as MainWindowViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsPlaying) ||
            sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        PianoRollEditor.SetGamePlaybackActive(viewModel.IsPlaying);
        if (viewModel.IsPlaying)
        {
            ShowPlaybackMonitor(viewModel);
        }
    }

    private void ShowPlaybackMonitor(MainWindowViewModel viewModel)
    {
        if (_playbackMonitorWindow is not null)
        {
            return;
        }

        var monitor = new PlaybackMonitorWindow
        {
            DataContext = viewModel,
        };
        monitor.ReturnToEditorRequested += PlaybackMonitor_OnReturnToEditorRequested;
        monitor.Closed += PlaybackMonitor_OnClosed;
        _playbackMonitorWindow = monitor;
        monitor.Show();
        WindowState = WindowState.Minimized;
    }

    private void PlaybackMonitor_OnReturnToEditorRequested(object? sender, EventArgs e)
    {
        WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    private void PlaybackMonitor_OnClosed(object? sender, EventArgs e)
    {
        if (sender is PlaybackMonitorWindow monitor)
        {
            monitor.ReturnToEditorRequested -= PlaybackMonitor_OnReturnToEditorRequested;
            monitor.Closed -= PlaybackMonitor_OnClosed;
        }

        _playbackMonitorWindow = null;
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        InputManager.Current.PreProcessInput -= InputManager_OnPreProcessInput;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _subscribedViewModel = null;
        }

        _playbackMonitorWindow?.CloseWithoutRestoringEditor();
        _playbackMonitorWindow = null;
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

            if (dialog.Choice == UnsavedChangesChoice.DontSave)
            {
                viewModel.DiscardRecovery();
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

    private static T? FindDescendant<T>(DependencyObject root, string? name = null)
        where T : FrameworkElement
    {
        if (root is T matchingElement &&
            (string.IsNullOrEmpty(name) || matchingElement.Name == name))
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

    private static T? FindAncestor<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
