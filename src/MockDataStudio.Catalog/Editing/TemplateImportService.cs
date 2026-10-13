using System.Text.Json;
using MockDataStudio.Catalog.Packs;
using MockDataStudio.Catalog.Staging;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Catalog.Editing;

// Texto para copiar e colar numa IA (S09, "Copiar instruções para IA"): descreve as tabelas de staging e o formato esperado.
public static class TemplateInstructions
{
    public const string ForAi = """
        Gere UM único script SQL para o Mock Data Studio importar templates de dados.
        Regras:
        - Use somente INSERT INTO nas tabelas abaixo. Não use DDL, transações (BEGIN/COMMIT), SELECT nem comentários fora de "--".
        - Escape aspas simples dobrando-as ('') dentro de textos.
        - Cada template tem código em MAIÚSCULAS, dígitos e "_", de 2 a 64 caracteres (ex.: NOME_BR).

        Tabelas:
        - template(code, name, kind, logical_type, locale, description, tags)
            kind: 'scalar' ou 'composite'. logical_type obrigatório para scalar (ex.: 'string', 'integer', 'date').
            locale: 'pt-BR'. tags: array JSON opcional, ex.: '["pessoa","br"]'.
        - template_value(template_code, value, weight)           -- só para scalar
        - template_field(template_code, field_code, logical_type, ordinal, max_length, required)  -- só para composite
        - template_record(template_code, record_key, data, weight)  -- só para composite; data é objeto JSON {campo: valor}
        - template_alias(template_code, field_code, alias, weight) -- field_code NULL = alias do template inteiro

        Responda apenas com o script SQL, sem explicações.
        """;
}

public sealed class TemplateImportService(IStagingImporter importer)
{
    public Task<Result<StagingAnalysis>> AnalyzeScriptAsync(string script, MergeMode mode, CancellationToken ct) =>
        importer.AnalyzeAsync(new StagingImportRequest(script ?? string.Empty, mode, null), null, ct);

    // Pack JSON (formato de pack-core-ptbr.json): convertido em script pelo instalador e analisado como qualquer script.
    public async Task<Result<StagingAnalysis>> AnalyzePackJsonAsync(string json, MergeMode mode, CancellationToken ct)
    {
        string script;
        try
        {
            script = BuiltInPackInstaller.BuildScript(json ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Result<StagingAnalysis>.Fail(ErrorCodes.CatImpCode, "Pack JSON inválido: " + ex.Message);
        }

        return await AnalyzeScriptAsync(script, mode, ct);
    }

    public Task<Result<MergeReport>> MergeAsync(string stagingSessionId, CancellationToken ct) =>
        importer.MergeAsync(stagingSessionId, ct);

    public void Discard(string stagingSessionId) => importer.Discard(stagingSessionId);
}
