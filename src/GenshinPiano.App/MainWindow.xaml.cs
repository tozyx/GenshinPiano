using System.Windows;

namespace GenshinPiano.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) => Close();
}
