namespace MockDataStudio.Core.Connections;

/// <summary>
/// Capacidades suportadas pelo provider do SGBD conforme especificado em C02.
/// </summary>
public sealed record ProviderCapabilities(
    bool SupportsNamespaces,
    bool SupportsReturning,
    bool SupportsMultiRowValues,
    bool SupportsDeferrableConstraints,
    bool SupportsDisableFkChecksPerSession,
    bool SupportsIdentityOverride,
    bool SupportsSequences,
    bool SupportsTransactionalDdl,
    int MaxParametersPerCommand,
    int MaxIdentifierLength);
