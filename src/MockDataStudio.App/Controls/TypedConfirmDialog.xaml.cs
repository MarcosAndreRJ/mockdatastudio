using System.Windows;
using System.Windows.Controls;

namespace MockDataStudio.App.Controls;

// Confirmação digitada para operações destrutivas: o botão só habilita quando o texto exato é digitado.
public partial class TypedConfirmDialog : Window
{
    private readonly string _expected;

    private TypedConfirmDialog(Window owner, string title, string message, string expected, string confirmText)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        MessageText.Text = message;
        InstructionText.Text = $"Digite \"{expected}\" para confirmar.";
        ConfirmButton.Content = confirmText;
        _expected = expected;
    }

    public static bool Ask(Window owner, string title, string message, string expected, string confirmText)
    {
        var dialog = new TypedConfirmDialog(owner, title, message, expected, confirmText);
        return dialog.ShowDialog() == true;
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = string.Equals(Input.Text, _expected, StringComparison.Ordinal);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
