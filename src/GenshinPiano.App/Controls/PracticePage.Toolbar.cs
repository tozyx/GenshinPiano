using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using GenshinPiano.App.Services;

namespace GenshinPiano.App.Controls;

public partial class PracticePage
{
    private readonly HashSet<ComboBox> _expandingPracticeOptions = [];
    private bool _expandingPracticeVolume;
    private bool _isDraggingPracticeVolume;
    private CancellationTokenSource? _practiceVolumeCloseCts;

    private static ComboBox? FindPracticeOptionCombo(object sender)
    {
        if (sender is not Border { Child: StackPanel panel }) return null;
        foreach (UIElement child in panel.Children)
            if (child is Grid { Children.Count: > 0 } reveal && reveal.Children[0] is ComboBox combo)
                return combo;
        return null;
    }

    private Border? FindPracticeOptionHost(ComboBox combo)
    {
        if (ReferenceEquals(combo, PlaybackSpeedBox)) return PlaybackSpeedOption;
        if (ReferenceEquals(combo, NoteSpacingBox)) return NoteSpacingOption;
        if (ReferenceEquals(combo, PracticeInstrumentBox)) return InstrumentOption;
        return null;
    }

    private void PracticeOption_OnMouseEnter(object sender, MouseEventArgs e) =>
        AnimatePracticeOption(sender, true);

    private void PracticeOption_OnMouseLeave(object sender, MouseEventArgs e)
    {
        var combo = FindPracticeOptionCombo(sender);
        if (combo is null) return;
        if (combo.IsDropDownOpen || combo.IsMouseOver || _expandingPracticeOptions.Contains(combo)) return;
        AnimatePracticeOption(sender, false);
    }

    private void PracticeOptionCombo_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ComboBox combo && _expandingPracticeOptions.Contains(combo))
            CompletePracticeOptionExpansion(combo);
    }

    private void PracticeOption_OnDropDownClosed(object sender, EventArgs e)
    {
        if (sender is ComboBox combo && FindPracticeOptionHost(combo) is { } host &&
            !host.IsMouseOver && !combo.IsMouseOver)
            AnimatePracticeOption(host, false);
        Surface.Focus();
    }

    private double GetPracticeOptionWidth(ComboBox combo)
    {
        var english = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
        return ReferenceEquals(combo, PracticeInstrumentBox)
            ? 168
            : english ? 92 : 96;
    }

    private void CompletePracticeOptionExpansion(ComboBox combo)
    {
        if (combo.Parent is not Grid reveal) return;
        var width = GetPracticeOptionWidth(combo);
        combo.Width = width;
        reveal.BeginAnimation(WidthProperty, null);
        reveal.BeginAnimation(OpacityProperty, null);
        reveal.Width = width;
        reveal.Opacity = 1;
        reveal.IsHitTestVisible = true;
        _expandingPracticeOptions.Remove(combo);
        reveal.UpdateLayout();
    }

    private void AnimatePracticeOption(object sender, bool expanded)
    {
        var combo = FindPracticeOptionCombo(sender);
        if (combo?.Parent is not Grid reveal) return;

        var expandedWidth = GetPracticeOptionWidth(combo);
        combo.Width = expandedWidth;
        if (expanded)
        {
            reveal.IsHitTestVisible = true;
            _expandingPracticeOptions.Add(combo);
        }
        else reveal.IsHitTestVisible = false;

        var duration = TimeSpan.FromMilliseconds(170);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var widthAnimation = new DoubleAnimation(expanded ? expandedWidth : 0, duration)
            { EasingFunction = easing };
        var opacityAnimation = new DoubleAnimation(expanded ? 1 : 0, duration) { EasingFunction = easing };
        if (expanded)
        {
            widthAnimation.Completed += (_, _) =>
            {
                _expandingPracticeOptions.Remove(combo);
                reveal.IsHitTestVisible = true;
                if (sender is Border host && !host.IsMouseOver && !combo.IsMouseOver && !combo.IsDropDownOpen)
                    AnimatePracticeOption(host, false);
            };
        }

        reveal.BeginAnimation(WidthProperty, widthAnimation);
        reveal.BeginAnimation(OpacityProperty, opacityAnimation);
    }

    private void PracticeVolumeOption_OnMouseEnter(object sender, MouseEventArgs e)
    {
        CancelPracticeVolumeClose();
        if (!_expandingPracticeVolume && PracticeVolumeReveal.ActualWidth < 169)
            AnimatePracticeVolume(true);
    }

    private void PracticeVolumeOption_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_expandingPracticeVolume || _isDraggingPracticeVolume || PracticeVolumeSlider.IsMouseCaptureWithin)
            return;
        SchedulePracticeVolumeClose();
    }

    private void PracticeVolumeSlider_OnMouseEnter(object sender, MouseEventArgs e) =>
        CancelPracticeVolumeClose();

    private void PracticeVolumeSlider_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPracticeVolume && !PracticeVolumeSlider.IsMouseCaptureWithin)
            SchedulePracticeVolumeClose();
    }

    private void CancelPracticeVolumeClose()
    {
        _practiceVolumeCloseCts?.Cancel();
        _practiceVolumeCloseCts?.Dispose();
        _practiceVolumeCloseCts = null;
    }

    private async void SchedulePracticeVolumeClose()
    {
        CancelPracticeVolumeClose();
        var cancellation = new CancellationTokenSource();
        _practiceVolumeCloseCts = cancellation;
        try
        {
            await Task.Delay(240, cancellation.Token);
            if (!IsPointerNear(PracticeVolumeOption, 8) &&
                !IsPointerNear(PracticeVolumeSlider, 8) &&
                !_isDraggingPracticeVolume && !PracticeVolumeSlider.IsMouseCaptureWithin)
            {
                AnimatePracticeVolume(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_practiceVolumeCloseCts, cancellation))
            {
                _practiceVolumeCloseCts.Dispose();
                _practiceVolumeCloseCts = null;
            }
        }
    }

    private static bool IsPointerNear(FrameworkElement element, double tolerance)
    {
        var point = Mouse.GetPosition(element);
        return point.X >= -tolerance && point.Y >= -tolerance &&
               point.X <= element.ActualWidth + tolerance &&
               point.Y <= element.ActualHeight + tolerance;
    }

    private void AnimatePracticeVolume(bool expanded)
    {
        if (expanded)
        {
            PracticeVolumeReveal.IsHitTestVisible = true;
            _expandingPracticeVolume = true;
        }
        else PracticeVolumeReveal.IsHitTestVisible = false;

        var duration = TimeSpan.FromMilliseconds(170);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var widthAnimation = new DoubleAnimation(expanded ? 170 : 0, duration)
            { EasingFunction = easing };
        if (expanded)
        {
            widthAnimation.Completed += (_, _) =>
            {
                _expandingPracticeVolume = false;
                if (!IsPointerNear(PracticeVolumeOption, 8) && !IsPointerNear(PracticeVolumeSlider, 8))
                    SchedulePracticeVolumeClose();
            };
        }
        PracticeVolumeReveal.BeginAnimation(WidthProperty, widthAnimation);
        PracticeVolumeReveal.BeginAnimation(OpacityProperty,
            new DoubleAnimation(expanded ? 1 : 0, duration) { EasingFunction = easing });
    }

    private void CompletePracticeVolumeExpansion()
    {
        PracticeVolumeReveal.BeginAnimation(WidthProperty, null);
        PracticeVolumeReveal.BeginAnimation(OpacityProperty, null);
        PracticeVolumeReveal.Width = 170;
        PracticeVolumeReveal.Opacity = 1;
        PracticeVolumeReveal.IsHitTestVisible = true;
        _expandingPracticeVolume = false;
        PracticeVolumeReveal.UpdateLayout();
    }

    private void PracticeVolumeSlider_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPracticeVolumeClose();
        if (_expandingPracticeVolume) CompletePracticeVolumeExpansion();
        _isDraggingPracticeVolume = true;
        PracticeVolumeSlider.CaptureMouse();
        UpdatePracticeVolumeFromPointer(e);
        e.Handled = true;
    }

    private void PracticeVolumeSlider_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPracticeVolume) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndPracticeVolumeDrag();
            return;
        }
        UpdatePracticeVolumeFromPointer(e);
        e.Handled = true;
    }

    private void PracticeVolumeSlider_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingPracticeVolume) return;
        UpdatePracticeVolumeFromPointer(e);
        EndPracticeVolumeDrag();
        e.Handled = true;
    }

    private void UpdatePracticeVolumeFromPointer(MouseEventArgs e)
    {
        const double trackMargin = 6;
        var usableWidth = Math.Max(1, PracticeVolumeSlider.ActualWidth - trackMargin * 2);
        var x = e.GetPosition(PracticeVolumeSlider).X;
        PracticeVolumeSlider.Value = Math.Clamp((x - trackMargin) / usableWidth, 0, 1);
    }

    private void EndPracticeVolumeDrag()
    {
        _isDraggingPracticeVolume = false;
        if (PracticeVolumeSlider.IsMouseCaptured) PracticeVolumeSlider.ReleaseMouseCapture();
        if (!IsPointerNear(PracticeVolumeOption, 8) && !IsPointerNear(PracticeVolumeSlider, 8))
            SchedulePracticeVolumeClose();
        Surface.Focus();
    }

    private void PracticeVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyGlobalAuditionVolume(e.NewValue);
        UpdatePracticeVolumeValueTrack();
        if (!_restoringPracticeSettings && System.Windows.Application.Current is App app)
            app.UserSettingsService.SetAuditionVolume((int)Math.Round(e.NewValue * 100));
    }

    private void PracticeVolumeSlider_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdatePracticeVolumeValueTrack();

    private void UpdatePracticeVolumeValueTrack()
    {
        if (PracticeVolumeValueTrack is null || PracticeVolumeSlider is null) return;
        var availableWidth = Math.Max(0, PracticeVolumeSlider.ActualWidth - 12);
        PracticeVolumeValueTrack.Width = availableWidth * PracticeVolumeSlider.Value;
    }

    private static void ApplyGlobalAuditionVolume(double normalizedVolume)
    {
        if (System.Windows.Application.Current is App { AuditionService: { } service })
            service.SetVolume((int)Math.Round(Math.Clamp(normalizedVolume, 0, 1) * 127));
    }

    private void SyncGlobalAuditionVolume()
    {
        if (System.Windows.Application.Current is not App app) return;
        var normalized = app.UserSettingsService.Current.Editor.AuditionVolume / 100d;
        if (Math.Abs(PracticeVolumeSlider.Value - normalized) > 0.001)
            PracticeVolumeSlider.Value = normalized;
        ApplyGlobalAuditionVolume(normalized);
    }
}
