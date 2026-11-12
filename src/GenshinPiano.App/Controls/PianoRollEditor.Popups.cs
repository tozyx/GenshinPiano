using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GenshinPiano.Core.Scores;

namespace GenshinPiano.App.Controls;

public partial class PianoRollEditor
{
    private bool _isDraggingAuditionVolume;
    private bool _isClosingAuditionVolume;
    private bool _isClosingBpmEditor;
    private Window? _ownerWindow;

    private void AuditionVolumeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AuditionVolumePopup.IsOpen)
        {
            BeginCloseAuditionVolumePopup();
            return;
        }

        _isClosingAuditionVolume = false;
        AuditionVolumePopup.IsOpen = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            AnimateOpenAuditionVolumePopup);
    }

    private void OwnerWindow_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var dismissedTransientEditor = false;

        if (NoteEditorPopup.IsOpen && !IsPointerOverPopup(NoteEditorPopup))
        {
            NoteEditorPopup.IsOpen = false;
            dismissedTransientEditor = true;
        }

        if (EditorHelpPopup.IsOpen &&
            !EditorHelpButton.IsMouseOver &&
            !IsPointerOverPopup(EditorHelpPopup))
        {
            EditorHelpButton.IsChecked = false;
            dismissedTransientEditor = true;
        }

        if (BpmEditPanel.Visibility == Visibility.Visible &&
            !BpmControlHost.IsMouseOver)
        {
            Keyboard.ClearFocus();
            ApplyBpm();
            BeginCloseBpmEditor();
            dismissedTransientEditor = true;
        }

        if (AuditionVolumePopup.IsOpen &&
            !AuditionVolumeButton.IsMouseOver &&
            !IsPointerOverAuditionVolumePopup())
        {
            BeginCloseAuditionVolumePopup();
            dismissedTransientEditor = true;
        }

        // A click whose first purpose is dismissing a transient editor must not
        // fall through to the piano roll and create a note at the same position.
        if (dismissedTransientEditor && e.ChangedButton == MouseButton.Left && Surface.IsMouseOver)
        {
            e.Handled = true;
        }
    }

    private void OwnerWindow_OnDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            NoteEditorPopup.IsOpen = false;
            EditorHelpButton.IsChecked = false;

            if (BpmEditPanel.Visibility == Visibility.Visible)
            {
                Keyboard.ClearFocus();
                ApplyBpm();
                BeginCloseBpmEditor();
            }

            if (!IsPointerOverAuditionVolumePopup() &&
                !AuditionVolumeSlider.IsMouseCaptureWithin &&
                !_isDraggingAuditionVolume)
            {
                BeginCloseAuditionVolumePopup();
            }
        });
    }

    private void AnimateOpenAuditionVolumePopup()
    {
        if (AuditionVolumePopupContent.RenderTransform is not System.Windows.Media.ScaleTransform transform)
        {
            return;
        }

        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        transform.ScaleX = 0.08;
        transform.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0.08,
                1,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
    }

    private void BeginCloseAuditionVolumePopup()
    {
        if (!AuditionVolumePopup.IsOpen || _isClosingAuditionVolume)
        {
            return;
        }

        _isClosingAuditionVolume = true;
        if (AuditionVolumePopupContent.RenderTransform is not System.Windows.Media.ScaleTransform transform)
        {
            AuditionVolumePopup.IsOpen = false;
            _isClosingAuditionVolume = false;
            return;
        }

        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            transform.ScaleX,
            0.08,
            TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn,
            },
        };
        animation.Completed += (_, _) =>
        {
            AuditionVolumePopup.IsOpen = false;
            _isClosingAuditionVolume = false;
        };
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
    }

    private bool IsPointerOverAuditionVolumePopup()
        => IsPointerOverPopup(AuditionVolumePopup);

    private static bool IsPointerOverPopup(System.Windows.Controls.Primitives.Popup popup)
    {
        if (popup.Child is not FrameworkElement
            {
                IsVisible: true,
                ActualWidth: > 0,
                ActualHeight: > 0,
            } popupContent)
        {
            return false;
        }

        var pointer = Mouse.GetPosition(popupContent);
        return pointer.X >= 0 && pointer.X <= popupContent.ActualWidth &&
               pointer.Y >= 0 && pointer.Y <= popupContent.ActualHeight;
    }

    private void AuditionVolumeSlider_OnPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _isDraggingAuditionVolume = true;
        AuditionVolumeSlider.CaptureMouse();
        UpdateAuditionVolumeFromPointer(e);
        e.Handled = true;
    }

    private void AuditionVolumeSlider_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingAuditionVolume)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndAuditionVolumeDrag();
            return;
        }

        UpdateAuditionVolumeFromPointer(e);
        e.Handled = true;
    }

    private void AuditionVolumeSlider_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_isDraggingAuditionVolume)
        {
            return;
        }

        UpdateAuditionVolumeFromPointer(e);
        EndAuditionVolumeDrag();
        e.Handled = true;
    }

    private void UpdateAuditionVolumeFromPointer(MouseEventArgs e)
    {
        const double trackMargin = 8;
        var usableWidth = Math.Max(1, AuditionVolumeSlider.ActualWidth - trackMargin * 2);
        var x = e.GetPosition(AuditionVolumeSlider).X;
        var normalized = (x - trackMargin) / usableWidth;
        AuditionVolumeSlider.Value = Math.Clamp(normalized, 0, 1);
    }

    private void EndAuditionVolumeDrag()
    {
        _isDraggingAuditionVolume = false;
        _settingsService?.SetAuditionVolume((int)Math.Round(AuditionVolumeSlider.Value * 100));
        if (AuditionVolumeSlider.IsMouseCaptured)
        {
            AuditionVolumeSlider.ReleaseMouseCapture();
        }
    }

    private void AuditionVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetAuditionVolume(e.NewValue * 100);
        UpdateAuditionVolumeValueTrack();
    }

    private void AuditionVolumeSlider_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateAuditionVolumeValueTrack();

    private void UpdateAuditionVolumeValueTrack()
    {
        if (AuditionVolumeValueTrack is null || AuditionVolumeSlider is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, AuditionVolumeSlider.ActualWidth - 12);
        AuditionVolumeValueTrack.Width = availableWidth * AuditionVolumeSlider.Value;
    }

    private void SetAuditionVolume(double percentage)
    {
        _auditionService?.SetVolume((int)Math.Round(Math.Clamp(percentage, 0, 100) * 1.27));
    }

    private static FrameworkElement CreatePauseIcon()
    {
        var panel = new Grid
        {
            Width = 11,
            Height = 13,
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition());
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        panel.ColumnDefinitions.Add(new ColumnDefinition());

        var leftBar = new Border
        {
            Width = 3,
            Height = 13,
            CornerRadius = new CornerRadius(0.7),
        };
        var rightBar = new Border
        {
            Width = 3,
            Height = 13,
            CornerRadius = new CornerRadius(0.7),
        };
        leftBar.SetResourceReference(Border.BackgroundProperty, "PrimaryTextBrush");
        rightBar.SetResourceReference(Border.BackgroundProperty, "PrimaryTextBrush");
        Grid.SetColumn(leftBar, 0);
        Grid.SetColumn(rightBar, 2);
        panel.Children.Add(leftBar);
        panel.Children.Add(rightBar);
        return panel;
    }

    private void BpmDisplayButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isClosingBpmEditor = false;
        if (BpmEditLabel.RenderTransform is System.Windows.Media.ScaleTransform labelScale)
        {
            labelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            labelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
            labelScale.ScaleX = 1.1;
            labelScale.ScaleY = 1.1;
        }

        BpmDisplayButton.Visibility = Visibility.Collapsed;
        BpmEditPanel.Visibility = Visibility.Visible;
        BpmEditPanel.BeginAnimation(OpacityProperty, null);
        BpmEditPanel.Opacity = 1;
        BpmControlHost.BeginAnimation(WidthProperty, null);
        BpmControlHost.Width = 68;
        BpmControlHost.BeginAnimation(
            WidthProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                68,
                108,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
        if (BpmTextBox.RenderTransform is System.Windows.Media.ScaleTransform transform)
        {
            transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
            transform.ScaleX = 0.08;
            transform.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(
                    0.08,
                    1,
                    TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                    },
                });
        }

        BpmTextBox.Focus();
        BpmTextBox.SelectAll();
    }

    private void BpmTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        ApplyBpm();
        BeginCloseBpmEditor();
    }

    private void BeginCloseBpmEditor()
    {
        if (BpmEditPanel.Visibility != Visibility.Visible || _isClosingBpmEditor)
        {
            return;
        }

        _isClosingBpmEditor = true;
        if (BpmTextBox.RenderTransform is not System.Windows.Media.ScaleTransform transform)
        {
            BpmEditPanel.Visibility = Visibility.Collapsed;
            BpmDisplayButton.Visibility = Visibility.Visible;
            _isClosingBpmEditor = false;
            return;
        }

        var animation = new System.Windows.Media.Animation.DoubleAnimation(
            transform.ScaleX,
            0.08,
            TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn,
            },
        };
        BpmControlHost.BeginAnimation(
            WidthProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                BpmControlHost.ActualWidth,
                68,
                TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut,
                },
            });
        if (BpmEditLabel.RenderTransform is System.Windows.Media.ScaleTransform labelScale)
        {
            labelScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleXProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.1, 1, TimeSpan.FromMilliseconds(150)));
            labelScale.BeginAnimation(
                System.Windows.Media.ScaleTransform.ScaleYProperty,
                new System.Windows.Media.Animation.DoubleAnimation(1.1, 1, TimeSpan.FromMilliseconds(150)));
        }
        animation.Completed += (_, _) =>
        {
            BpmEditPanel.Visibility = Visibility.Collapsed;
            BpmDisplayButton.Visibility = Visibility.Visible;
            BpmControlHost.BeginAnimation(WidthProperty, null);
            BpmControlHost.Width = 68;
            _isClosingBpmEditor = false;
        };
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
    }

    private void BpmClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        BpmTextBox.Clear();
        BpmTextBox.Focus();
        e.Handled = true;
    }

    private void BpmTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyBpm();
            Surface.Focus();
            e.Handled = true;
        }
    }

    private void ApplyBpm()
    {
        if (Score is null || !double.TryParse(
                BpmTextBox.Text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture,
                out var bpm))
        {
            return;
        }

        bpm = Math.Clamp(bpm, 20, 300);
        BpmTextBox.Text = bpm.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        var tempoMap = Score.Timing.TempoMap
            .Where(change => change.Tick != 0)
            .Prepend(new TempoChange { Tick = 0, Bpm = bpm })
            .OrderBy(change => change.Tick)
            .ToList();
        SetCurrentValue(ScoreProperty, Score with
        {
            Timing = Score.Timing with { TempoMap = tempoMap },
        });
    }
}
