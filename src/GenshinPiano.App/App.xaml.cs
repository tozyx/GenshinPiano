using System.Windows;
using GenshinPiano.Application.Workspace;
using GenshinPiano.App.ViewModels;
using GenshinPiano.Infrastructure.Serialization;

namespace GenshinPiano.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serializer = new JsonScoreDocumentSerializer();
        var workspace = new ScoreWorkspace(serializer);
        var viewModel = new MainWindowViewModel(workspace);

        var mainWindow = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
