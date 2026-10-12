using System.Windows;
using MockDataStudio.App.ViewModels;

namespace MockDataStudio.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
