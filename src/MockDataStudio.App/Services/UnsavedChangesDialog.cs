using System.Windows;
using System.Windows.Controls;
using MockDataStudio.App.Resources;

namespace MockDataStudio.App.Services;

/// <summary>Diálogo Salvar / Descartar / Continuar editando (TC-104), montado em código para não depender de XAML extra.</summary>
public sealed class UnsavedChangesDialog : Window, IUnsavedChangesPrompt
{
    private LeaveDecision _decision = LeaveDecision.Stay;

    public UnsavedChangesDialog()
    {
        Title = TemplatesText.UnsavedTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
    }

    public Task<LeaveDecision> AskAsync(string description)
    {
        var dialog = new UnsavedChangesDialog { Owner = System.Windows.Application.Current?.MainWindow };
        dialog.Build(description);
        dialog.ShowDialog();
        return Task.FromResult(dialog._decision);
    }

    private void Build(string description)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Choice(TemplatesText.UnsavedSave, LeaveDecision.Save, isDefault: true));
        buttons.Children.Add(Choice(TemplatesText.UnsavedDiscard, LeaveDecision.Discard));
        buttons.Children.Add(Choice(TemplatesText.UnsavedStay, LeaveDecision.Stay, isCancel: true));

        var text = new TextBlock { Text = description + "\n\n" + TemplatesText.UnsavedQuestion, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(text);
        root.Children.Add(buttons);
        Content = root;
    }

    private Button Choice(string label, LeaveDecision decision, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = label, MinWidth = 96, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            _decision = decision;
            DialogResult = true;
        };
        return button;
    }
}
