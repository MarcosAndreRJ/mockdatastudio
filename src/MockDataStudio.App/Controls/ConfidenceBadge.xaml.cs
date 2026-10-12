using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MockDataStudio.App.Controls;

public enum ConfidenceLevel
{
    High,
    Medium,
    Low,
    None,
}

// Confiança de um mapeamento: indicador ●●● / ●●○ / ●○○ / ○○○ + texto + cor.
public partial class ConfidenceBadge : UserControl
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(ConfidenceLevel), typeof(ConfidenceBadge), new PropertyMetadata(ConfidenceLevel.None, OnLevelChanged));

    public ConfidenceBadge()
    {
        InitializeComponent();
    }

    public ConfidenceLevel Level
    {
        get => (ConfidenceLevel)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ConfidenceBadge)d).Refresh();

    private void Refresh()
    {
        var (token, dots, text) = Level switch
        {
            ConfidenceLevel.High => ("Conf.High", "●●●", "Alta"),
            ConfidenceLevel.Medium => ("Conf.Medium", "●●○", "Média"),
            ConfidenceLevel.Low => ("Conf.Low", "●○○", "Baixa"),
            _ => ("Conf.None", "○○○", "Nenhuma"),
        };

        var color = (Color)FindResource(token);
        var foreground = new SolidColorBrush(color);
        Dots.Text = dots;
        Dots.Foreground = foreground;
        Label.Text = text;
        Label.Foreground = foreground;
        Root.Background = new SolidColorBrush(Color.FromArgb(0x1A, color.R, color.G, color.B));
    }
}
