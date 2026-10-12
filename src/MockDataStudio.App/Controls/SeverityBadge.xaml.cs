using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MockDataStudio.App.Controls;

// Severidade de diagnóstico: ícone + texto + cor (cor nunca é o único indicador).
public partial class SeverityBadge : UserControl
{
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(SeverityLevel), typeof(SeverityBadge), new PropertyMetadata(SeverityLevel.Info, OnSeverityChanged));

    public SeverityBadge()
    {
        InitializeComponent();
    }

    public SeverityLevel Severity
    {
        get => (SeverityLevel)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SeverityBadge)d).Refresh();

    private void Refresh()
    {
        var (token, glyph, text) = Severity switch
        {
            SeverityLevel.Error => ("Sev.Error", "\uE711", "Erro"),
            SeverityLevel.Warning => ("Sev.Warning", "\uE7BA", "Aviso"),
            SeverityLevel.Success => ("Sev.Success", "\uE73E", "Sucesso"),
            _ => ("Sev.Info", "\uE946", "Info"),
        };

        var color = (Color)FindResource(token);
        var foreground = new SolidColorBrush(color);
        Icon.Text = glyph;
        Icon.Foreground = foreground;
        Label.Text = text;
        Label.Foreground = foreground;
        Root.Background = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B));
    }
}
