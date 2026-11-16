using System.IO;
using GenshinPiano.App.Services;
using Xunit;

namespace GenshinPiano.Core.Tests;

public sealed class AppearanceSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GenshinPiano-appearance-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Theory]
    [InlineData(AppColorScheme.Blue)]
    [InlineData(AppColorScheme.Teal)]
    [InlineData(AppColorScheme.Purple)]
    [InlineData(AppColorScheme.Orange)]
    [InlineData(AppColorScheme.Pink)]
    public void ColorScheme_PersistsIndependentlyOfLightAndDarkMode(AppColorScheme scheme)
    {
        var settings = new UserSettingsService(SettingsPath);
        settings.SetTheme(AppTheme.Light);
        settings.SetLanguage(AppLanguage.English);
        settings.SetColorScheme(scheme);
        var restored = new UserSettingsService(SettingsPath);
        Assert.Equal(scheme.ToString(), restored.Current.Appearance.ColorScheme);
        Assert.Equal(nameof(AppTheme.Light), restored.Current.Appearance.Theme);
        Assert.Equal(nameof(AppLanguage.English), restored.Current.Appearance.Language);
        restored.SetTheme(AppTheme.Dark);
        Assert.Equal(scheme.ToString(), new UserSettingsService(SettingsPath).Current.Appearance.ColorScheme);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"colorScheme\":null}")]
    [InlineData("{\"colorScheme\":\"Unknown\"}")]
    [InlineData("{\"colorScheme\":\"999\"}")]
    public void OldOrInvalidColorScheme_FallsBackToBlueWithoutResettingTheme(string appearance)
    {
        Directory.CreateDirectory(_directory);
        var fields = appearance[1..^1];
        File.WriteAllText(SettingsPath, "{\"appearance\":{\"theme\":\"Light\",\"language\":\"English\"" +
            (fields.Length == 0 ? "" : "," + fields) + "}}");
        var settings = new UserSettingsService(SettingsPath);
        Assert.Equal(nameof(AppColorScheme.Blue), settings.Current.Appearance.ColorScheme);
        Assert.Equal(nameof(AppTheme.Light), settings.Current.Appearance.Theme);
        Assert.Equal(nameof(AppLanguage.English), settings.Current.Appearance.Language);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
