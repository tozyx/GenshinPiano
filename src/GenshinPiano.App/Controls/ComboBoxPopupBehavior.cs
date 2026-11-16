using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GenshinPiano.App.Controls;

/// <summary>Adjusts Fluent popup shadows and motion while retaining its dropdown template.</summary>
public static class ComboBoxPopupBehavior
{
    public static readonly DependencyProperty RemoveShadowProperty = DependencyProperty.RegisterAttached(
        "RemoveShadow", typeof(bool), typeof(ComboBoxPopupBehavior), new PropertyMetadata(false, OnRemoveShadowChanged));

    public static readonly DependencyProperty UseFadeAnimationProperty = DependencyProperty.RegisterAttached(
        "UseFadeAnimation", typeof(bool), typeof(ComboBoxPopupBehavior), new PropertyMetadata(false, OnUseFadeAnimationChanged));

    private static readonly DependencyProperty FadePopupProperty = DependencyProperty.RegisterAttached(
        "FadePopup", typeof(Popup), typeof(ComboBoxPopupBehavior));

    private static readonly DependencyProperty FadeOwnerProperty = DependencyProperty.RegisterAttached(
        "FadeOwner", typeof(ComboBox), typeof(ComboBoxPopupBehavior));

    private static readonly DependencyProperty FadeOperationProperty = DependencyProperty.RegisterAttached(
        "FadeOperation", typeof(DispatcherOperation), typeof(ComboBoxPopupBehavior));

    private static readonly DependencyProperty OriginalShadowProperty = DependencyProperty.RegisterAttached(
        "OriginalShadow", typeof(DropShadowEffect), typeof(ComboBoxPopupBehavior));

    public static bool GetRemoveShadow(DependencyObject element) => (bool)element.GetValue(RemoveShadowProperty);

    public static void SetRemoveShadow(DependencyObject element, bool value) => element.SetValue(RemoveShadowProperty, value);

    public static bool GetUseFadeAnimation(DependencyObject element) => (bool)element.GetValue(UseFadeAnimationProperty);

    public static void SetUseFadeAnimation(DependencyObject element, bool value) => element.SetValue(UseFadeAnimationProperty, value);

    private static void OnUseFadeAnimationChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ComboBox combo) return;
        combo.Loaded -= OnFadeLoaded;
        combo.Unloaded -= OnFadeUnloaded;
        combo.DropDownOpened -= OnFadeDropDownOpened;
        if ((bool)args.NewValue)
        {
            combo.Loaded += OnFadeLoaded;
            combo.Unloaded += OnFadeUnloaded;
            combo.DropDownOpened += OnFadeDropDownOpened;
            if (combo.IsLoaded) AttachFadePopup(combo);
        }
        else DetachFadePopup(combo);
    }

    private static void OnFadeLoaded(object sender, RoutedEventArgs args) => AttachFadePopup((ComboBox)sender);

    private static void OnFadeUnloaded(object sender, RoutedEventArgs args) => DetachFadePopup((ComboBox)sender);

    private static void OnFadeDropDownOpened(object? sender, EventArgs args)
    {
        // The template can have changed since Loaded (editable mode or theme change).
        if (sender is ComboBox combo) AttachFadePopup(combo);
    }

    private static void AttachFadePopup(ComboBox combo)
    {
        combo.ApplyTemplate();
        var popup = combo.Template?.FindName("PART_Popup", combo) as Popup;
        if (ReferenceEquals(popup, combo.GetValue(FadePopupProperty))) return;
        DetachFadePopup(combo);
        if (popup is not { AllowsTransparency: true, Child: Border }) return;
        combo.SetValue(FadePopupProperty, popup);
        popup.SetValue(FadeOwnerProperty, combo);
        popup.Opened += OnFadePopupOpened;
        popup.Closed += OnFadePopupClosed;
    }

    private static void DetachFadePopup(ComboBox combo)
    {
        if (combo.GetValue(FadePopupProperty) is not Popup popup) return;
        popup.Opened -= OnFadePopupOpened;
        popup.Closed -= OnFadePopupClosed;
        ResetFade(popup);
        popup.ClearValue(FadeOwnerProperty);
        combo.ClearValue(FadePopupProperty);
    }

    private static void OnFadePopupOpened(object? sender, EventArgs args)
    {
        if (sender is not Popup { Child: Border border } popup ||
            popup.GetValue(FadeOwnerProperty) is not ComboBox combo || !GetUseFadeAnimation(combo)) return;

        ResetFade(popup);
        // Popup.Opened may precede the template's trigger actions. Hide the first
        // frame, then replace the fixed -90 -> 0 slide after those actions run.
        border.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0, TimeSpan.Zero));
        var operation = popup.Dispatcher.InvokeAsync(() =>
        {
            popup.ClearValue(FadeOperationProperty);
            if (!ReferenceEquals(popup.GetValue(FadeOwnerProperty), combo) || !GetUseFadeAnimation(combo) ||
                !ReferenceEquals(popup.Child, border)) return;
            if (border.RenderTransform is TranslateTransform transform)
            {
                if (transform.IsFrozen)
                {
                    transform = transform.CloneCurrentValue();
                    border.SetCurrentValue(UIElement.RenderTransformProperty, transform);
                }
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 0, TimeSpan.Zero));
            }
            border.BeginAnimation(UIElement.OpacityProperty, null);
            if (SystemParameters.ClientAreaAnimation && SystemParameters.ComboBoxAnimation)
                border.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, border.Opacity, TimeSpan.FromMilliseconds(120)) { FillBehavior = FillBehavior.Stop });
        }, DispatcherPriority.Normal);
        popup.SetValue(FadeOperationProperty, operation);
    }

    private static void OnFadePopupClosed(object? sender, EventArgs args)
    {
        if (sender is Popup popup) ResetFade(popup);
    }

    private static void ResetFade(Popup popup)
    {
        if (popup.GetValue(FadeOperationProperty) is DispatcherOperation operation)
        {
            operation.Abort();
            popup.ClearValue(FadeOperationProperty);
        }
        if (popup.Child is not Border border) return;
        border.BeginAnimation(UIElement.OpacityProperty, null);
        if (border.RenderTransform is TranslateTransform { IsFrozen: false } transform)
            transform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private static void OnRemoveShadowChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ComboBox combo) return;
        combo.Loaded -= OnLoaded;
        combo.DropDownOpened -= OnDropDownOpened;
        if ((bool)args.NewValue)
        {
            combo.Loaded += OnLoaded;
            combo.DropDownOpened += OnDropDownOpened;
            if (combo.IsLoaded) UpdateShadow(combo, remove: true);
        }
        else
        {
            UpdateShadow(combo, remove: false);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => UpdateShadow((ComboBox)sender, remove: true);

    private static void OnDropDownOpened(object? sender, EventArgs args)
    {
        if (sender is ComboBox combo) UpdateShadow(combo, remove: true);
    }

    private static void UpdateShadow(ComboBox combo, bool remove)
    {
        if (remove) combo.ApplyTemplate();
        if (combo.Template?.FindName("PART_Popup", combo) is not Popup { AllowsTransparency: true, Child: Border border }) return;

        if (remove && border.Effect is DropShadowEffect shadow)
        {
            // The popup HWND has no shadow margin, so Fluent's blurred pixels form a
            // rectangular edge outside the rounded border. Keep only the border itself.
            border.SetValue(OriginalShadowProperty, shadow);
            border.SetCurrentValue(UIElement.EffectProperty, null);
        }
        else if (!remove && border.GetValue(OriginalShadowProperty) is DropShadowEffect original)
        {
            border.SetCurrentValue(UIElement.EffectProperty, original);
            border.ClearValue(OriginalShadowProperty);
        }
    }
}
