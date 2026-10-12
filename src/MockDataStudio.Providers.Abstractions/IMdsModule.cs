using Microsoft.Extensions.DependencyInjection;

namespace MockDataStudio.Providers;

/// <summary>
/// Versão atual dos contratos de extensão do Mock Data Studio.
/// </summary>
public static class MdsContracts
{
    public const int Version = 1;
}

/// <summary>
/// Contrato de módulo de extensão/registro no container de injeção de dependência (docs/31 §2).
/// </summary>
public interface IMdsModule
{
    string Id { get; }
    int ContractVersion { get; }
    void Register(IServiceCollection services);
}
