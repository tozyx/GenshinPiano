using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GenshinPiano.App.Dialogs;

public sealed class PracticeLatencyDialog : Window
{
    private readonly CalibrationRoll _roll;
    private readonly bool _zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
    public double DelayMilliseconds => _roll.DelayMilliseconds;

    public PracticeLatencyDialog(double delay)
    {
        Title = _zh ? "输入延迟与校准" : "Input offset and calibration";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
        SetResourceReference(FontFamilyProperty, "AppFontFamily");
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
        frame.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = frame;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Child = layout;
        var titleBar = new Border
        {
            Padding = new Thickness(14, 0, 6, 0),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
        };
        titleBar.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        titleBar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        layout.Children.Add(titleBar);
        var titleContent = new DockPanel();
        titleBar.Child = titleContent;
        var close = new Button
        {
            Width = 32, Height = 28, Padding = new Thickness(0),
            Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ToolTip = _zh ? "关闭" : "Close",
        };
        close.SetResourceReference(StyleProperty, "AppButtonStyle");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        titleContent.Children.Add(close);
        titleContent.Children.Add(new TextBlock
        {
            Text = Title, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14, FontWeight = FontWeights.SemiBold,
        });
        var panel = new StackPanel { Margin = new Thickness(22, 16, 22, 16) };
        Grid.SetRow(panel, 1);
        layout.Children.Add(panel);
        panel.Children.Add(new TextBlock
        {
            Text = _zh ? "跟随四拍声音观察音符下落。上下拖动目标线，使音符触线与听到的第四拍同步。"
                : "Drag the judgment line until the falling note crosses it when you hear beat four.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });
        _roll = new CalibrationRoll(delay) { Height = 280 };
        panel.Children.Add(_roll);
        var value = new TextBlock { Text = $"{delay:0} ms", FontSize = 16,
            FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 12) };
        value.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _roll.DelayChanged += (_, _) => value.Text = $"{_roll.DelayMilliseconds:+0;-0;0} ms";
        panel.Children.Add(value);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        void Add(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(12, 5, 12, 5) };
            button.SetResourceReference(StyleProperty, "AppButtonStyle");
            button.Click += (_, _) => action();
            buttons.Children.Add(button);
        }
        Add(_zh ? "重置" : "Reset", () => _roll.SetDelay(0));
        Add(_zh ? "保存" : "Save", () => DialogResult = true);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
