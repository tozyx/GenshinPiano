using System.Windows;
using System.Windows.Controls;

namespace GenshinPiano.App.Controls;

/// <summary>Keeps Fluent's clear action from taking over short numeric inputs.</summary>
public static class TextBoxClearButtonBehavior
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached(
        "Compact", typeof(bool), typeof(TextBoxClearButtonBehavior), new PropertyMetadata(false, OnCompactChanged));

    public static bool GetCompact(DependencyObject element) => (bool)element.GetValue(CompactProperty);
    public static void SetCompact(DependencyObject element, bool value) => element.SetValue(CompactProperty, value);

    private static void OnCompactChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TextBox textBox) return;
        textBox.Loaded -= OnLoaded;
        textBox.SizeChanged -= OnSizeChanged;
        textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
        if ((bool)args.NewValue)
        {
            textBox.Loaded += OnLoaded;
            textBox.SizeChanged += OnSizeChanged;
            textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            if (textBox.IsLoaded) Apply(textBox);
        }
        else if (textBox.Template?.FindName("DeleteButton", textBox) is Button button)
        {
            foreach (var property in new[] { FrameworkElement.WidthProperty, FrameworkElement.HeightProperty,
                         FrameworkElement.MinWidthProperty, FrameworkElement.MinHeightProperty,
                         FrameworkElement.VerticalAlignmentProperty, FrameworkElement.MarginProperty,
                         Control.PaddingProperty, UIElement.VisibilityProperty }) button.ClearValue(property);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => Apply((TextBox)sender);
    private static void OnSizeChanged(object sender, SizeChangedEventArgs args) => Apply((TextBox)sender);
    private static void OnGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs args) => Apply((TextBox)sender);

    private static void Apply(TextBox textBox)
    {
        textBox.ApplyTemplate();
        if (textBox.Template?.FindName("DeleteButton", textBox) is not Button button) return;
        button.MinWidth = button.MinHeight = 0;
        button.Width = button.Height = 20;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0, 0, 4, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        if (textBox.ActualWidth < 100) button.Visibility = Visibility.Collapsed;
        else button.ClearValue(UIElement.VisibilityProperty); // Preserve Fluent's focus/text/read-only triggers.
    }
}
