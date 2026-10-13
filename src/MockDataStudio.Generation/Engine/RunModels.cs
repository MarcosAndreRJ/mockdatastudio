namespace MockDataStudio.Generation.Engine;

/// <summary>Resultado final de uma execução (C06). Informado ao writer em <c>CompleteAsync</c>.</summary>
public enum RunStatus
{
    Completed,
    Failed,
    Cancelled,
}
