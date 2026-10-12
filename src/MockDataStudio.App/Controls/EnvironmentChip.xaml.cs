using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MockDataStudio.App.Controls;

// Ambiente do perfil de conexão (cor + rótulo; Production tem cadeado para não depender só de cor).
public partial class EnvironmentChip : UserControl
{
    public static readonly DependencyProperty EnvironmentProperty = DependencyProperty.Register(
        nameof(Environment), typeof(string), typeof(EnvironmentChip), new PropertyMetadata("Unknown", OnEnvironmentChanged));

    public EnvironmentChip()
    {
        InitializeComponent();
    }

    public string Environment
    {
        get => (string)GetValue(EnvironmentProperty);
        set => SetValue(EnvironmentProperty, value);
    }

    private static void OnEnvironmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EnvironmentChip)d).Refresh();

    private void Refresh()
    {
        var token = Environment switch
        {
            "Development" => "Env.Development",
            "Test" or "QA" => "Env.Test",
            "Staging" => "Env.Staging",
            "Production" => "Env.Production",
            _ => "Env.Unknown",
        };

        var color = (Color)FindResource(token);
        Label.Text = Environment;
        Label.Foreground = new SolidColorBrush(color);
        Root.Background = new SolidColorBrush(Color.FromArgb(0x1F, color.R, color.G, color.B));
        LockIcon.Foreground = Label.Foreground;
        LockIcon.Visibility = Environment == "Production" ? Visibility.Visible : Visibility.Collapsed;
    }
}
