namespace GenshinPiano.App.Services;

public sealed class ThemeService : IThemeService
{
    private const string ThemeSourceMarker = "Resources/Themes/Theme.";

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public event EventHandler? ThemeChanged;

    public void Apply(AppTheme theme)
    {
        if (theme == CurrentTheme)
        {
            return;
        }

        var fileName = theme switch
        {
            AppTheme.Dark => "Theme.Dark.xaml",
            AppTheme.Light => "Theme.Light.xaml",
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
        };

        ResourceDictionarySwitcher.Replace(ThemeSourceMarker, $"Resources/Themes/{fileName}");
        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
