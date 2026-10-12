using System.Windows.Controls;
using MockDataStudio.App.Controls;

namespace MockDataStudio.App.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            ObjectTree.UpdateLayout();
            ObjectTree.ApplyInitialExpansion();
        };
    }
}
