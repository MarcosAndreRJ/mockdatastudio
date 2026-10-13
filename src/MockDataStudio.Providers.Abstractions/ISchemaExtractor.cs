using System.Data.Common;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface ISchemaExtractor
{
    Task<Result<SchemaModel>> ExtractAsync(
        DbConnection connection,
        CaptureOptions options,
        IProgress<ExtractionProgress>? progress,
        CancellationToken ct);

    Task<Result<TableModel>> ExtractTableAsync(
        DbConnection connection,
        ObjectName table,
        CancellationToken ct);

    Task<IReadOnlyList<ObjectName>> ListTablesAsync(
        DbConnection connection,
        CancellationToken ct);

    // Listagem leve para navegação sob demanda (Explorer): somente nomes, sem colunas.
    Task<IReadOnlyList<ObjectName>> ListViewsAsync(
        DbConnection connection,
        CancellationToken ct);

    // Triggers com tabela associada, sem corpo (docs/11 §2).
    Task<IReadOnlyList<TriggerModel>> ListTriggersAsync(
        DbConnection connection,
        CancellationToken ct);

    // Procedures e funções do usuário, sem corpo (Explorer, docs/11 §2). Namespace = schema/database da rotina.
    // Padrão vazio para providers que ainda não listam rotinas.
    Task<IReadOnlyList<(string Namespace, RoutineModel Routine)>> ListRoutinesAsync(
        DbConnection connection,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<(string Namespace, RoutineModel Routine)>>([]);

    // Sequences/generators do usuário (Explorer). Padrão vazio para SGBDs sem sequences (MySQL, SQLite).
    Task<IReadOnlyList<(string Namespace, SequenceModel Sequence)>> ListSequencesAsync(
        DbConnection connection,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<(string Namespace, SequenceModel Sequence)>>([]);
}
