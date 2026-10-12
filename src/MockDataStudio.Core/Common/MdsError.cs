namespace MockDataStudio.Core.Common;

/// <summary>
/// Representa um erro de domínio padronizado do Mock Data Studio.
/// </summary>
/// <param name="Code">Código estável do erro (ex.: PRV-CONN-FAILED).</param>
/// <param name="Message">Mensagem explicativa para o usuário.</param>
/// <param name="Details">Detalhes contextuais adicionais estruturados.</param>
public sealed record MdsError(string Code, string Message, IReadOnlyDictionary<string, string>? Details = null)
{
    public static MdsError Create(string code, string message, IReadOnlyDictionary<string, string>? details = null)
        => new(code, message, details);
}
