using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GenshinPiano.App.Dialogs;

public partial class PlaybackMonitorWindow : Window
{
    private bool _restoreEditorOnClose = true;
    private bool _transitioningBack;

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
        Opacity = 0;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, AnimateEntrance);
    }

    private void AnimateEntrance()
    {
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        if (MonitorRoot.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(210))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(210))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ReturnButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_transitioningBack)
        {
            return;
        }

        _transitioningBack = true;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
        if (MonitorRoot.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(scale.ScaleX, 0.94, TimeSpan.FromMilliseconds(150)));
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(scale.ScaleY, 0.94, TimeSpan.FromMilliseconds(150)));
        }
    }

    private void PlaybackMonitorWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_restoreEditorOnClose)
        {
            ReturnToEditorRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
