namespace MockDataStudio.Core.Common;

/// <summary>
/// Abstração para obtenção de data/hora atual determinística ou em tempo real.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Implementação padrão de relógio do sistema.
/// </summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
