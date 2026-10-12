using System.Windows;
using System.Windows.Controls;
using MockDataStudio.App.Samples;
using MockDataStudio.App.Views;

namespace MockDataStudio.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavList.ItemsSource = SampleData.Navigation;
        NavList.SelectedIndex = 0;
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavItem item)
        {
            return;
        }

        ContentHost.Content = item.Key switch
        {
            "home" => new HomeView(),
            "connections" => new ConnectionsView(),
            "explorer" or "sql" => new ExplorerView(),
            "digest" => new SchemaDigestView(),
            "templates" => new TemplatesView(),
            "mapping" => new MappingView(),
            _ => new PlaceholderView(item.Title),
        };
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
