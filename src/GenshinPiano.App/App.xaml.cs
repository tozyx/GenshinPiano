using System.Windows;
using GenshinPiano.Application.Workspace;
using GenshinPiano.Application.Conversion;
using GenshinPiano.Application.Playback;
using GenshinPiano.App.Services;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Infrastructure.Input;
using GenshinPiano.Infrastructure.Legacy;
using GenshinPiano.Infrastructure.Midi;
using GenshinPiano.Infrastructure.Serialization;
using GenshinPiano.Application.Updates;
using GenshinPiano.Infrastructure.Updates;
using GenshinPiano.Application.Ocr;
using GenshinPiano.Infrastructure.Ocr;
using System.Reflection;
using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Text.Json;
using System.Windows.Threading;

namespace GenshinPiano.App;

public partial class App : System.Windows.Application
{
    private WindowsGlobalEscapeListener? _escapeListener;
    private WindowsKeyboardInput? _keyboardInput;
    private WindowsMidiOutput? _midiOutput;
    private HttpClient? _updateMetadataHttpClient;
    private HttpClient? _updateDownloadHttpClient;
    private SingleInstanceCoordinator? _singleInstance;

    public WindowsNotificationService NotificationService { get; private set; } = null!;

    public WindowsTaskbarProgressService TaskbarProgressService { get; private set; } = null!;

    public IUserSettingsService UserSettingsService { get; private set; } = null!;

    public ScoreAuditionService? AuditionService { get; private set; }

    public MidiBatchConversionService MidiBatchConversionService { get; private set; } = null!;

    public IOcrAddonService OcrAddonService { get; private set; } = null!;

    public OcrAddonPackageManager OcrAddonPackageManager { get; private set; } = null!;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            TryEmergencyReleaseAllKeys("WPF dispatcher exception");
            AppLogger.WriteCrashReport("WPF dispatcher", args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            TryEmergencyReleaseAllKeys("AppDomain exception");
            AppLogger.WriteCrashReport(
                "AppDomain",
                args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            TryEmergencyReleaseAllKeys("process exit");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.WriteCrashReport("Unobserved task", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Set this before creating any HWND or touching taskbar APIs. The OCR
        // engine uses a different explicit ID so its short-lived process never
        // causes Windows to rebuild the main window's application group.
        var appIdentityConfigured = WindowsAppIdentity.TrySetCurrentProcess(
            WindowsAppIdentity.MainApplicationId);

        _singleInstance = new SingleInstanceCoordinator();
        var startupScorePath = FindSupportedStartupPath(e.Args);
        if (!_singleInstance.TryAcquire())
        {
            var signaled = SingleInstanceCoordinator.SignalActivationRequest();
            var forwarded = false;
            try
            {
                forwarded = _singleInstance.Forward(new SingleInstanceRequest(
                    startupScorePath,
                    IsCurrentProcessElevated()));
            }
            catch
            {
                forwarded = false;
            }

            if (!signaled && !forwarded)
            {
                SingleInstanceCoordinator.TryActivateExistingInstance();
            }

            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        AppLogger.Initialize();
        AppLogger.Info("Application startup began; primary single-instance lock acquired.");
        if (!appIdentityConfigured)
        {
            AppLogger.Warning("The explicit Windows AppUserModelID could not be configured.");
        }

        var serializer = new JsonScoreDocumentSerializer();
        var recoveryService = new ScoreRecoveryService(serializer);
        UserSettingsService = new UserSettingsService();
        NotificationService = new WindowsNotificationService();
        TaskbarProgressService = new WindowsTaskbarProgressService();
        var themeService = new ThemeService();
        var localizationService = new LocalizationService();
        var appearance = UserSettingsService.Current.Appearance;
        if (Enum.TryParse<AppTheme>(appearance.Theme, out var configuredTheme))
        {
            themeService.Apply(configuredTheme);
        }
        if (Enum.TryParse<AppLanguage>(appearance.Language, out var configuredLanguage))
        {
            localizationService.Apply(configuredLanguage);
        }
        var workspace = new ScoreWorkspace(
            serializer,
            localizationService.GetString("Score_Untitled"));
        try
        {
            _midiOutput = new WindowsMidiOutput();
            AuditionService = new ScoreAuditionService(_midiOutput);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            AuditionService = null;
            AppLogger.Warning($"Windows MIDI output is unavailable: {exception.Message}");
        }
        _keyboardInput = new WindowsKeyboardInput();
        var playbackService = new ScorePlaybackService(
            _keyboardInput,
            new WindowsForegroundProcessGuard());
        var legacyConversionService = new LegacyBatchConversionService(
            new LegacyGenshinPianoImporter(),
            serializer);
        var midiScoreImporter = new DryWetMidiScoreImporter();
        MidiBatchConversionService = new MidiBatchConversionService(midiScoreImporter, serializer);
        OcrAddonService = new ExternalOcrAddonService(
            Path.Combine(AppContext.BaseDirectory, "addons", "ocr"));
        var assembly = typeof(App).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!SemanticVersion.TryParse(informationalVersion, out var currentVersion))
        {
            currentVersion = new SemanticVersion(3, 0, 0);
        }
        var isFrameworkDependent = string.Equals(
            assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "GenshinPianoSelfContained")?
                .Value,
            "false",
            StringComparison.OrdinalIgnoreCase);
        var simulationManifestPath = Path.Combine(
            AppContext.BaseDirectory,
            "config",
            "update-simulation.json");
        _updateMetadataHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _updateDownloadHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var publicKeyResource = GetResourceStream(
            new Uri("/Assets/Security/UpdateSigningPublicKey.xml", UriKind.Relative)) ??
            throw new InvalidOperationException("The update signing public key resource is missing.");
        using var publicKeyReader = new StreamReader(publicKeyResource.Stream);
        var signedPackageVerifier = new SignedUpdatePackageVerifier(publicKeyReader.ReadToEnd());
        var ocrSource = new RacingUpdateSource(
        [
            new OcrAddonReleaseSource(
                _updateMetadataHttpClient, "GitCode",
                new Uri("https://api.gitcode.com/api/v5/repos/tozyx/GenshinPiano/releases")),
            new OcrAddonReleaseSource(
                _updateMetadataHttpClient, "GitHub",
                new Uri("https://api.github.com/repos/tozyx/GenshinPiano/releases")),
        ], diagnostic: AppLogger.Info);
        OcrAddonPackageManager = new OcrAddonPackageManager(
            ocrSource,
            new ResumableUpdatePackageDownloader(
                _updateDownloadHttpClient,
                Path.Combine(AppContext.BaseDirectory, "update-cache", "downloads", "ocr")),
            signedPackageVerifier,
            OcrAddonService,
            Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar));
        IUpdateSource updateSource;
        IUpdatePackageDownloader updateDownloader;
        IUpdatePackageVerifier updateVerifier;
        if (File.Exists(simulationManifestPath))
        {
            updateSource = new LocalSimulationUpdateSource(currentVersion, simulationManifestPath);
            updateDownloader = new SimulatedUpdatePackageDownloader();
            updateVerifier = new SimulatedUpdatePackageVerifier();
        }
        else
        {
            updateSource = new RacingUpdateSource(
            [
                new ReleaseMirrorUpdateSource(
                    _updateMetadataHttpClient,
                    "GitCode",
                    new Uri("https://api.gitcode.com/api/v5/repos/tozyx/GenshinPiano/releases"),
                    isFrameworkDependent,
                    currentVersion),
                new ReleaseMirrorUpdateSource(
                    _updateMetadataHttpClient,
                    "GitHub",
                    new Uri("https://api.github.com/repos/tozyx/GenshinPiano/releases"),
                    isFrameworkDependent,
                    currentVersion),
            ], diagnostic: AppLogger.Info);
            updateDownloader = new ResumableUpdatePackageDownloader(
                _updateDownloadHttpClient,
                Path.Combine(AppContext.BaseDirectory, "update-cache", "downloads"));
            updateVerifier = signedPackageVerifier;
        }
        var updateCoordinator = new UpdateCoordinator(
            currentVersion,
            updateSource,
            updateDownloader,
            updateVerifier);
        var updateStatus = new UpdateStatusViewModel(
            updateCoordinator,
            UserSettingsService,
            localizationService);
        var viewModel = new MainWindowViewModel(
            workspace,
            themeService,
            localizationService,
            UserSettingsService,
            playbackService,
            legacyConversionService,
            midiScoreImporter,
            recoveryService,
            updateStatus);

        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = mainWindow;
        mainWindow.Show();
        _singleInstance.StartListening(request =>
            Dispatcher.BeginInvoke(new Action(async () =>
                await mainWindow.HandleSingleInstanceRequestAsync(request))));
        ShowCompletedUpdateNotes(mainWindow);

        _ = StartAutomaticUpdateCheckAsync(updateStatus);

        _ = CompleteStartupDocumentFlowAsync(
            mainWindow,
            viewModel,
            recoveryService,
            startupScorePath);

        try
        {
            _escapeListener = new WindowsGlobalEscapeListener();
            _escapeListener.EscapePressed += (_, _) =>
                mainWindow.Dispatcher.BeginInvoke(viewModel.HandleGlobalEscape);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            viewModel.ReportGlobalEscapeUnavailable(exception.Message);
            AppLogger.Warning($"Global Escape listener is unavailable: {exception.Message}");
        }


        AppLogger.Info("Application startup completed.");
    }

    private static string? FindSupportedStartupPath(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            try
            {
                var path = Path.GetFullPath(argument);
                if (MainWindowViewModel.IsSupportedScorePath(path)) return path;
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                AppLogger.Warning($"Ignored invalid startup path '{argument}': {exception.Message}");
            }
        }
        return null;
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task StartAutomaticUpdateCheckAsync(UpdateStatusViewModel updateStatus)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            await updateStatus.StartAutomaticCheckAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Automatic update check failed: {exception.Message}");
        }
    }

    private static void ShowCompletedUpdateNotes(MainWindow owner)
    {
        var markerPath = Path.Combine(AppContext.BaseDirectory, "update-cache", "update-completed.json");
        if (!File.Exists(markerPath)) return;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(markerPath));
            var version = json.RootElement.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString() ?? string.Empty : string.Empty;
            var notes = json.RootElement.TryGetProperty("releaseNotes", out var notesElement)
                ? notesElement.GetString() : null;
            ReleaseNotesCacheService.Save(version, notes);
            File.Delete(markerPath);
            new Dialogs.ReleaseNotesDialog(version, notes) { Owner = owner }.ShowDialog();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Could not display update release notes: {exception.Message}");
        }
    }

    private static async Task CompleteStartupDocumentFlowAsync(
        MainWindow owner,
        MainWindowViewModel viewModel,
        ScoreRecoveryService recoveryService,
        string? startupScorePath)
    {
        if (startupScorePath is not null)
        {
            await viewModel.OpenPathAsync(startupScorePath);
        }
        else if (recoveryService.HasRecovery)
        {
            await RestorePreviousSessionAsync(owner, viewModel, recoveryService);
        }

        await ShowFileAssociationRepairPromptIfNeededAsync(owner, viewModel);
    }

    private static async Task ShowFileAssociationRepairPromptIfNeededAsync(
        MainWindow owner,
        MainWindowViewModel viewModel)
    {
        try
        {
            await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (owner.Dispatcher.HasShutdownStarted || !owner.IsVisible)
            {
                return;
            }

            var state = FileAssociationService.GetState();
            if (!state.ExtensionPointsToGenshinPiano || state.OpensWithCurrentExecutable)
            {
                return;
            }

            var dialog = new Dialogs.UpdateReadyDialog(
                "FileAssociationRepair_Title",
                "FileAssociationRepair_Message",
                "FileAssociationRepair_Action")
            {
                Owner = owner,
            };
            dialog.ShowDialog();

            if (!dialog.RestartRequested)
            {
                viewModel.NotifyStatus("Status_FileAssociationRepairSkipped");
                return;
            }

            FileAssociationService.RegisterGpianoAssociation();
            viewModel.NotifyStatus("Status_FileAssociationRegistered");
            AppLogger.Info(
                ".gpiano file association pointed to a different executable and was repaired on startup.");
        }
        catch (Exception exception)
        {
            viewModel.NotifyStatus("Status_FileAssociationRegisterFailed", exception.Message);
            AppLogger.Warning($"Startup file association repair check failed: {exception.Message}");
        }
    }

    private static async Task RestorePreviousSessionAsync(
        MainWindow owner,
        MainWindowViewModel viewModel,
        ScoreRecoveryService recoveryService)
    {
        try
        {
            var snapshot = await recoveryService.LoadAsync();
            if (snapshot is null)
            {
                return;
            }

            var dialog = new Dialogs.RecoveryDialog(snapshot.SavedAt) { Owner = owner };
            dialog.ShowDialog();
            if (dialog.ShouldRestore)
            {
                viewModel.RestoreRecovery(snapshot);
            }
            else
            {
                viewModel.DiscardRecovery();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("Failed to restore autosaved score.", exception);
            recoveryService.Discard();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info($"Application exiting with code {e.ApplicationExitCode}.");
        TryEmergencyReleaseAllKeys("application exit");
        _escapeListener?.Dispose();
        _midiOutput?.Dispose();
        _updateMetadataHttpClient?.Dispose();
        _updateDownloadHttpClient?.Dispose();
        _singleInstance?.Dispose();
        NotificationService?.Dispose();
        base.OnExit(e);
    }

    private void TryEmergencyReleaseAllKeys(string reason)
    {
        try
        {
            _keyboardInput?.EmergencyReleaseAllKeys();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Emergency key release failed during {reason}: {exception.Message}");
        }
    }
}
