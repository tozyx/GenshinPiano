using System.Reflection;
using System.Windows;
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

        VersionText.Text = string.Format(
            (string)FindResource("About_Version"),
            string.IsNullOrWhiteSpace(displayVersion)
                ? GetDisplayVersion()
                : displayVersion);
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
