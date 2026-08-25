using System.IO;
using System.Collections.ObjectModel;
using GenshinPiano.App.Commands;
using GenshinPiano.App.Dialogs;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Abstractions;
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
    private readonly IUserSettingsService _userSettingsService;
    private readonly ScorePlaybackService _playbackService;
    private readonly LegacyBatchConversionService _legacyConversionService;
    private readonly IMidiScoreImporter _midiScoreImporter;
    private readonly ScoreRecoveryService _recoveryService;
    private CancellationTokenSource? _autosaveCancellation;
    private string _scoreTitle;
    private string _statusKey = "Status_Ready";
    private object?[] _statusArguments = [];
    private CancellationTokenSource? _playbackCancellation;
    private bool _isPlaying;
    private bool _isPlaybackPaused;
    private int _playbackChordIndex;
    private int _playbackChordCount;
    private string _playbackCurrentKeys = "—";
    private string? _lastBrowseDirectory;
    private string _scoreFolder = string.Empty;
    private string _scoreFolderSearch = string.Empty;
    private string? _currentSourcePath;
    private IReadOnlyList<ScoreFolderFile> _allScoreFolderFiles = [];

    public MainWindowViewModel(
        ScoreWorkspace workspace,
        IThemeService themeService,
        ILocalizationService localizationService,
        IUserSettingsService userSettingsService,
        ScorePlaybackService playbackService,
        LegacyBatchConversionService legacyConversionService,
        IMidiScoreImporter midiScoreImporter,
        ScoreRecoveryService recoveryService)
    {
        _workspace = workspace;
        _themeService = themeService;
        _localizationService = localizationService;
        _userSettingsService = userSettingsService;
        _playbackService = playbackService;
        _legacyConversionService = legacyConversionService;
        _midiScoreImporter = midiScoreImporter;
        _recoveryService = recoveryService;
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
        OpenScoreFolderCommand = new AsyncRelayCommand(OpenScoreFolderAsync);
        RefreshScoreFolderCommand = new AsyncRelayCommand(RefreshScoreFolderAsync, () => ScoreFolder.Length > 0);
        _scoreFolder = _userSettingsService.Current.Library.ScoreFolder;
        if (_scoreFolder.Length > 0)
        {
            _lastBrowseDirectory = _scoreFolder;
            _ = RefreshScoreFolderAsync();
        }
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
            ScheduleAutosave();
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

    public int PlaybackChordIndex
    {
        get => _playbackChordIndex;
        private set
        {
            if (SetProperty(ref _playbackChordIndex, value))
            {
                OnPropertyChanged(nameof(PlaybackProgressPercent));
            }
        }
    }

    public int PlaybackChordCount
    {
        get => _playbackChordCount;
        private set
        {
            if (SetProperty(ref _playbackChordCount, value))
            {
                OnPropertyChanged(nameof(PlaybackProgressPercent));
            }
        }
    }

    public double PlaybackProgressPercent => PlaybackChordCount <= 0
        ? 0
        : Math.Clamp(PlaybackChordIndex * 100d / PlaybackChordCount, 0, 100);

    public string PlaybackCurrentKeys
    {
        get => _playbackCurrentKeys;
        private set => SetProperty(ref _playbackCurrentKeys, value);
    }

    public ObservableCollection<ScoreFolderFile> ScoreFolderFiles { get; } = [];

    public string ScoreFolder => _scoreFolder;

    public string ScoreFolderName => ScoreFolder.Length == 0
        ? _localizationService.GetString("Sidebar_NoFolder")
        : Path.GetFileName(Path.TrimEndingDirectorySeparator(ScoreFolder));

    public string ScoreFolderSearch
    {
        get => _scoreFolderSearch;
        set
        {
            if (SetProperty(ref _scoreFolderSearch, value))
            {
                ApplyScoreFolderFilter();
            }
        }
    }

    public string? CurrentSourcePath
    {
        get => _currentSourcePath;
        private set => SetProperty(ref _currentSourcePath, value);
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

    public AsyncRelayCommand OpenScoreFolderCommand { get; }

    public AsyncRelayCommand RefreshScoreFolderCommand { get; }

    public void NotifyDurationsOptimized(int noteCount) =>
        SetStatus("Status_DurationsOptimized", noteCount);

    public void NotifyShortPressDurationsGenerated(int noteCount) =>
        SetStatus("Status_ShortPressDurationsGenerated", noteCount);

    private void CreateNew()
    {
        CancelAutosave();
        _recoveryService.Discard();
        _workspace.CreateNew(_localizationService.GetString("Score_Untitled"));
        CurrentSourcePath = null;
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
            InitialDirectory = _lastBrowseDirectory,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await OpenPathAsync(dialog.FileName);
    }

    public static bool IsSupportedScorePath(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".gpiano", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".midi", StringComparison.OrdinalIgnoreCase);
    }

    public async Task OpenPathAsync(string path)
    {
        if (!IsSupportedScorePath(path))
        {
            return;
        }

        path = Path.GetFullPath(path);
        _lastBrowseDirectory = Path.GetDirectoryName(path);
        try
        {
            SetStatus("Status_Opening");
            var extension = Path.GetExtension(path);
            if (extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Status_ImportingMidi");
                var fileInfo = await _midiScoreImporter.AnalyzeAsync(path);
                var importDialog = new MidiImportDialog(fileInfo)
                {
                    Owner = System.Windows.Application.Current.MainWindow,
                };
                if (importDialog.ShowDialog() != true || importDialog.Options is null)
                {
                    SetStatus("Status_Ready");
                    return;
                }

                var result = await _midiScoreImporter.ImportAsync(path, importDialog.Options);
                var fileTitle = Path.GetFileNameWithoutExtension(path);
                var score = result.Score with
                {
                    Metadata = result.Score.Metadata with { Title = fileTitle },
                };
                _workspace.ImportScore(score);
                CurrentSourcePath = path;
                RefreshFromWorkspace();
                SetStatus(
                    "Status_MidiImported",
                    result.Report.ImportedNoteCount,
                    result.Report.FoldedNoteCount,
                    result.Report.DroppedNoteCount,
                    result.Report.IgnoredPercussionNoteCount);
                return;
            }

            await _workspace.LoadAsync(path);
            CurrentSourcePath = path;
            CancelAutosave();
            _recoveryService.Discard();
            RefreshFromWorkspace();
            SetStatus("Status_Opened", Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            AppLogger.Error($"Failed to open score '{path}'.", exception);
            SetStatus("Status_OpenFailed", exception.Message);
        }
    }

    private async Task OpenScoreFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = _localizationService.GetString("Dialog_OpenScoreFolder"),
            InitialDirectory = ScoreFolder.Length > 0 ? ScoreFolder : _lastBrowseDirectory,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _scoreFolder = Path.GetFullPath(dialog.FolderName);
        _lastBrowseDirectory = _scoreFolder;
        _userSettingsService.SetScoreFolder(_scoreFolder);
        OnPropertyChanged(nameof(ScoreFolder));
        OnPropertyChanged(nameof(ScoreFolderName));
        RefreshScoreFolderCommand.NotifyCanExecuteChanged();
        await RefreshScoreFolderAsync();
    }

    private async Task RefreshScoreFolderAsync()
    {
        if (!Directory.Exists(ScoreFolder))
        {
            _allScoreFolderFiles = [];
            ApplyScoreFolderFilter();
            return;
        }

        var folder = ScoreFolder;
        _allScoreFolderFiles = await Task.Run(() => Directory
            .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedScorePath)
            .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
            .Select(path => new ScoreFolderFile(
                path,
                Path.GetFileNameWithoutExtension(path),
                Path.GetExtension(path).TrimStart('.').ToUpperInvariant()))
            .ToArray());
        ApplyScoreFolderFilter();
    }

    public async Task<bool> RenameScoreFileAsync(ScoreFolderFile file, string newTitle)
    {
        newTitle = newTitle.Trim();
        if (newTitle.Length == 0)
        {
            return false;
        }

        var sourcePath = Path.GetFullPath(file.Path);
        var extension = Path.GetExtension(sourcePath);
        var destinationPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, newTitle + extension);
        if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(destinationPath))
        {
            SetStatus("Status_RenameScoreExists", Path.GetFileName(destinationPath));
            return false;
        }

        try
        {
            var isCurrent = string.Equals(
                sourcePath,
                CurrentSourcePath,
                StringComparison.OrdinalIgnoreCase);
            if (extension.Equals(".gpiano", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                await _workspace.RenameStoredScoreAsync(sourcePath, destinationPath, newTitle);
            }
            else
            {
                MoveFileAllowingCaseOnlyRename(sourcePath, destinationPath);
                if (isCurrent)
                {
                    _workspace.RelabelCurrentScore(newTitle);
                }
            }

            if (isCurrent)
            {
                CurrentSourcePath = destinationPath;
                RefreshFromWorkspace();
            }

            await RefreshScoreFolderAsync();
            SetStatus("Status_ScoreRenamed", newTitle);
            return true;
        }
        catch (Exception exception)
        {
            AppLogger.Error($"Failed to rename score '{sourcePath}'.", exception);
            SetStatus("Status_RenameScoreFailed", exception.Message);
            return false;
        }
    }

    private static void MoveFileAllowingCaseOnlyRename(string sourcePath, string destinationPath)
    {
        if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Move(sourcePath, temporaryPath);
        try
        {
            File.Move(temporaryPath, destinationPath);
        }
        catch
        {
            if (File.Exists(temporaryPath) && !File.Exists(sourcePath))
            {
                File.Move(temporaryPath, sourcePath);
            }

            throw;
        }
    }

    private void ApplyScoreFolderFilter()
    {
        var query = ScoreFolderSearch.Trim();
        var files = query.Length == 0
            ? _allScoreFolderFiles
            : _allScoreFolderFiles.Where(file =>
                file.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        ScoreFolderFiles.Clear();
        foreach (var file in files)
        {
            ScoreFolderFiles.Add(file);
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
            CurrentSourcePath = Path.GetFullPath(path);
            CancelAutosave();
            _recoveryService.Discard();
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(WindowTitle));
            SetStatus("Status_Saved", path);
            if (ScoreFolder.Length > 0 && string.Equals(
                    Path.GetDirectoryName(CurrentSourcePath),
                    ScoreFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                await RefreshScoreFolderAsync();
            }
            return true;
        }
        catch (Exception exception)
        {
            AppLogger.Error($"Failed to save score '{path}'.", exception);
            SetStatus("Status_SaveFailed", exception.Message);
            return false;
        }
    }

    public void RestoreRecovery(ScoreRecoverySnapshot snapshot)
    {
        _workspace.RestoreScore(snapshot.Score, snapshot.OriginalPath);
        RefreshFromWorkspace();
        SetStatus("Status_RecoveryRestored");
        ScheduleAutosave();
    }

    public void DiscardRecovery() => _recoveryService.Discard();

    private void ScheduleAutosave()
    {
        CancelAutosave();
        if (!_workspace.IsDirty)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _autosaveCancellation = cancellation;
        _ = AutosaveAfterDelayAsync(cancellation);
    }

    private async Task AutosaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
            await _recoveryService.SaveAsync(
                _workspace.CurrentScore,
                _workspace.CurrentPath,
                cancellation.Token);
            AppLogger.Info("Recovery snapshot saved.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.Error("Failed to save recovery snapshot.", exception);
        }
        finally
        {
            if (ReferenceEquals(_autosaveCancellation, cancellation))
            {
                _autosaveCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CancelAutosave()
    {
        _autosaveCancellation?.Cancel();
        _autosaveCancellation = null;
    }

    private async Task PlayAsync()
    {
        if (!CurrentScore.Tracks.Any(track => track.Notes.Count > 0))
        {
            SetStatus("Status_CannotPlayEmptyScore");
            return;
        }

        _playbackService.TryFocusFirstPlaybackTarget();

        // Capture exactly what is currently visible in the editor. Later edits or
        // saves must not switch the score underneath an active game playback.
        var scoreToPlay = CurrentScore;
        using var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        PlaybackChordIndex = 0;
        PlaybackChordCount = 0;
        PlaybackCurrentKeys = "—";
        IsPlaying = true;

        var progress = new Progress<PlaybackProgress>(playbackProgress =>
        {
            if (playbackProgress.ChordCount > 0)
            {
                PlaybackChordCount = playbackProgress.ChordCount;
            }

            if (playbackProgress.Phase is PlaybackPhase.Playing or PlaybackPhase.Completed)
            {
                PlaybackChordIndex = playbackProgress.ChordIndex;
            }

            switch (playbackProgress.Phase)
            {
                case PlaybackPhase.WaitingForTarget:
                    PlaybackCurrentKeys = "—";
                    IsPlaybackPaused = true;
                    SetStatus("Status_PlayWaitingForTarget");
                    break;
                case PlaybackPhase.Countdown:
                    PlaybackCurrentKeys = "—";
                    IsPlaybackPaused = false;
                    SetStatus("Status_PlayCountdown", playbackProgress.CountdownSeconds);
                    break;
                case PlaybackPhase.Paused:
                    PlaybackCurrentKeys = "—";
                    IsPlaybackPaused = true;
                    SetStatus(
                        playbackProgress.PauseReason == PlaybackPauseReason.Manual
                            ? "Status_PlayPausedManual"
                            : "Status_PlayPaused");
                    break;
                case PlaybackPhase.Resumed:
                    PlaybackCurrentKeys = "—";
                    IsPlaybackPaused = false;
                    SetStatus("Status_PlayResumed");
                    break;
                case PlaybackPhase.Playing:
                    IsPlaybackPaused = false;
                    var keys = playbackProgress.CurrentKeys is null
                        ? string.Empty
                        : string.Concat(playbackProgress.CurrentKeys);
                    PlaybackCurrentKeys = keys;
                    SetStatus(
                        "Status_Playing",
                        playbackProgress.ChordIndex,
                        playbackProgress.ChordCount,
                        keys);
                    break;
                case PlaybackPhase.Completed:
                    PlaybackCurrentKeys = "—";
                    SetStatus("Status_PlayCompleted", playbackProgress.SkippedNoteCount);
                    break;
            }
        });

        try
        {
            await _playbackService.PlayAsync(
                scoreToPlay,
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
            _playbackService.TryFocusFirstPlaybackTarget();
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
        _userSettingsService.SetTheme(theme);
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
        _userSettingsService.SetLanguage(language);
        SetStatus("Status_LanguageChanged");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (CurrentSourcePath is null &&
            ScoreTitle is "未命名曲谱" or "Untitled score")
        {
            var localizedTitle = _localizationService.GetString("Score_Untitled");
            _workspace.RelabelCurrentScore(localizedTitle);
            ScoreTitle = localizedTitle;
            OnPropertyChanged(nameof(CurrentScore));
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsChinese));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(ScoreFolderName));
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

public sealed record ScoreFolderFile(string Path, string DisplayName, string FileType);
