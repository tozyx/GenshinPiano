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
using System.Reflection;
using System.IO;
using System.Net.Http;

namespace GenshinPiano.App;

public partial class App : System.Windows.Application
{
    private WindowsGlobalEscapeListener? _escapeListener;
    private WindowsMidiOutput? _midiOutput;
    private HttpClient? _updateMetadataHttpClient;
    private HttpClient? _updateDownloadHttpClient;

    public IUserSettingsService UserSettingsService { get; private set; } = null!;

    public ScoreAuditionService? AuditionService { get; private set; }

    public MidiBatchConversionService MidiBatchConversionService { get; private set; } = null!;

    public App()
    {
        AppLogger.Initialize();
        DispatcherUnhandledException += (_, args) =>
            AppLogger.WriteCrashReport("WPF dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.WriteCrashReport(
                "AppDomain",
                args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.WriteCrashReport("Unobserved task", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Info("Application startup began.");

        var serializer = new JsonScoreDocumentSerializer();
        var recoveryService = new ScoreRecoveryService(serializer);
        UserSettingsService = new UserSettingsService();
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
        var playbackService = new ScorePlaybackService(
            new WindowsKeyboardInput(),
            new WindowsForegroundProcessGuard());
        var legacyConversionService = new LegacyBatchConversionService(
            new LegacyGenshinPianoImporter(),
            serializer);
        var midiScoreImporter = new DryWetMidiScoreImporter();
        MidiBatchConversionService = new MidiBatchConversionService(midiScoreImporter, serializer);
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
            _updateMetadataHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            _updateDownloadHttpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
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
            ]);
            updateDownloader = new ResumableUpdatePackageDownloader(
                _updateDownloadHttpClient,
                Path.Combine(AppContext.BaseDirectory, "update-cache", "downloads"));
            updateVerifier = new Sha256UpdatePackageVerifier();
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

        _ = StartAutomaticUpdateCheckAsync(updateStatus);

        if (recoveryService.HasRecovery)
        {
            _ = RestorePreviousSessionAsync(mainWindow, viewModel, recoveryService);
        }

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
        _escapeListener?.Dispose();
        _midiOutput?.Dispose();
        _updateMetadataHttpClient?.Dispose();
        _updateDownloadHttpClient?.Dispose();
        base.OnExit(e);
    }
}
