using System.Windows.Controls;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MockDataStudio.App.Controls;
using MockDataStudio.App.Samples;

namespace MockDataStudio.App.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();

        QueryEditor.SyntaxHighlighting = LoadSqlHighlighting();
        QueryEditor.Text = SampleData.ExplorerQuery;

        Loaded += (_, _) =>
        {
            ObjectTree.UpdateLayout();
            ObjectTree.ApplyInitialExpansion();
        };
    }

    private static IHighlightingDefinition LoadSqlHighlighting()
    {
        const string resourceName = "MockDataStudio.App.Resources.Highlighting.sql.xshd";
        using var stream = typeof(ExplorerView).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso de realce não encontrado: {resourceName}");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
