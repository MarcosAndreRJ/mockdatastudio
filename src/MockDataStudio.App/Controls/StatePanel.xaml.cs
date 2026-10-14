using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MockDataStudio.App.ViewModels.Templates;

namespace MockDataStudio.App.Controls;

// Mostra o estado visual de uma lista (carregando, vazio, sem resultados, erro). Só apresentação: sem regra de negócio.
public partial class StatePanel : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ViewState), typeof(StatePanel), new PropertyMetadata(ViewState.Ready, OnStateChanged));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(StatePanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register(
        nameof(RetryCommand), typeof(ICommand), typeof(StatePanel), new PropertyMetadata(null, OnStateChanged));

    public static readonly DependencyProperty RetryTextProperty = DependencyProperty.Register(
        nameof(RetryText), typeof(string), typeof(StatePanel), new PropertyMetadata(string.Empty));

    public StatePanel()
    {
        InitializeComponent();
        Refresh();
    }

    public ViewState State
    {
        get => (ViewState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public string RetryText
    {
        get => (string)GetValue(RetryTextProperty);
        set => SetValue(RetryTextProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((StatePanel)d).Refresh();

    private void Refresh()
    {
        Glyph.Text = State switch
        {
            ViewState.Error => "",
            ViewState.NoResults => "",
            ViewState.Empty => "",
            _ => "",
        };
        Bar.Visibility = State == ViewState.Loading ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = State == ViewState.Error && RetryCommand is not null ? Visibility.Visible : Visibility.Collapsed;
    }
}
