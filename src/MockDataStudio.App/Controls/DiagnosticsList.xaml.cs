using System.Windows;
using System.Windows.Controls;

namespace MockDataStudio.App.Controls;

// Lista de diagnósticos agrupada por severidade (Erro, Aviso, Info, Sucesso).
public partial class DiagnosticsList : UserControl
{
    public static readonly DependencyProperty DiagnosticItemsProperty = DependencyProperty.Register(
        nameof(DiagnosticItems), typeof(IEnumerable<DiagnosticItem>), typeof(DiagnosticsList),
        new PropertyMetadata(Array.Empty<DiagnosticItem>(), OnItemsChanged));

    public DiagnosticsList()
    {
        InitializeComponent();
    }

    public IEnumerable<DiagnosticItem> DiagnosticItems
    {
        get => (IEnumerable<DiagnosticItem>)GetValue(DiagnosticItemsProperty);
        set => SetValue(DiagnosticItemsProperty, value);
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var list = (DiagnosticsList)d;
        var items = (IEnumerable<DiagnosticItem>?)e.NewValue ?? Array.Empty<DiagnosticItem>();

        list.Groups.ItemsSource = items
            .GroupBy(item => item.Severity)
            .OrderBy(group => group.Key)
            .Select(group => new DiagnosticGroup(group.Key, group.ToList()))
            .ToList();
    }

    private sealed record DiagnosticGroup(SeverityLevel Severity, IReadOnlyList<DiagnosticItem> Items);
}
