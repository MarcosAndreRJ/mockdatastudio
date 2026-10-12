using System.Windows.Controls;
using MockDataStudio.App.Controls;
using MockDataStudio.App.Samples;

namespace MockDataStudio.App.Views;

public partial class SchemaDigestView : UserControl
{
    public SchemaDigestView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            DigestTreeView.UpdateLayout();
            DigestTreeView.ApplyInitialExpansion();
        };
    }
}
