using System.Windows;
using System.Windows.Controls;

namespace MockDataStudio.App.Controls;

// Campo de busca visual. Não executa pesquisa nesta etapa.
public partial class SearchField : UserControl
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowShortcutProperty = DependencyProperty.Register(
        nameof(ShowShortcut), typeof(bool), typeof(SearchField), new PropertyMetadata(false));

    public SearchField()
    {
        InitializeComponent();
        Input.TextChanged += (_, _) => Hint.Visibility = string.IsNullOrEmpty(Input.Text) ? Visibility.Visible : Visibility.Collapsed;
        Hint.Visibility = Visibility.Visible;
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool ShowShortcut
    {
        get => (bool)GetValue(ShowShortcutProperty);
        set => SetValue(ShowShortcutProperty, value);
    }
}
