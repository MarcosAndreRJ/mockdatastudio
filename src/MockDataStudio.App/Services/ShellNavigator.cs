namespace MockDataStudio.App.Services;

/// <summary>Permite que uma tela peça ao shell para navegar a outra, sem conhecer o ShellViewModel.</summary>
public sealed class ShellNavigator
{
    public event Action<string>? Requested;

    public void NavigateTo(string key) => Requested?.Invoke(key);
}
