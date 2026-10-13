using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MockDataStudio.App.ViewModels;
using MockDataStudio.Application.Explorer;

namespace MockDataStudio.App.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
    }

    // Selecionar um nó só preenche o inspetor; o texto do editor nunca é trocado por isso.
    private async void ObjectTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ExplorerViewModel viewModel && e.NewValue is ExplorerNode node)
        {
            await viewModel.SelectNodeAsync(node);
        }
    }

    private void ForeignKeyGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ExplorerViewModel viewModel && ForeignKeyGrid.SelectedItem is ForeignKeyInfo fk)
        {
            viewModel.OpenReferencedTableCommand.Execute(fk);
        }
    }
}
