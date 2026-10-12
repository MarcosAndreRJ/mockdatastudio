namespace MockDataStudio.Core.Connections;

/// <summary>
/// Configurações puras de conexão com SGBD conforme especificado em C02.
/// </summary>
public sealed record ConnectionSettings(
    string? Host,
    int? Port,
    string Database,
    string? User,
    bool UseSsl = false,
    string? Charset = null,
    int ConnectTimeoutSec = 15,
    int CommandTimeoutSec = 0,
    IReadOnlyDictionary<string, string>? Extra = null);
