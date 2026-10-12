using System.Windows.Controls;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MockDataStudio.App.ViewModels;

namespace MockDataStudio.App.Views;

public partial class SqlEditorView : UserControl
{
    public SqlEditorView()
    {
        InitializeComponent();

        QueryEditor.SyntaxHighlighting = LoadSqlHighlighting();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is SqlEditorViewModel viewModel)
            {
                QueryEditor.Text = viewModel.QueryText;
            }
        };
    }

    private static IHighlightingDefinition LoadSqlHighlighting()
    {
        const string resourceName = "MockDataStudio.App.Resources.Highlighting.sql.xshd";
        using var stream = typeof(SqlEditorView).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso de realce não encontrado: {resourceName}");
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
