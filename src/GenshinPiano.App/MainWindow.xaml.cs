using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Interop;
using GenshinPiano.App.Dialogs;
using GenshinPiano.App.Controls;
using GenshinPiano.App.ViewModels;
using GenshinPiano.App.Services;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App;

public partial class MainWindow : Window
{
    private const string FeedbackUrl = "https://github.com/tozyx/GenshinPiano/issues";
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
    private OcrImportDialog? _ocrImportDialog;
    private bool _workspaceFileSelectionBusy;
    private bool _suppressWorkspaceFileOpen;
    private bool _scoreSearchClosing;
    private readonly DispatcherTimer _scoreSearchCloseTimer;
    private HwndSource? _hwndSource;

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
        InstallSingleInstanceWindowMessageHook();
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

    private void InstallSingleInstanceWindowMessageHook()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        AllowSingleInstanceActivationMessage(handle);
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(MainWindowWindowProc);
    }

    private IntPtr MainWindowWindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if ((uint)message != SingleInstanceCoordinator.ActivationMessage)
        {
            return IntPtr.Zero;
        }

        handled = true;
        Dispatcher.BeginInvoke(RestoreAndActivateFromSecondInstance, DispatcherPriority.Normal);
        return IntPtr.Zero;
    }

    private static void AllowSingleInstanceActivationMessage(IntPtr handle)
    {
        var message = SingleInstanceCoordinator.ActivationMessage;
        if (message == 0)
        {
            return;
        }

        try
        {
            var filter = new ChangeFilterStruct { Size = (uint)Marshal.SizeOf<ChangeFilterStruct>() };
            ChangeWindowMessageFilterEx(handle, message, MessageFilterAllow, ref filter);
        }
        catch (Exception exception) when (
            exception is EntryPointNotFoundException or DllNotFoundException)
        {
        }
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
        OpenScoreFolderSearchPopup();
    }

    private void ScoreFolderSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenScoreFolderSearchPopup();
    }

    private void OpenScoreFolderSearchPopup()
    {
        _scoreSearchCloseTimer.Stop();
        var wasOpen = IsScoreFolderSearchOpen && !_scoreSearchClosing;
        _scoreSearchClosing = false;
        ScoreFolderSearchTextBox.Visibility = Visibility.Visible;
        if (wasOpen)
        {
            return;
        }

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
        if (!IsPointerOverScoreFolderSearch())
        {
            BeginCloseScoreFolderSearch();
        }
    }

    private void BeginCloseScoreFolderSearch()
    {
        if (!IsScoreFolderSearchOpen || _scoreSearchClosing)
        {
            return;
        }

        _scoreSearchClosing = true;
        if (ScoreFolderSearchTextBox.RenderTransform is not ScaleTransform transform)
        {
            ScoreFolderSearchTextBox.Visibility = Visibility.Collapsed;
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
                ScoreFolderSearchTextBox.Visibility = Visibility.Collapsed;
                ScoreFolderSearchTextBox.Opacity = 0.82;
                transform.ScaleX = 0.05;
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

    private bool IsScoreFolderSearchOpen =>
        ScoreFolderSearchTextBox.Visibility == Visibility.Visible;

    private bool IsPointerOverScoreFolderSearch() =>
        ScoreFolderSearchButton.IsMouseOver ||
        ScoreFolderSearchTextBox.IsMouseOver;

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(
        IntPtr windowHandle,
        uint message,
        uint action,
        ref ChangeFilterStruct filter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr windowHandle, bool altTab);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    private const int ShowWindowRestore = 9;
    private const uint FlashAll = 0x00000003;
    private const uint FlashTimerNoForeground = 0x0000000C;
    private const uint MessageFilterAllow = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChangeFilterStruct
    {
        public uint Size;
        public uint ExtStatus;
    }

    public async Task HandleSingleInstanceRequestAsync(SingleInstanceRequest request)
    {
        RestoreAndActivateFromSecondInstance();

        if (request.IsElevated && !IsCurrentProcessElevated())
        {
            MessageBox.Show(
                this,
                (string)FindResource("SingleInstance_ElevationConflict"),
                (string)FindResource("SingleInstance_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) ||
            !MainWindowViewModel.IsSupportedScorePath(request.FilePath) ||
            DataContext is not MainWindowViewModel viewModel ||
            viewModel.IsPlaying ||
            _isLocalAuditionPlaying)
        {
            return;
        }

        if (viewModel.CurrentSourcePath is not null && string.Equals(
                Path.GetFullPath(viewModel.CurrentSourcePath),
                Path.GetFullPath(request.FilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (viewModel.IsDirty)
        {
            var dialog = new UnsavedChangesDialog { Owner = this };
            dialog.ShowDialog();
            if (dialog.Choice == UnsavedChangesChoice.Cancel) return;
            if (dialog.Choice == UnsavedChangesChoice.Save &&
                !await viewModel.SavePendingChangesAsync()) return;
            if (dialog.Choice == UnsavedChangesChoice.DontSave)
                viewModel.DiscardRecovery();
        }

        await viewModel.OpenPathAsync(request.FilePath);
    }

    private void RestoreAndActivateFromSecondInstance()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        ShowWindow(handle, ShowWindowRestore);
        var activated = Activate();
        Focus();
        var foregroundSet = SetForegroundWindow(handle);
        if (!foregroundSet)
        {
            var wasTopmost = Topmost;
            Topmost = true;
            Topmost = wasTopmost;
            activated = Activate();
            foregroundSet = SetForegroundWindow(handle);
        }

        if (!foregroundSet && !activated)
        {
            SwitchToThisWindow(handle, true);
        }

        if (!activated && !IsActive)
        {
            FlashTaskbar(handle);
        }
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => { if (!IsActive) FlashTaskbar(handle); });
        AppLogger.Info($"Restored main window for a second-instance request; foreground={IsActive}.");
    }

    private static void FlashTaskbar(IntPtr handle)
    {
        var flash = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = handle,
            Flags = FlashAll | FlashTimerNoForeground,
            Count = 3,
        };
        FlashWindowEx(ref flash);
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) => Close();

    private void OptimizeDurationsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var optimizedCount = PianoRollEditor.OptimizeAllNoteDurations();
        if (optimizedCount > 0 && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotifyDurationsOptimized(optimizedCount);
        }
    }

    private void SelectAllNotesMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        PianoRollEditor.SelectAllNotes();

    private void UndoMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        PianoRollEditor.UndoEdit();

    private void RedoMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        PianoRollEditor.RedoEdit();

    private void SelectBeforeCursorMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        PianoRollEditor.SelectNotesBeforeCursor();

    private void SelectAfterCursorMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        PianoRollEditor.SelectNotesAfterCursor();

    private void ScoreAnalysisMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var report = ScoreQualityAnalyzer.Analyze(viewModel.CurrentScore);
        var dialog = new ScoreAnalysisDialog(report) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.KeySteps is int keySteps)
        {
            var changedNotes = PianoRollEditor.ShiftAllNotesInGenshinRange(keySteps);
            if (changedNotes > 0)
            {
                viewModel.NotifyScoreRangeShifted(keySteps, changedNotes);
            }

            return;
        }

        var cleanupResult = PianoRollEditor.ApplyScoreCleanup(dialog.CleanupOptions);
        if (cleanupResult is not null)
        {
            viewModel.NotifyScoreCleaned(cleanupResult);
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

    private void EditMenuItem_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        MenuItem_OnSubmenuOpened(sender, e);
        if (sender is MenuItem menuItem && ReferenceEquals(e.OriginalSource, menuItem))
        {
            UpdatePitchLabelMenuChecks();
        }
    }

    private void SettingsMenuItem_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        MenuItem_OnSubmenuOpened(sender, e);
        if (sender is MenuItem menuItem && ReferenceEquals(e.OriginalSource, menuItem))
        {
            if (System.Windows.Application.Current is App app)
            {
                OcrCompletionNotificationMenuItem.IsChecked =
                    app.UserSettingsService.Current.Notifications.NotifyWhenOcrCompletes;
                UpdatePianoRollFrameRateMenuChecks(
                    app.UserSettingsService.Current.Editor.PianoRollFrameRate);
            }
            UpdateNetworkDependentUpdateMenuItems(animate: false);
            UpdateFileAssociationMenuState();
        }
    }

    private void UpdatesMenuItem_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        MenuItem_OnSubmenuOpened(sender, e);
        if (sender is MenuItem menuItem && ReferenceEquals(e.OriginalSource, menuItem))
        {
            UpdateNetworkDependentUpdateMenuItems(animate: false);
        }
    }

    private void AnimatedCheckableMenuItem_OnCheckStateChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || !ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        menuItem.BeginAnimation(OpacityProperty, null);
        menuItem.Opacity = 0.88;
        menuItem.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.88, 1, TimeSpan.FromMilliseconds(120)));
    }

    private void UpdateNetworkDependentUpdateMenuItems(bool animate)
    {
        var networkEnabled = DataContext is MainWindowViewModel
        {
            UpdateStatus.NetworkAccessEnabled: true,
        };
        var hasRollback = UpdateInstallerService.HasRollback;
        RollbackMenuItem.IsEnabled = hasRollback;

        SetUpdateMenuElementVisibility(UpdateNetworkSeparator, networkEnabled, animate);
        SetUpdateMenuElementVisibility(AutomaticUpdatesMenuItem, networkEnabled, animate);
        SetUpdateMenuElementVisibility(PreviewUpdatesMenuItem, networkEnabled, animate);
        SetUpdateMenuElementVisibility(CheckForUpdatesMenuItem, networkEnabled, animate);
        SetUpdateMenuElementVisibility(RollbackMenuItem, networkEnabled && hasRollback, animate);
    }

    private void SetUpdateMenuElementVisibility(FrameworkElement element, bool shouldShow, bool animate)
    {
        element.BeginAnimation(OpacityProperty, null);
        var transform = EnsureMenuTransitionTransform(element);
        transform.BeginAnimation(TranslateTransform.XProperty, null);

        if (!animate)
        {
            element.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            element.Opacity = 1;
            transform.X = 0;
            return;
        }

        if (shouldShow)
        {
            if (element.Visibility == Visibility.Visible && element.Opacity >= 0.98)
            {
                transform.X = 0;
                return;
            }

            element.Visibility = Visibility.Visible;
            element.Opacity = 0;
            transform.X = -8;
            element.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(165)));
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = CreateMenuTransitionEase(),
                });
            return;
        }

        if (element.Visibility != Visibility.Visible)
        {
            element.Visibility = Visibility.Collapsed;
            element.Opacity = 1;
            transform.X = 0;
            return;
        }

        var fadeOut = new DoubleAnimation(
            Math.Clamp(element.Opacity, 0, 1),
            0,
            TimeSpan.FromMilliseconds(130));
        fadeOut.Completed += (_, _) =>
        {
            if (!ShouldShowUpdateMenuElement(element))
            {
                element.Visibility = Visibility.Collapsed;
                element.Opacity = 1;
                transform.X = 0;
            }

        };
        element.BeginAnimation(OpacityProperty, fadeOut);
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(transform.X, -8, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = CreateMenuTransitionEase(),
            });
    }

    private bool ShouldShowUpdateMenuElement(FrameworkElement element)
    {
        if (DataContext is not MainWindowViewModel
            {
                UpdateStatus.NetworkAccessEnabled: true,
            })
        {
            return false;
        }

        return !ReferenceEquals(element, RollbackMenuItem) ||
               UpdateInstallerService.HasRollback;
    }

    private static TranslateTransform EnsureMenuTransitionTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        transform = new TranslateTransform();
        element.RenderTransform = transform;
        return transform;
    }

    private static IEasingFunction CreateMenuTransitionEase() => new CubicEase
    {
        EasingMode = EasingMode.EaseOut,
    };

    private void UpdateFileAssociationMenuState()
    {
        try
        {
            var state = FileAssociationService.GetState();
            RegisterGpianoAssociationMenuItem.IsEnabled = !state.OpensWithCurrentExecutable;
            UnregisterGpianoAssociationMenuItem.IsEnabled = state.CanUnregister;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Could not read .gpiano file association state: {exception.Message}");
            RegisterGpianoAssociationMenuItem.IsEnabled = true;
            UnregisterGpianoAssociationMenuItem.IsEnabled = false;
        }
    }

    private void RegisterGpianoAssociationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.RegisterGpianoAssociation();
            UpdateFileAssociationMenuState();
            NotifyStatus("Status_FileAssociationRegistered");
            AppLogger.Info(".gpiano file association registered for the current executable.");
        }
        catch (Exception exception)
        {
            NotifyStatus("Status_FileAssociationRegisterFailed", exception.Message);
            AppLogger.Error("Failed to register .gpiano file association.", exception);
        }
    }

    private void UnregisterGpianoAssociationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.UnregisterGpianoAssociation();
            UpdateFileAssociationMenuState();
            NotifyStatus("Status_FileAssociationUnregistered");
            AppLogger.Info(".gpiano file association unregistered.");
        }
        catch (Exception exception)
        {
            NotifyStatus("Status_FileAssociationUnregisterFailed", exception.Message);
            AppLogger.Error("Failed to unregister .gpiano file association.", exception);
        }
    }

    private void NotifyStatus(string key, params object?[] arguments)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.NotifyStatus(key, arguments);
        }
    }

    private async void RollbackMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!UpdateInstallerService.HasRollback) return;
        var dialog = new UpdateReadyDialog(
            "Rollback_Title", "Rollback_Message", "Rollback_Restart") { Owner = this };
        dialog.ShowDialog();
        if (!dialog.RestartRequested) return;
        try
        {
            _pendingUpdatePlanPath = await new UpdateInstallerService().PrepareRollbackAsync();
            Close();
        }
        catch (Exception exception)
        {
            _pendingUpdatePlanPath = null;
            AppLogger.Warning($"Could not prepare application rollback: {exception}");
        }
    }

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var displayVersion = (DataContext as MainWindowViewModel)?
            .UpdateStatus.CurrentVersionText ?? "unknown";
        new AboutDialog(displayVersion)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void ViewReleaseNotesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var cached = ReleaseNotesCacheService.Load();
        var currentState = (DataContext as MainWindowViewModel)?.UpdateStatus.CurrentState;
        var version = cached?.Version ??
                      currentState?.AvailableVersion?.ToString() ??
                      string.Empty;
        var notes = cached?.ReleaseNotes ?? currentState?.ReleaseNotes;
        new ReleaseNotesDialog("Update_ReleaseNotesTitle", version, notes)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void GitHubFeedbackMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ExternalLinkService.TryOpen(FeedbackUrl, out var exception))
        {
            NotifyStatus("Status_GitHubOpened");
            return;
        }

        AppLogger.Warning($"Could not open GitHub issues page: {exception?.Message}");
        NotifyStatus("Status_OpenLinkFailed", exception?.Message ?? string.Empty);
    }

    private void PitchLabelMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } &&
            Enum.TryParse<PitchLabelMode>(tag, out var mode))
        {
            PianoRollEditor.SetPitchLabelMode(mode);
            UpdatePitchLabelMenuChecks();
        }
    }

    private void UpdatePitchLabelMenuChecks()
    {
        var mode = PianoRollEditor.PitchLabelMode;
        PitchLettersWithKeyMenuItem.IsChecked = mode == PitchLabelMode.LetterWithKey;
        PitchNumbersWithKeyMenuItem.IsChecked = mode == PitchLabelMode.NumberedWithKey;
        PitchLettersOnlyMenuItem.IsChecked = mode == PitchLabelMode.LetterOnly;
        PitchNumbersOnlyMenuItem.IsChecked = mode == PitchLabelMode.NumberedOnly;
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

    private void OcrCompletionNotificationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && System.Windows.Application.Current is App app)
        {
            app.UserSettingsService.SetNotifyWhenOcrCompletes(menuItem.IsChecked);
        }
    }

    private void PianoRollFrameRateMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string mode } ||
            System.Windows.Application.Current is not App app)
        {
            return;
        }

        app.UserSettingsService.SetPianoRollFrameRate(mode);
        PianoRollEditor.SetFrameRateMode(mode);
        UpdatePianoRollFrameRateMenuChecks(mode);
    }

    private void UpdatePianoRollFrameRateMenuChecks(string mode)
    {
        PianoRollFrameRate30MenuItem.IsChecked = mode == "30";
        PianoRollFrameRate60MenuItem.IsChecked = mode == "60";
        PianoRollFrameRateVSyncMenuItem.IsChecked = mode == "VSync";
    }

    private async void ImportOcrImageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is not App app ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (_ocrImportDialog is { IsVisible: true } existingDialog)
        {
            if (existingDialog.WindowState == WindowState.Minimized)
            {
                existingDialog.WindowState = WindowState.Normal;
            }

            existingDialog.Activate();
            return;
        }

        var dialog = new OcrImportDialog(
            app.OcrAddonService,
            app.OcrAddonPackageManager,
            app.UserSettingsService,
            app.NotificationService,
            app.TaskbarProgressService);
        _ocrImportDialog = dialog;
        bool accepted;
        try
        {
            accepted = await dialog.ShowAsync(this);
        }
        finally
        {
            if (ReferenceEquals(_ocrImportDialog, dialog))
            {
                _ocrImportDialog = null;
            }
        }

        if (!accepted || dialog.Result?.Score is not { } score ||
            dialog.ImagePath is not { } imagePath)
        {
            return;
        }

        if (viewModel.IsDirty)
        {
            var unsavedDialog = new UnsavedChangesDialog { Owner = this };
            unsavedDialog.ShowDialog();
            if (unsavedDialog.Choice == UnsavedChangesChoice.Cancel)
            {
                return;
            }

            if (unsavedDialog.Choice == UnsavedChangesChoice.Save &&
                !await viewModel.SavePendingChangesAsync())
            {
                return;
            }

            if (unsavedDialog.Choice == UnsavedChangesChoice.DontSave)
            {
                viewModel.DiscardRecovery();
            }
        }

        viewModel.ImportOcrScore(score, imagePath);
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
            if (IsScoreFolderSearchOpen &&
                !IsPointerOverScoreFolderSearch())
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
            _subscribedViewModel.UpdateStatus.PropertyChanged -= UpdateStatus_OnPropertyChanged;
            _subscribedViewModel.UpdateStatus.ReadyUpdateRequested -=
                UpdateStatus_OnReadyUpdateRequested;
        }

        _subscribedViewModel = e.NewValue as MainWindowViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            _subscribedViewModel.UpdateStatus.PropertyChanged += UpdateStatus_OnPropertyChanged;
            _subscribedViewModel.UpdateStatus.ReadyUpdateRequested +=
                UpdateStatus_OnReadyUpdateRequested;
            UpdateNetworkDependentUpdateMenuItems(animate: false);
        }
    }

    private void UpdateStatus_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UpdateStatusViewModel.NetworkAccessEnabled))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => UpdateNetworkDependentUpdateMenuItems(animate: UpdatesMenuItem.IsSubmenuOpen));
    }

    private async void UpdateStatus_OnReadyUpdateRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await ShowReadyUpdateDialogAsync(viewModel);
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
        _hwndSource?.RemoveHook(MainWindowWindowProc);
        _hwndSource = null;

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _subscribedViewModel.UpdateStatus.PropertyChanged -= UpdateStatus_OnPropertyChanged;
            _subscribedViewModel.UpdateStatus.ReadyUpdateRequested -=
                UpdateStatus_OnReadyUpdateRequested;
            _subscribedViewModel.UpdateStatus.Dispose();
            _subscribedViewModel = null;
        }

        _playbackMonitorWindow?.CloseWithoutRestoringEditor();
        _playbackMonitorWindow = null;

        if (!string.IsNullOrWhiteSpace(_pendingUpdatePlanPath))
        {
            UpdateInstallerService.Launch(_pendingUpdatePlanPath);
        }
    }

    private string? _pendingUpdatePlanPath;

    private async void UpdateStatus_OnClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        e.Handled = true;
        if (viewModel.UpdateStatus.Stage == GenshinPiano.Application.Updates.UpdateStage.Available)
        {
            var downloadDialog = new UpdateReadyDialog(
                "Update_DownloadTitle", "Update_DownloadMessage", "Update_DownloadAction")
            { Owner = this };
            downloadDialog.ShowDialog();
            if (downloadDialog.RestartRequested)
            {
                await viewModel.UpdateStatus.DownloadAvailableUpdateAsync();
            }
            return;
        }
        if (!viewModel.UpdateStatus.CanInstall) return;
        await ShowReadyUpdateDialogAsync(viewModel);
    }

    private async Task ShowReadyUpdateDialogAsync(MainWindowViewModel viewModel)
    {
        if (!viewModel.UpdateStatus.CanInstall) return;
        var state = viewModel.UpdateStatus.CurrentState;
        var dialog = new Dialogs.UpdateReadyDialog(state.AvailableVersion?.ToString() ?? string.Empty) { Owner = this };
        dialog.ShowDialog();
        if (!dialog.RestartRequested) return;
        try
        {
            _pendingUpdatePlanPath = await new UpdateInstallerService().PrepareAsync(state);
            Close();
        }
        catch (Exception exception)
        {
            _pendingUpdatePlanPath = null;
            AppLogger.Warning($"Could not prepare update installation: {exception}");
        }
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
        if (_allowClose || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (!viewModel.IsDirty)
        {
            if (!viewModel.UpdateStatus.CanInstall || _closePromptActive) return;
            e.Cancel = true;
            _closePromptActive = true;
            try
            {
                await PrepareReadyUpdateIfNeededAsync(viewModel);
                _allowClose = true;
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Close));
            }
            finally
            {
                _closePromptActive = false;
            }
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
                _pendingUpdatePlanPath = null;
                return;
            }

            if (dialog.Choice == UnsavedChangesChoice.Save &&
                !await viewModel.SavePendingChangesAsync())
            {
                _pendingUpdatePlanPath = null;
                return;
            }

            if (dialog.Choice == UnsavedChangesChoice.DontSave)
            {
                viewModel.DiscardRecovery();
            }

            await PrepareReadyUpdateIfNeededAsync(viewModel);
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

    private async Task PrepareReadyUpdateIfNeededAsync(MainWindowViewModel viewModel)
    {
        if (_pendingUpdatePlanPath is not null || !viewModel.UpdateStatus.CanInstall) return;
        try
        {
            _pendingUpdatePlanPath = await new UpdateInstallerService()
                .PrepareAsync(viewModel.UpdateStatus.CurrentState, restartAfterInstall: false);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Could not prepare background update on exit: {exception}");
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
