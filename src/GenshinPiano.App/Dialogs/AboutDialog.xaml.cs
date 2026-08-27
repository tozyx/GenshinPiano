using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using GenshinPiano.App.Services;
using GenshinPiano.Application.Updates;

namespace GenshinPiano.App.Dialogs;

public partial class AboutDialog : Window
{
    private const string ProjectUrl = "https://github.com/tozyx/GenshinPiano/";

    public AboutDialog()
        : this(GetDisplayVersion())
    {
    }

    public AboutDialog(string displayVersion)
    {
        InitializeComponent();

        var dpiScale = 1.0;

        if (PresentationSource.FromVisual(this) is { } source)
        {
            dpiScale = source.CompositionTarget.TransformToDevice.M11;
        }

        var targetSize = (int)Math.Round(48 * dpiScale);
        var uri = new Uri(
            "pack://application:,,,/Assets/Icons/GenshinPiano.ico",
            UriKind.Absolute);

        var decoder = new IconBitmapDecoder(
            uri,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var frame = decoder.Frames
            .OrderBy(frame => Math.Abs(frame.PixelWidth - targetSize))
            .First();

        AboutIcon.Source = frame;

        VersionText.Text = string.Format(
            (string)FindResource("About_Version"),
            displayVersion);
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(AboutDialog).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return SemanticVersion.TryParse(version, out var semanticVersion)
            ? $"v{semanticVersion}"
            : "unknown";
    }

    private void OpenGitHub_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ExternalLinkService.TryOpen(ProjectUrl, out var exception))
        {
            AppLogger.Warning($"Could not open GitHub project page: {exception?.Message}");
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
