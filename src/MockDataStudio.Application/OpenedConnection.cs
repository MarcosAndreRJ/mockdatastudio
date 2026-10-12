using System.Data.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Application;

public enum ConnectionPurpose
{
    Explore,
    Query,
    Introspect,
    ReadReferences,
    InspectTarget,
    WriteTarget,
}

public sealed class OpenedConnection : IAsyncDisposable, IDisposable
{
    public OpenedConnection(
        ConnectionProfile profile,
        DbConnection connection,
        IDatabaseProvider provider,
        ServerInfo server,
        bool isServerReadOnly)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Server = server ?? throw new ArgumentNullException(nameof(server));
        IsServerReadOnly = isServerReadOnly;
    }

    public ConnectionProfile Profile { get; }

    public DbConnection Connection { get; }

    public IDatabaseProvider Provider { get; }

    public ServerInfo Server { get; }

    /// <summary>
    /// Indica se a sessão foi efetivamente configurada como somente leitura no SGBD.
    /// Distingue a preferência salva no perfil da restrição de fato aplicada pelo servidor (Prompt §11, docs/23 §5).
    /// </summary>
    public bool IsServerReadOnly { get; }

    public void Dispose()
    {
        Connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
    }
}
