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

namespace GenshinPiano.App;

public partial class App : System.Windows.Application
{
    private WindowsGlobalEscapeListener? _escapeListener;
    private WindowsMidiOutput? _midiOutput;

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
        var workspace = new ScoreWorkspace(serializer);
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
        var viewModel = new MainWindowViewModel(
            workspace,
            themeService,
            localizationService,
            UserSettingsService,
            playbackService,
            legacyConversionService,
            midiScoreImporter);

        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = mainWindow;
        mainWindow.Show();

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

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info($"Application exiting with code {e.ApplicationExitCode}.");
        _escapeListener?.Dispose();
        _midiOutput?.Dispose();
        base.OnExit(e);
    }
}
