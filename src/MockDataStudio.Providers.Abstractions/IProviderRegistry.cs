using MockDataStudio.Core.Common;

namespace MockDataStudio.Providers;

/// <summary>
/// Consulta de providers disponíveis por SGBD (C02). A composição (DI) é feita na raiz da aplicação.
/// </summary>
public interface IProviderRegistry
{
    IReadOnlyList<IDatabaseProvider> All { get; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifier conflicts with keyword",
        Justification = "Nome exigido pelo contrato C02 (IProviderRegistry.Get).")]
    IDatabaseProvider Get(DatabaseEngine engine);
}
