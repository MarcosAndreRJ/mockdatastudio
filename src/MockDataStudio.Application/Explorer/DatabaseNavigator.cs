using System.Data.Common;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers;

namespace MockDataStudio.Application.Explorer;

// Navegação sob demanda sobre o contrato ISchemaExtractor (docs/11 §6). Cada consulta recupera somente o necessário:
// nomes para listagens e um único TableModel para inspeção. O cache vive na sessão e é descartado por Refresh().
public sealed class DatabaseNavigator(IDatabaseProvider provider)
{
    private readonly Dictionary<string, TableModel> _tables = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<string>> ListSchemasAsync(DbConnection connection, CancellationToken ct)
    {
        var tables = await provider.CreateSchemaExtractor().ListTablesAsync(connection, ct);
        var views = await provider.CreateSchemaExtractor().ListViewsAsync(connection, ct);
        return tables.Concat(views).Select(o => o.Namespace).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<ObjectName>> ListTablesAsync(DbConnection connection, string schema, CancellationToken ct) =>
        (await provider.CreateSchemaExtractor().ListTablesAsync(connection, ct))
        .Where(o => string.Equals(o.Namespace, schema, StringComparison.Ordinal))
        .OrderBy(o => o.Name, StringComparer.Ordinal)
        .ToList();

    public async Task<IReadOnlyList<ObjectName>> ListViewsAsync(DbConnection connection, string schema, CancellationToken ct) =>
        (await provider.CreateSchemaExtractor().ListViewsAsync(connection, ct))
        .Where(o => string.Equals(o.Namespace, schema, StringComparison.Ordinal))
        .OrderBy(o => o.Name, StringComparer.Ordinal)
        .ToList();

    public async Task<IReadOnlyList<TriggerModel>> ListTriggersAsync(DbConnection connection, string schema, CancellationToken ct) =>
        (await provider.CreateSchemaExtractor().ListTriggersAsync(connection, ct))
        .Where(t => t.Table is { } table && string.Equals(table.Namespace, schema, StringComparison.Ordinal))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToList();

    // Árvore do Explorer numa passada: cada tipo de objeto é listado uma única vez e agrupado por schema em memória
    // (listar por schema repetia a consulta inteira para cada um). Triggers, rotinas e sequences são opcionais: uma
    // falha nelas (permissão, versão do servidor) não esconde tabelas e views.
    public async Task<IReadOnlyList<SchemaObjects>> LoadCatalogAsync(DbConnection connection, CancellationToken ct)
    {
        var extractor = provider.CreateSchemaExtractor();
        var tables = await extractor.ListTablesAsync(connection, ct);
        var views = await extractor.ListViewsAsync(connection, ct);
        var triggers = await OptionalAsync(() => extractor.ListTriggersAsync(connection, ct));
        var routines = await OptionalAsync(() => extractor.ListRoutinesAsync(connection, ct));
        var sequences = await OptionalAsync(() => extractor.ListSequencesAsync(connection, ct));

        var schemas = tables.Select(t => t.Namespace)
            .Concat(views.Select(v => v.Namespace))
            .Concat(triggers.Where(t => t.Table is not null).Select(t => t.Table!.Namespace))
            .Concat(routines.Select(r => r.Namespace))
            .Concat(sequences.Select(s => s.Namespace))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal);

        return schemas.Select(schema => new SchemaObjects(
                schema,
                tables.Where(t => t.Namespace == schema).OrderBy(t => t.Name, StringComparer.Ordinal).ToList(),
                views.Where(v => v.Namespace == schema).OrderBy(v => v.Name, StringComparer.Ordinal).ToList(),
                triggers.Where(t => t.Table?.Namespace == schema).OrderBy(t => t.Name, StringComparer.Ordinal).ToList(),
                routines.Where(r => r.Namespace == schema).Select(r => r.Routine).OrderBy(r => r.Name, StringComparer.Ordinal).ToList(),
                sequences.Where(s => s.Namespace == schema).Select(s => s.Sequence).OrderBy(s => s.Name, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    private static async Task<IReadOnlyList<T>> OptionalAsync<T>(Func<Task<IReadOnlyList<T>>> list)
    {
        try
        {
            return await list();
        }
        catch (DbException)
        {
            return [];
        }
        catch (NotImplementedException)
        {
            return [];
        }
    }

    // Metadados de um objeto. Resultado em cache até Refresh().
    public async Task<Result<TableModel>> DescribeTableAsync(DbConnection connection, ObjectName table, CancellationToken ct)
    {
        if (_tables.TryGetValue(table.CanonicalId, out var cached))
        {
            return Result<TableModel>.Ok(cached);
        }

        var result = await provider.CreateSchemaExtractor().ExtractTableAsync(connection, table, ct);
        if (result.IsSuccess)
        {
            _tables[table.CanonicalId] = result.Value;
        }

        return result;
    }

    // Descarta o cache de metadados (atualização manual do usuário, ação "Atualizar").
    public void Refresh() => _tables.Clear();
}

// Objetos de um schema para a árvore do Explorer (nomes e metadados leves, sem colunas).
public sealed record SchemaObjects(
    string Schema,
    IReadOnlyList<ObjectName> Tables,
    IReadOnlyList<ObjectName> Views,
    IReadOnlyList<TriggerModel> Triggers,
    IReadOnlyList<RoutineModel> Routines,
    IReadOnlyList<SequenceModel> Sequences);
