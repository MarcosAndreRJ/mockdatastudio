using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MockDataStudio.App.ViewModels;

namespace MockDataStudio.App.Views;

public partial class SqlEditorView : UserControl
{
    private static readonly IHighlightingDefinition SqlHighlighting = LoadSqlHighlighting();
    private SqlEditorWorkspaceViewModel? _workspace;

    public SqlEditorView()
    {
        InitializeComponent();

        QueryEditor.SyntaxHighlighting = SqlHighlighting;
        QueryEditor.TextArea.SelectionChanged += (_, _) =>
        {
            if (_workspace?.SelectedTab is { } tab)
            {
                tab.SelectedText = QueryEditor.SelectedText;
            }
        };

        // F9 e Ctrl+Enter executam (padrão de IBExpert, DBeaver, SQL Developer); Ctrl+T e Ctrl+W abrem e fecham abas.
        PreviewKeyDown += OnPreviewKeyDown;

        DataContextChanged += (_, _) =>
        {
            if (_workspace is not null)
            {
                _workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            }

            _workspace = DataContext as SqlEditorWorkspaceViewModel;
            if (_workspace is not null)
            {
                _workspace.PropertyChanged += OnWorkspacePropertyChanged;
            }

            ShowSelectedTab();
        };

        Loaded += async (_, _) =>
        {
            ShowSelectedTab();
            if (_workspace is not null)
            {
                await _workspace.RefreshProfilesAsync();
            }
        };
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SqlEditorWorkspaceViewModel.SelectedTab))
        {
            ShowSelectedTab();
        }
    }

    // Cada aba tem o próprio TextDocument: trocar de aba troca o documento (texto e histórico de desfazer).
    private void ShowSelectedTab()
    {
        var tab = _workspace?.SelectedTab;
        QueryEditor.IsEnabled = tab is not null;
        if (tab is not null && !ReferenceEquals(QueryEditor.Document, tab.Document))
        {
            QueryEditor.Document = tab.Document;
            tab.SelectedText = string.Empty;
            if (IsVisible)
            {
                QueryEditor.TextArea.Focus();
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_workspace is null)
        {
            return;
        }

        var tab = _workspace.SelectedTab;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if ((e.Key == Key.F9 && Keyboard.Modifiers == ModifierKeys.None) || (ctrl && e.Key == Key.Enter))
        {
            if (tab?.RunCommand.CanExecute(null) == true)
            {
                tab.RunCommand.Execute(null);
            }

            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.T)
        {
            _workspace.NewTabCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.W)
        {
            _workspace.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.S)
        {
            tab?.SaveScriptCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && tab?.StopCommand.CanExecute(null) == true)
        {
            tab.StopCommand.Execute(null);
            e.Handled = true;
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
