using System.IO;
using GenshinPiano.App.Commands;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Conversion;
using GenshinPiano.Application.Playback;
using GenshinPiano.Application.Workspace;
using GenshinPiano.Core.Scores;
using Microsoft.Win32;

namespace GenshinPiano.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ScoreWorkspace _workspace;
    private readonly IThemeService _themeService;
    private readonly ILocalizationService _localizationService;
    private readonly ScorePlaybackService _playbackService;
    private readonly LegacyBatchConversionService _legacyConversionService;
    private string _scoreTitle;
    private string _statusKey = "Status_Ready";
    private object?[] _statusArguments = [];
    private CancellationTokenSource? _playbackCancellation;
    private bool _isPlaying;
    private bool _isPlaybackPaused;

    public MainWindowViewModel(
        ScoreWorkspace workspace,
        IThemeService themeService,
        ILocalizationService localizationService,
        ScorePlaybackService playbackService,
        LegacyBatchConversionService legacyConversionService)
    {
        _workspace = workspace;
        _themeService = themeService;
        _localizationService = localizationService;
        _playbackService = playbackService;
        _legacyConversionService = legacyConversionService;
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
        TogglePlaybackCommand = new RelayCommand(TogglePlayback);
        StopCommand = new RelayCommand(StopPlayback, () => IsPlaying);
        ConvertLegacyScoresCommand = new AsyncRelayCommand(ConvertLegacyScoresAsync);
    }

    public string WindowTitle => $"{(IsDirty ? "*" : string.Empty)}{ScoreTitle} - GenshinPiano v3";

    public bool IsDirty => _workspace.IsDirty;

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

    public ScoreDocument CurrentScore
    {
        get => _workspace.CurrentScore;
        set
        {
            if (ReferenceEquals(value, _workspace.CurrentScore))
            {
                return;
            }

            _workspace.ReplaceScore(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool IsDarkTheme => _themeService.CurrentTheme == AppTheme.Dark;

    public bool IsLightTheme => _themeService.CurrentTheme == AppTheme.Light;

    public bool IsChinese => _localizationService.CurrentLanguage == AppLanguage.SimplifiedChinese;

    public bool IsEnglish => _localizationService.CurrentLanguage == AppLanguage.English;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!SetProperty(ref _isPlaying, value))
            {
                return;
            }

            StopCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsPlaybackPaused
    {
        get => _isPlaybackPaused;
        private set => SetProperty(ref _isPlaybackPaused, value);
    }

    public RelayCommand NewCommand { get; }

    public AsyncRelayCommand OpenCommand { get; }

    public AsyncRelayCommand SaveAsCommand { get; }

    public RelayCommand UseDarkThemeCommand { get; }

    public RelayCommand UseLightThemeCommand { get; }

    public RelayCommand UseChineseCommand { get; }

    public RelayCommand UseEnglishCommand { get; }

    public RelayCommand TogglePlaybackCommand { get; }

    public RelayCommand StopCommand { get; }

    public AsyncRelayCommand ConvertLegacyScoresCommand { get; }

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
            AppLogger.Error($"Failed to open score '{dialog.FileName}'.", exception);
            SetStatus("Status_OpenFailed", exception.Message);
        }
    }

    private async Task SaveAsAsync()
    {
        await SaveToPathAsync(SelectSavePath());
    }

    public async Task<bool> SavePendingChangesAsync()
    {
        if (!_workspace.IsDirty)
        {
            return true;
        }

        var path = _workspace.CurrentPath ?? SelectSavePath();
        return path is not null && await SaveToPathAsync(path);
    }

    private string? SelectSavePath()
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
            return null;
        }

        return dialog.FileName;
    }

    private async Task<bool> SaveToPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            SetStatus("Status_Saving");
            await _workspace.SaveAsync(path);
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(WindowTitle));
            SetStatus("Status_Saved", path);
            return true;
        }
        catch (Exception exception)
        {
            AppLogger.Error($"Failed to save score '{path}'.", exception);
            SetStatus("Status_SaveFailed", exception.Message);
            return false;
        }
    }

    private async Task PlayAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        IsPlaying = true;

        var progress = new Progress<PlaybackProgress>(playbackProgress =>
        {
            switch (playbackProgress.Phase)
            {
                case PlaybackPhase.WaitingForTarget:
                    IsPlaybackPaused = true;
                    SetStatus("Status_PlayWaitingForTarget");
                    break;
                case PlaybackPhase.Countdown:
                    IsPlaybackPaused = false;
                    SetStatus("Status_PlayCountdown", playbackProgress.CountdownSeconds);
                    break;
                case PlaybackPhase.Paused:
                    IsPlaybackPaused = true;
                    SetStatus(
                        playbackProgress.PauseReason == PlaybackPauseReason.Manual
                            ? "Status_PlayPausedManual"
                            : "Status_PlayPaused");
                    break;
                case PlaybackPhase.Resumed:
                    IsPlaybackPaused = false;
                    SetStatus("Status_PlayResumed");
                    break;
                case PlaybackPhase.Playing:
                    IsPlaybackPaused = false;
                    var keys = playbackProgress.CurrentKeys is null
                        ? string.Empty
                        : string.Concat(playbackProgress.CurrentKeys);
                    SetStatus(
                        "Status_Playing",
                        playbackProgress.ChordIndex,
                        playbackProgress.ChordCount,
                        keys);
                    break;
                case PlaybackPhase.Completed:
                    SetStatus("Status_PlayCompleted", playbackProgress.SkippedNoteCount);
                    break;
            }
        });

        try
        {
            await _playbackService.PlayAsync(
                _workspace.CurrentScore,
                countdownSeconds: 3,
                progress,
                cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Status_PlayStopped");
        }
        catch (Exception exception)
        {
            AppLogger.Error("Game playback failed.", exception);
            SetStatus("Status_PlayFailed", exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                _playbackCancellation = null;
            }

            IsPlaying = false;
            IsPlaybackPaused = false;
        }
    }

    private void TogglePlayback()
    {
        if (!IsPlaying)
        {
            _ = PlayAsync();
            return;
        }

        if (_playbackService.IsManuallyPaused)
        {
            _playbackService.Resume();
            SetStatus("Status_PlayResuming");
        }
        else
        {
            _playbackService.Pause();
            IsPlaybackPaused = true;
            SetStatus("Status_PlayPausedManual");
        }
    }

    private void StopPlayback()
    {
        _playbackService.Resume();
        _playbackCancellation?.Cancel();
    }

    public void HandleGlobalEscape()
    {
        if (!IsPlaying || _playbackService.IsManuallyPaused)
        {
            return;
        }

        if (_playbackService.PauseIfTargetFocused())
        {
            IsPlaybackPaused = true;
            SetStatus("Status_PlayPausedByEscape");
        }
    }

    public void ReportGlobalEscapeUnavailable(string message) =>
        SetStatus("Status_GlobalEscapeUnavailable", message);

    private async Task ConvertLegacyScoresAsync()
    {
        var sourceDialog = new OpenFolderDialog
        {
            Title = _localizationService.GetString("Dialog_LegacySourceTitle"),
            Multiselect = false,
        };

        if (sourceDialog.ShowDialog() != true)
        {
            return;
        }

        var outputDialog = new OpenFolderDialog
        {
            Title = _localizationService.GetString("Dialog_LegacyOutputTitle"),
            Multiselect = false,
        };

        if (outputDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            SetStatus("Status_ConvertingLegacy");
            var result = await _legacyConversionService.ConvertDirectoryAsync(
                sourceDialog.FolderName,
                outputDialog.FolderName);
            SetStatus(
                "Status_ConversionCompleted",
                result.ConvertedCount,
                result.SkippedCount,
                result.FailedCount);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Legacy score batch conversion failed.", exception);
            SetStatus("Status_ConversionFailed", exception.Message);
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

    private void RefreshFromWorkspace()
    {
        ScoreTitle = _workspace.CurrentScore.Metadata.Title;
        OnPropertyChanged(nameof(CurrentScore));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(WindowTitle));
    }
}
