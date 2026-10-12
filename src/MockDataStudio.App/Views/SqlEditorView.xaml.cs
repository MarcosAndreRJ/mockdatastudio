using System.ComponentModel;
using System.Windows.Controls;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MockDataStudio.App.ViewModels;

namespace MockDataStudio.App.Views;

public partial class SqlEditorView : UserControl
{
    private SqlEditorViewModel? _viewModel;

    public SqlEditorView()
    {
        InitializeComponent();

        QueryEditor.SyntaxHighlighting = LoadSqlHighlighting();
        QueryEditor.TextChanged += (_, _) =>
        {
            if (_viewModel is not null && _viewModel.QueryText != QueryEditor.Text)
            {
                _viewModel.QueryText = QueryEditor.Text;
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _viewModel = DataContext as SqlEditorViewModel;
            if (_viewModel is not null)
            {
                QueryEditor.Text = _viewModel.QueryText;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
    }

    // O texto pode mudar fora do editor (ex.: seleção de tabela no Explorer gera um SELECT).
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SqlEditorViewModel.QueryText) && _viewModel is not null && QueryEditor.Text != _viewModel.QueryText)
        {
            QueryEditor.Text = _viewModel.QueryText;
        }
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
