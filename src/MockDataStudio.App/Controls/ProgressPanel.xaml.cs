using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MockDataStudio.App.Controls;

// Progresso de operação longa (docs/21 §8): fase, barra, detalhe (linhas/s, ETA) e cancelamento.
public partial class ProgressPanel : UserControl
{
    public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(string), typeof(ProgressPanel), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(ProgressPanel), new PropertyMetadata(0d, OnChanged));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(ProgressPanel), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand), typeof(ICommand), typeof(ProgressPanel));

    public ProgressPanel()
    {
        InitializeComponent();
    }

    public string Phase
    {
        get => (string)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (ProgressPanel)d;
        panel.PhaseText.Text = panel.Phase;
        panel.Bar.Value = panel.Percent;
        panel.DetailText.Text = panel.Detail;
    }
}
