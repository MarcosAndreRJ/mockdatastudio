using MockDataStudio.Core.Common;

namespace MockDataStudio.Providers;

// Registro de providers montado a partir dos módulos (TASK-02.01). Um engine só pode ter um provider.
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<DatabaseEngine, IDatabaseProvider> _byEngine;

    public ProviderRegistry(IEnumerable<IDatabaseProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var list = providers.ToList();
        _byEngine = new Dictionary<DatabaseEngine, IDatabaseProvider>(list.Count);
        foreach (var provider in list)
        {
            if (!_byEngine.TryAdd(provider.Engine, provider))
            {
                throw new ArgumentException($"Já existe um provider registrado para {provider.Engine}.", nameof(providers));
            }
        }

        All = list;
    }

    public IReadOnlyList<IDatabaseProvider> All { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifier conflicts with keyword",
        Justification = "Nome exigido pelo contrato C02 (IProviderRegistry.Get).")]
    public IDatabaseProvider Get(DatabaseEngine engine) =>
        _byEngine.TryGetValue(engine, out var provider)
            ? provider
            : throw new InvalidOperationException($"Nenhum provider registrado para o SGBD '{engine}'.");
}
