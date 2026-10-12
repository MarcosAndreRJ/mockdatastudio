using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Core.Digest;

public interface IDigestSerializer
{
    string FormatVersion { get; }

    string Serialize(SchemaModel model, DigestWriteOptions options);

    // Valida e converte o digest em SchemaModel. Falha com o primeiro erro DIG-* encontrado.
    Result<SchemaModel> Deserialize(string json);
}

public sealed record DigestWriteOptions(bool Indented = false, bool IncludeComments = false);

public interface IDigestValidator
{
    // Lista vazia = válido. Avisos (Severity = Warning) não invalidam o digest.
    IReadOnlyList<DigestValidationError> Validate(string json);
}

public enum DigestSeverity
{
    Warning,
    Error,
}

// Contrato C01 com a severidade acrescentada (12 §7 distingue erros de avisos como DIG-FK-EXTERNAL).
public sealed record DigestValidationError(
    string JsonPath, string Code, string Message, DigestSeverity Severity = DigestSeverity.Error);
