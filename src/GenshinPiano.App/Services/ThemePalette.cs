using System.Windows;
using System.Windows.Media;

namespace GenshinPiano.App.Services;

internal static class ThemePalette
{
    // Fluent captures the Windows accent in per-control brushes. Override those semantic
    // resources as well as the app brushes so existing controls change immediately.
    private static readonly string[] AccentKeys =
    [
        "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
        "AccentFillColorSelectedTextBackgroundBrush", "AccentFillColorDefaultBrush", "SystemFillColorAttentionBrush",
        "AccentButtonBackground", "CalendarViewSelectedBackground", "CalendarViewSelectedBorderBrush",
        "CalendarViewTodayBackground", "CheckBoxCheckBackgroundFillIndeterminate", "CheckBoxCheckBackgroundFillChecked",
        "CheckBoxCheckBackgroundStrokeIndeterminate", "CheckBoxCheckBackgroundStrokeChecked",
        "ComboBoxBorderBrushFocused", "ComboBoxItemPillFillBrush", "DataGridRowSelectedBackgroundThemeBrush",
        "DatePickerBorderBrushFocused", "DatePickerFocusedBorderBrush", "HyperlinkForeground", "HyperlinkForegroundPointerOver",
        "HyperlinkButtonForeground", "InfoBarInformationalSeverityIconBackground", "ListViewItemPillFillBrush",
        "NavigationViewSelectionIndicatorForeground", "ProgressBarForeground", "ProgressRingForegroundThemeBrush",
        "RadioButtonOuterEllipseCheckedStroke", "RadioButtonOuterEllipseCheckedFill", "RatingControlSelectedForeground",
        "SliderThumbBackground", "TextControlSelectionHighlightColor", "TextControlFocusedBorderBrush", "ThumbRateForeground",
        "ToggleButtonBackgroundChecked", "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchFillOn",
        "TreeViewItemSelectionIndicatorForeground",
    ];

    private static readonly string[] HoverKeys =
    [
        "AccentFillColorSecondaryBrush", "AccentButtonBackgroundPointerOver",
        "CheckBoxCheckBackgroundFillIndeterminatePointerOver", "CheckBoxCheckBackgroundFillCheckedPointerOver",
        "CheckBoxCheckBackgroundStrokeIndeterminatePointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
        "HyperlinkButtonForegroundPointerOver", "RadioButtonOuterEllipseCheckedStrokePointerOver",
        "RadioButtonCheckOuterEllipseCheckedFillPointerOver", "SliderThumbBackgroundPointerOver",
        "ToggleButtonBackgroundCheckedPointerOver", "ToggleSwitchFillOnPointerOver",
    ];

    private static readonly string[] PressedKeys =
    [
        "AccentFillColorTertiaryBrush", "AccentButtonBackgroundPressed",
        "CheckBoxCheckBackgroundFillIndeterminatePressed", "CheckBoxCheckBackgroundFillCheckedPressed",
        "CheckBoxCheckBackgroundStrokeIndeterminatePressed", "CheckBoxCheckBackgroundStrokeCheckedPressed",
        "HyperlinkButtonForegroundPressed", "RadioButtonCheckOuterEllipseCheckedStrokePressed",
        "RadioButtonCheckOuterEllipseCheckedFillPressed", "ToggleButtonBackgroundCheckedPressed",
    ];

    public static ResourceDictionary Create(AppTheme theme, AppColorScheme scheme)
    {
        var colors = (theme, scheme) switch
        {
            (AppTheme.Light, AppColorScheme.Blue) => new Palette("#2563A9", "#DCEBFA", "#F4F6F8", "#E9EDF2", "#FFFFFF", "#FFFFFF", "#C7CFD9"),
            (AppTheme.Dark, AppColorScheme.Blue) => new Palette("#7EB6FF", "#253D59", "#20252D", "#181C23", "#1C2129", "#282E38", "#414B59"),
            (AppTheme.Light, AppColorScheme.Teal) => new Palette("#16796F", "#D9EFEB", "#F2F7F6", "#E5EFEC", "#FFFFFF", "#FBFEFD", "#BFCFCB"),
            (AppTheme.Dark, AppColorScheme.Teal) => new Palette("#6CD3C0", "#23463F", "#1E2927", "#17211F", "#1B2422", "#26332F", "#40564E"),
            (AppTheme.Light, AppColorScheme.Purple) => new Palette("#7654AA", "#EDE4F7", "#F7F4FA", "#EDE7F3", "#FFFFFF", "#FEFCFF", "#CFC4DC"),
            (AppTheme.Dark, AppColorScheme.Purple) => new Palette("#C3A2F0", "#403052", "#27222E", "#1E1925", "#231E2A", "#312A3A", "#51445F"),
            (AppTheme.Light, AppColorScheme.Orange) => new Palette("#A55320", "#F8E6D7", "#FAF6F1", "#F2E9DF", "#FFFFFF", "#FFFCF8", "#D9C9B9"),
            (AppTheme.Dark, AppColorScheme.Orange) => new Palette("#F0B47F", "#513A29", "#2B2520", "#211B17", "#261F1B", "#362C25", "#5C4B3D"),
            (AppTheme.Light, AppColorScheme.Pink) => new Palette("#AD4776", "#F7E0EB", "#FBF4F7", "#F3E6ED", "#FFFFFF", "#FFFBFD", "#DCC2CF"),
            (AppTheme.Dark, AppColorScheme.Pink) => new Palette("#F0A0C4", "#512E40", "#2D2229", "#23191F", "#281D24", "#382932", "#604452"),
            _ => throw new ArgumentOutOfRangeException(nameof(scheme)),
        };
        var resources = new ResourceDictionary();
        var accent = Brush(colors.Accent);
        resources["AccentBrush"] = accent;
        resources["AccentSurfaceBrush"] = Brush(colors.AccentSurface);
        resources["AppBackgroundBrush"] = Brush(colors.Background);
        resources["SidebarBackgroundBrush"] = Brush(colors.Sidebar);
        resources["PanelBackgroundBrush"] = Brush(colors.Panel);
        resources["SurfaceBrush"] = Brush(colors.Surface);
        resources["BorderBrush"] = Brush(colors.Border);
        if (theme == AppTheme.Light)
        {
            resources["EditorAlternateRowBrush"] = Brush(scheme switch
            {
                AppColorScheme.Blue => "#E8EDF3",
                AppColorScheme.Teal => "#E6EEEB",
                AppColorScheme.Purple => "#ECE8F1",
                AppColorScheme.Orange => "#F1EBE4",
                AppColorScheme.Pink => "#F3E8EE",
                _ => throw new ArgumentOutOfRangeException(nameof(scheme)),
            });
        }
        foreach (var key in AccentKeys) resources[key] = accent;
        // Fluent text inputs use a separate gradient for the focused bottom edge.
        // Keep its two-DIP geometry and neutral outline, replacing the Windows accent.
        var focusedBorder = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 2),
            RelativeTransform = new ScaleTransform(1, -1, 0, 0.5),
        };
        focusedBorder.GradientStops.Add(new GradientStop(accent.Color, 1));
        focusedBorder.GradientStops.Add(new GradientStop(
            theme == AppTheme.Light ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x12, 255, 255, 255), 1));
        focusedBorder.Freeze();
        resources["TextControlBorderBrushFocused"] = focusedBorder;
        resources["TextControlElevationBorderFocusedBrush"] = focusedBorder;
        var hover = Brush(colors.Accent, 0.9);
        foreach (var key in HoverKeys) resources[key] = hover;
        var pressed = Brush(colors.Accent, 0.8);
        foreach (var key in PressedKeys) resources[key] = pressed;
        resources["ListBoxItemSelectedBackgroundThemeBrush"] = Brush(colors.Accent, 0.4);
        resources["ListBoxItemSelectedBackgroundPointerOverThemeBrush"] = Brush(colors.Accent, 0.6);
        resources["ListBoxItemSelectedBackgroundPressedThemeBrush"] = Brush(colors.Accent, 0.7);
        resources["ListBoxItemBackgroundSelectedPressedThemeBrush"] = Brush(colors.Accent, 0.7);
        resources["ComboBoxDropDownBackground"] = resources["SurfaceBrush"];
        return resources;
    }

    private static SolidColorBrush Brush(string color, double opacity = 1)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private sealed record Palette(string Accent, string AccentSurface, string Background, string Sidebar,
        string Panel, string Surface, string Border);
}
