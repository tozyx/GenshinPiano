namespace GenshinPiano.App.Services;

public enum AppLanguage
{
    SimplifiedChinese,
    English,
}

public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }

    event EventHandler? LanguageChanged;

    string GetString(string key, params object?[] arguments);

    void Apply(AppLanguage language);
}
