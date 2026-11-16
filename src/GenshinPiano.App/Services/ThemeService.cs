using System.Windows;

namespace GenshinPiano.App.Services;

public sealed class ThemeService : IThemeService
{
    private const string ThemeSourceMarker = "Resources/Themes/Theme.";
    private ResourceDictionary? _paletteResources;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public AppColorScheme CurrentColorScheme { get; private set; } = AppColorScheme.Blue;

    public event EventHandler? ThemeChanging;

    public event EventHandler? ThemeChanged;

    public void Apply(AppTheme theme) => Apply(theme, CurrentColorScheme);

    public void ApplyColorScheme(AppColorScheme colorScheme) => Apply(CurrentTheme, colorScheme);

    public void Apply(AppTheme theme, AppColorScheme colorScheme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (!Enum.IsDefined(colorScheme)) throw new ArgumentOutOfRangeException(nameof(colorScheme));
        if (_paletteResources is not null && theme == CurrentTheme && colorScheme == CurrentColorScheme)
        {
            return;
        }

        var fileName = theme switch
        {
            AppTheme.Dark => "Theme.Dark.xaml",
            AppTheme.Light => "Theme.Light.xaml",
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
        };

        var palette = ThemePalette.Create(theme, colorScheme);
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        ThemeChanging?.Invoke(this, EventArgs.Empty);
        if (theme != CurrentTheme)
        {
            ResourceDictionarySwitcher.Replace(ThemeSourceMarker, $"Resources/Themes/{fileName}");
        }
        if (_paletteResources is not null && dictionaries.IndexOf(_paletteResources) is var index && index >= 0)
        {
            dictionaries[index] = palette;
        }
        else
        {
            dictionaries.Add(palette);
        }
        _paletteResources = palette;
        CurrentTheme = theme;
        CurrentColorScheme = colorScheme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
