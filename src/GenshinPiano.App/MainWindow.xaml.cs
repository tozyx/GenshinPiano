using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace GenshinPiano.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e) => Close();

    private void MenuItem_OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || !ReferenceEquals(e.OriginalSource, menuItem))
        {
            return;
        }

        menuItem.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => RemovePopupShadow(menuItem));
    }

    private static void RemovePopupShadow(MenuItem menuItem)
    {
        if (menuItem.Template.FindName("PART_Popup", menuItem) is not Popup { Child: DependencyObject popupContent })
        {
            return;
        }

        var submenuBorder = FindDescendant<Border>(popupContent, "SubmenuBorder");
        if (submenuBorder is not null)
        {
            submenuBorder.Effect = null;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        if (root is T matchingElement && matchingElement.Name == name)
        {
            return matchingElement;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var match = FindDescendant<T>(VisualTreeHelper.GetChild(root, index), name);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
