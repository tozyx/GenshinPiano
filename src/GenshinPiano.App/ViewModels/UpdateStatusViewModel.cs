using GenshinPiano.App.Commands;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Updates;
using System.IO;

namespace GenshinPiano.App.ViewModels;

public sealed class UpdateStatusViewModel : ObservableObject, IDisposable
{
    private readonly UpdateCoordinator _coordinator;
    private readonly IUserSettingsService _settings;
    private readonly ILocalizationService _localization;
    private CancellationTokenSource? _operationCancellation;
    private UpdateState _state;
    private bool _manualCheckStarted;

    public UpdateStatusViewModel(
        UpdateCoordinator coordinator,
        IUserSettingsService settings,
        ILocalizationService localization)
    {
        _coordinator = coordinator;
        _settings = settings;
        _localization = localization;
        _state = coordinator.State;
        _coordinator.StateChanged += Coordinator_OnStateChanged;
        _localization.LanguageChanged += Localization_OnLanguageChanged;
        CheckForUpdatesCommand = new AsyncRelayCommand(
            CheckForUpdatesAsync,
            () => NetworkAccessEnabled);
    }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public event EventHandler? ReadyUpdateRequested;

    public bool NetworkAccessEnabled
    {
        get => _settings.Current.Update.NetworkAccessEnabled;
        set
        {
            if (value == NetworkAccessEnabled)
            {
                return;
            }

            _settings.SetNetworkAccessEnabled(value);
            OnPropertyChanged();
            CheckForUpdatesCommand.NotifyCanExecuteChanged();
            if (!value)
            {
                CancelActiveOperation();
                _coordinator.Disable();
            }
            else
            {
                _coordinator.Reset();
            }
        }
    }

    public bool AutomaticUpdatesEnabled
    {
        get => _settings.Current.Update.AutomaticUpdatesEnabled;
        set
        {
            if (value == AutomaticUpdatesEnabled)
            {
                return;
            }

            _settings.SetAutomaticUpdatesEnabled(value);
            OnPropertyChanged();
            if (!value)
            {
                CancelActiveOperation();
            }
        }
    }

    public bool PreviewUpdatesEnabled
    {
        get => string.Equals(_settings.Current.Update.Channel, "preview", StringComparison.Ordinal);
        set
        {
            if (value == PreviewUpdatesEnabled) return;
            _settings.SetUpdateChannel(value ? "preview" : "stable");
            OnPropertyChanged();
        }
    }

    public UpdateStage Stage => _state.Stage;

    public string CurrentVersionText => $"v{_coordinator.CurrentVersion}";

    public double Progress => _state.Progress;

    public int ProgressPercent => (int)Math.Round(Math.Clamp(Progress, 0, 1) * 100);

    public bool IsIndicatorVisible => Stage is not UpdateStage.Idle and not UpdateStage.Disabled;

    public bool IsIndeterminate => Stage is UpdateStage.Checking or UpdateStage.Verifying;

    public bool IsReady => Stage == UpdateStage.Ready;

    public UpdateState CurrentState => _state;

    public bool CanInstall => IsReady && !string.IsNullOrWhiteSpace(_state.DownloadedPath) &&
                              File.Exists(_state.DownloadedPath);

    public string StatusText => Stage switch
    {
        UpdateStage.Disabled => _localization.GetString("Update_StatusDisabled"),
        UpdateStage.Checking => _localization.GetString("Update_StatusChecking"),
        UpdateStage.Available => _localization.GetString("Update_StatusAvailable", _state.AvailableVersion),
        UpdateStage.Downloading => _localization.GetString("Update_StatusDownloading", ProgressPercent),
        UpdateStage.Verifying => _localization.GetString("Update_StatusVerifying"),
        UpdateStage.Ready => _localization.GetString("Update_StatusReady", _state.AvailableVersion),
        UpdateStage.Failed => _localization.GetString("Update_StatusFailed", _state.ErrorMessage ?? string.Empty),
        _ => _localization.GetString("Update_StatusIdle"),
    };

    public async Task StartAutomaticCheckAsync(CancellationToken cancellationToken = default)
    {
        if (_manualCheckStarted || !NetworkAccessEnabled || !AutomaticUpdatesEnabled)
        {
            return;
        }

        await RunCheckAsync(automaticallyDownload: true, cancellationToken);
    }

    private Task CheckForUpdatesAsync()
    {
        _manualCheckStarted = true;
        if (CanInstall)
        {
            ReadyUpdateRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
        return RunCheckAsync(AutomaticUpdatesEnabled, CancellationToken.None);
    }

    public Task DownloadAvailableUpdateAsync()
    {
        _manualCheckStarted = true;
        return RunCheckAsync(automaticallyDownload: true, CancellationToken.None);
    }

    private async Task RunCheckAsync(bool automaticallyDownload, CancellationToken cancellationToken)
    {
        CancelActiveOperation();
        var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = operationCancellation;
        try
        {
            await _coordinator.CheckAsync(
                NetworkAccessEnabled,
                automaticallyDownload,
                _settings.Current.Update.Channel,
                operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, operationCancellation))
            {
                _operationCancellation = null;
            }
            operationCancellation.Dispose();
        }
    }

    private void CancelActiveOperation()
    {
        _operationCancellation?.Cancel();
    }

    private void Coordinator_OnStateChanged(object? sender, UpdateState state)
    {
        _state = state;
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsIndicatorVisible));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(StatusText));
    }

    private void Localization_OnLanguageChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(StatusText));

    public void Dispose()
    {
        CancelActiveOperation();
        _coordinator.StateChanged -= Coordinator_OnStateChanged;
        _localization.LanguageChanged -= Localization_OnLanguageChanged;
        _operationCancellation?.Dispose();
    }
}
