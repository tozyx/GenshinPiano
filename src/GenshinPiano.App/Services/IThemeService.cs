namespace GenshinPiano.App.Services;

public enum AppTheme
{
    Dark,
    Light,
}

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    event EventHandler? ThemeChanged;

    void Apply(AppTheme theme);
}
