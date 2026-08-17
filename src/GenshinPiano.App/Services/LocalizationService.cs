using System.Globalization;
using System.Windows;

namespace GenshinPiano.App.Services;

public sealed class LocalizationService : ILocalizationService
{
    private const string LanguageSourceMarker = "Resources/Localization/Strings.";

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.SimplifiedChinese;

    public event EventHandler? LanguageChanged;

    public LocalizationService()
    {
        SetCulture("zh-CN");
    }

    public string GetString(string key, params object?[] arguments)
    {
        var template = System.Windows.Application.Current.TryFindResource(key) as string ?? $"[{key}]";
        return arguments.Length == 0
            ? template
            : string.Format(CultureInfo.CurrentCulture, template, arguments);
    }

    public void Apply(AppLanguage language)
    {
        if (language == CurrentLanguage)
        {
            return;
        }

        var (fileName, cultureName) = language switch
        {
            AppLanguage.SimplifiedChinese => ("Strings.zh-CN.xaml", "zh-CN"),
            AppLanguage.English => ("Strings.en-US.xaml", "en-US"),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null),
        };

        ResourceDictionarySwitcher.Replace(LanguageSourceMarker, $"Resources/Localization/{fileName}");

        SetCulture(cultureName);
        CurrentLanguage = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void SetCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
