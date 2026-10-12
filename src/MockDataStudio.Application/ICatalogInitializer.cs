using MockDataStudio.Core.Common;

namespace MockDataStudio.Application;

/// <summary>Garante que o catálogo local existe, está migrado e contém o pacote embutido (primeiro uso).</summary>
public interface ICatalogInitializer
{
    /// <summary>Idempotente e seguro para chamadas concorrentes: a preparação roda uma única vez.</summary>
    Task<Result<bool>> EnsureReadyAsync(CancellationToken ct);
}
