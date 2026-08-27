using System.Reflection;
using System.Windows;
using GenshinPiano.App.Services;

namespace GenshinPiano.App.Dialogs;

public partial class AboutDialog : Window
{
    private const string ProjectUrl = "https://github.com/tozyx/GenshinPiano/";

    public AboutDialog()
    {
        InitializeComponent();
        VersionText.Text = string.Format(
            (string)FindResource("About_Version"),
            GetDisplayVersion());
    }

    private static string GetDisplayVersion()
    {
        var version = typeof(AboutDialog).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "unknown" : version;
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
