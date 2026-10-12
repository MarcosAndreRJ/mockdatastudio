using System.Windows;
using System.Windows.Controls;

namespace MockDataStudio.App.Controls;

// Campo de busca visual. Não executa pesquisa nesta etapa.
public partial class SearchField : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SearchField),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) =>
        {
            var sf = (SearchField)d;
            var newVal = (string)e.NewValue ?? string.Empty;
            if (sf.Input.Text != newVal)
            {
                sf.Input.Text = newVal;
            }
        }));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchField), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowShortcutProperty = DependencyProperty.Register(
        nameof(ShowShortcut), typeof(bool), typeof(SearchField), new PropertyMetadata(false));

    public SearchField()
    {
        InitializeComponent();
        Input.TextChanged += (_, _) =>
        {
            Hint.Visibility = string.IsNullOrEmpty(Input.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (Text != Input.Text)
            {
                Text = Input.Text;
            }
        };
        Hint.Visibility = Visibility.Visible;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
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
