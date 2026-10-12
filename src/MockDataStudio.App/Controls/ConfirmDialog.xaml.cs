using System.Windows;

namespace MockDataStudio.App.Controls;

// Confirmação simples para ações reversíveis ou de baixo risco.
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(Window owner, string title, string message, string confirmText)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    public static bool Ask(Window owner, string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialog(owner, title, message, confirmText);
        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
