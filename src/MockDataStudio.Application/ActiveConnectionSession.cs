namespace MockDataStudio.Application;

/// <summary>
/// Conexão aberta e compartilhada pelas telas Explorer e SQL Editor. Quem a substitui descarta a anterior.
/// </summary>
public sealed class ActiveConnectionSession : IDisposable
{
    private OpenedConnection? _current;

    public OpenedConnection? Current => _current;

    /// <summary>Serializa o uso da conexão: um DbConnection nao aceita comandos simultaneos.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public event EventHandler? Changed;

    public void Set(OpenedConnection? connection)
    {
        var previous = Interlocked.Exchange(ref _current, connection);
        if (!ReferenceEquals(previous, connection))
        {
            previous?.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => Set(null);
}
