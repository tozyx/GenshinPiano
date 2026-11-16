namespace GenshinPiano.App.Services;

public enum AppTheme
{
    Dark,
    Light,
}

public enum AppColorScheme
{
    Blue,
    Teal,
    Purple,
    Orange,
    Pink,
}

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    AppColorScheme CurrentColorScheme { get; }

    event EventHandler? ThemeChanging;

    event EventHandler? ThemeChanged;

    void Apply(AppTheme theme);

    void Apply(AppTheme theme, AppColorScheme colorScheme);

    void ApplyColorScheme(AppColorScheme colorScheme);
}
