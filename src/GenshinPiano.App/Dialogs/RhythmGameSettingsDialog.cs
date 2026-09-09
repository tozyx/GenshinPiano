using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GenshinPiano.App.Dialogs;

public sealed class RhythmGameSettingsDialog : Window
{
    private readonly Slider _volume;
    public int HitVolume => (int)Math.Round(_volume.Value);

    public RhythmGameSettingsDialog(int hitVolume)
    {
        var zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
        Title = zh ? "音游设置" : "Rhythm game settings";
        Width = 410;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "PrimaryTextBrush");
        SetResourceReference(FontFamilyProperty, "AppFontFamily");

        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
        frame.SetResourceReference(BorderBrushProperty, "BorderBrush");
        Content = frame;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Child = layout;
        var titleBar = new Border { Padding = new Thickness(14, 0, 6, 0), BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(10, 10, 0, 0) };
        titleBar.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        titleBar.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        layout.Children.Add(titleBar);
        var titleContent = new DockPanel();
        titleBar.Child = titleContent;
        var close = new Button { Width = 32, Height = 28, Padding = new Thickness(0), Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.SetResourceReference(StyleProperty, "AppButtonStyle");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        titleContent.Children.Add(close);
        titleContent.Children.Add(new TextBlock { Text = Title, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = FontWeights.SemiBold });

        var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        Grid.SetRow(panel, 1);
        layout.Children.Add(panel);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(header);
        var value = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        value.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        DockPanel.SetDock(value, Dock.Right);
        header.Children.Add(value);
        header.Children.Add(new TextBlock { Text = zh ? "音游模式反馈音大小" : "Rhythm hit-sound volume", VerticalAlignment = VerticalAlignment.Center });
        _volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(hitVolume, 0, 100), TickFrequency = 1, IsSnapToTickEnabled = true };
        value.Text = $"{_volume.Value:0}";
        _volume.ValueChanged += (_, e) => { value.Text = $"{e.NewValue:0}"; Services.PracticeHitSound.SetVolume((int)Math.Round(e.NewValue)); };
        panel.Children.Add(_volume);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(buttons);
        var cancel = new Button { Content = zh ? "取消" : "Cancel" };
        cancel.SetResourceReference(StyleProperty, "AppButtonStyle");
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);
        var save = new Button { Content = zh ? "保存" : "Save" };
        save.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        save.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(save);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
