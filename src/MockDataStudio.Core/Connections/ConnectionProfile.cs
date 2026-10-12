using MockDataStudio.Core.Common;

namespace MockDataStudio.Core.Connections;

/// <summary>
/// Perfil de conexão configurado pelo usuário conforme especificado em C02.
/// </summary>
public sealed record ConnectionProfile(
    Guid Id,
    string Name,
    DatabaseEngine Engine,
    ConnectionSettings Settings,
    ConnectionEnvironment Environment,
    string? ColorHex,
    bool IsReadOnly,
    bool PromptPassword,
    string? Notes);
