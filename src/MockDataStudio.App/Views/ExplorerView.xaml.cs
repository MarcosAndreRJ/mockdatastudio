using System.Windows;
using System.Windows.Controls;
using MockDataStudio.App.ViewModels;

namespace MockDataStudio.App.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
    }

    private async void ObjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ExplorerViewModel viewModel && e.NewValue is ExplorerNode node)
        {
            await viewModel.SelectNodeAsync(node);
        }
    }
}
