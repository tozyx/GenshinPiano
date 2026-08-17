using System.IO;
using GenshinPiano.App.Commands;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Workspace;
using Microsoft.Win32;

namespace GenshinPiano.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ScoreWorkspace _workspace;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private string _scoreTitle;
    private string _statusKey = "Status_Ready";
    private object?[] _statusArguments = [];

    public MainWindowViewModel(
        ScoreWorkspace workspace,
        IThemeService themeService,
        ILocalizationService localizationService)
    {
        _workspace = workspace;
        _themeService = themeService;
        _localizationService = localizationService;
        _scoreTitle = workspace.CurrentScore.Metadata.Title;

        _themeService.ThemeChanged += OnThemeChanged;
        _localizationService.LanguageChanged += OnLanguageChanged;

        NewCommand = new RelayCommand(CreateNew);
        OpenCommand = new AsyncRelayCommand(OpenAsync);
        SaveAsCommand = new AsyncRelayCommand(SaveAsAsync);
        UseDarkThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Dark));
        UseLightThemeCommand = new RelayCommand(() => ApplyTheme(AppTheme.Light));
        UseChineseCommand = new RelayCommand(() => ApplyLanguage(AppLanguage.SimplifiedChinese));
        UseEnglishCommand = new RelayCommand(() => ApplyLanguage(AppLanguage.English));
    }

    public string WindowTitle => $"{ScoreTitle} - GenshinPiano v3";

    public string ScoreTitle
    {
        get => _scoreTitle;
        private set
        {
            if (SetProperty(ref _scoreTitle, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public string StatusText => _localizationService.GetString(_statusKey, _statusArguments);

    public bool IsDarkTheme => _themeService.CurrentTheme == AppTheme.Dark;

    public bool IsLightTheme => _themeService.CurrentTheme == AppTheme.Light;

    public bool IsChinese => _localizationService.CurrentLanguage == AppLanguage.SimplifiedChinese;

    public bool IsEnglish => _localizationService.CurrentLanguage == AppLanguage.English;

    public RelayCommand NewCommand { get; }

    public AsyncRelayCommand OpenCommand { get; }

    public AsyncRelayCommand SaveAsCommand { get; }

    public RelayCommand UseDarkThemeCommand { get; }

    public RelayCommand UseLightThemeCommand { get; }

    public RelayCommand UseChineseCommand { get; }

    public RelayCommand UseEnglishCommand { get; }

    private void CreateNew()
    {
        _workspace.CreateNew(_localizationService.GetString("Score_Untitled"));
        RefreshFromWorkspace();
        SetStatus("Status_NewScoreCreated");
    }

    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localizationService.GetString("Dialog_OpenTitle"),
            Filter = _localizationService.GetString("Dialog_OpenFilter"),
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetStatus("Status_Opening");
            await _workspace.LoadAsync(dialog.FileName);
            RefreshFromWorkspace();
            SetStatus("Status_Opened", Path.GetFileName(dialog.FileName));
        }
        catch (Exception exception)
        {
            SetStatus("Status_OpenFailed", exception.Message);
        }
    }

    private async Task SaveAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = _localizationService.GetString("Dialog_SaveTitle"),
            Filter = _localizationService.GetString("Dialog_SaveFilter"),
            DefaultExt = ".gpiano",
            AddExtension = true,
            FileName = ScoreTitle,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetStatus("Status_Saving");
            await _workspace.SaveAsync(dialog.FileName);
            SetStatus("Status_Saved", dialog.FileName);
        }
        catch (Exception exception)
        {
            SetStatus("Status_SaveFailed", exception.Message);
        }
    }

    private void ApplyTheme(AppTheme theme)
    {
        if (_themeService.CurrentTheme == theme)
        {
            return;
        }

        _themeService.Apply(theme);
        var themeNameKey = theme == AppTheme.Dark ? "Menu_ThemeDark" : "Menu_ThemeLight";
        SetStatus("Status_ThemeChanged", _localizationService.GetString(themeNameKey));
    }

    private void ApplyLanguage(AppLanguage language)
    {
        if (_localizationService.CurrentLanguage == language)
        {
            return;
        }

        _localizationService.Apply(language);
        SetStatus("Status_LanguageChanged");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(IsEnglish));
    }

    private void SetStatus(string key, params object?[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        OnPropertyChanged(nameof(StatusText));
    }

    private void RefreshFromWorkspace() => ScoreTitle = _workspace.CurrentScore.Metadata.Title;
}
