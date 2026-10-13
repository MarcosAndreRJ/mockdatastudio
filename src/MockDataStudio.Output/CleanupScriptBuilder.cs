using System.Globalization;
using System.Text;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Planning;
using MockDataStudio.Providers;

namespace MockDataStudio.Output;

/// <summary>
/// Script de limpeza (docs/18 §7): remove somente as linhas de uma execução, em ordem inversa de dependência.
/// Chaves inteiras Explicit usam faixa (<c>BETWEEN a AND b</c>); chaves compostas, UUID e capturadas usam a lista de tuplas
/// em <see cref="KeyRangeResult.CapturedKeys"/>, na ordem das colunas da chave primária (<c>Column</c> traz os nomes separados por vírgula).
/// </summary>
public sealed class CleanupScriptBuilder(SchemaModel schema) : ICleanupScriptBuilder
{

    public IReadOnlyList<string> Build(GenerationPlan plan, IReadOnlyList<TableRunResult> results, ISqlDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(dialect);

        var statements = new List<string>
        {
            "-- Remove somente as linhas desta execução (RunId). Linhas alteradas por triggers ou cascades em outras tabelas não são revertidas.",
        };

        var resultByTable = results.ToDictionary(r => r.Table);
        var order = plan.Steps.OfType<InsertTableStep>().Select(s => s.Table).ToList();

        // 1) FKs de ciclo que foram preenchidas depois (NullThenUpdate) voltam a NULL antes dos DELETEs.
        foreach (var table in order)
        {
            if (!plan.Tables.TryGetValue(table, out var tablePlan) || tablePlan.RowCount == 0)
            {
                continue;
            }

            foreach (var deferred in tablePlan.DeferredFks.Where(d => d.Strategy == CycleBreakStrategy.NullThenUpdate))
            {
                var model = schema.FindTable(table);
                var fk = model?.ForeignKeys.FirstOrDefault(f => f.Name == deferred.ForeignKeyName);
                var predicate = model is null ? null : Predicate(table, model, tablePlan, resultByTable.GetValueOrDefault(table), dialect);
                if (fk is null || predicate is null || predicate.Count == 0)
                {
                    statements.Add($"-- ATENÇÃO: não foi possível zerar a FK {deferred.ForeignKeyName} de {Display(table)}; faça manualmente antes de apagar.");
                    continue;
                }

                var set = string.Join(", ", fk.Columns.Select(c => dialect.QuoteIdentifier(c) + " = NULL"));
                foreach (var where in predicate)
                {
                    statements.Add($"UPDATE {dialect.QualifiedName(table)} SET {set} WHERE {where};");
                }
            }
        }

        // 2) DELETEs em ordem inversa à de inserção: filhos antes dos pais.
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var table = order[i];
            if (!plan.Tables.TryGetValue(table, out var tablePlan) || tablePlan.RowCount == 0)
            {
                continue;
            }

            var model = schema.FindTable(table);
            var predicate = model is null ? null : Predicate(table, model, tablePlan, resultByTable.GetValueOrDefault(table), dialect);
            if (predicate is null || predicate.Count == 0)
            {
                statements.Add($"-- ATENÇÃO: {Display(table)} não tem faixa de chave nem chaves capturadas; limpeza manual necessária.");
                continue;
            }

            foreach (var where in predicate)
            {
                statements.Add($"DELETE FROM {dialect.QualifiedName(table)} WHERE {where};");
            }
        }

        return statements;
    }

    // Condições WHERE que identificam as linhas da execução. Vazio/nulo quando não há como identificá-las.
    private static List<string>? Predicate(ObjectName table, TableModel model, MockDataStudio.Generation.Planning.TablePlan plan,
        TableRunResult? result, ISqlDialect dialect)
    {
        var key = plan.Keys.FirstOrDefault(k => k.Strategy == KeyStrategy.Explicit && k.Start is not null);
        if (key is { Start: { } start } && plan.RowCount > 0)
        {
            var last = start + (plan.RowCount - 1) * key.Step;
            return [$"{dialect.QuoteIdentifier(key.Column)} BETWEEN {start.ToString(CultureInfo.InvariantCulture)} AND {last.ToString(CultureInfo.InvariantCulture)}"];
        }

        var captured = result?.KeyRanges.FirstOrDefault(r => r.CapturedKeys is { Count: > 0 })?.CapturedKeys;
        if (captured is null || model.PrimaryKey is not { Columns.Count: > 0 } pk)
        {
            return null;
        }

        var columns = pk.Columns;
        var types = columns.Select(c => model.FindColumn(c)!.Type).ToList();
        var batchSize = Math.Max(1, dialect.MaxTuplesPerPredicate);
        var predicates = new List<string>();
        for (var offset = 0; offset < captured.Count; offset += batchSize)
        {
            var batch = captured.Skip(offset).Take(batchSize).ToList();
            predicates.Add(TuplePredicate(columns, types, batch, dialect));
        }

        return predicates;
    }

    private static string TuplePredicate(IReadOnlyList<string> columns, List<DataTypeInfo> types,
        IReadOnlyList<object?[]> tuples, ISqlDialect dialect)
    {
        var literals = tuples
            .Select(t => (IReadOnlyList<string>)columns.Select((_, i) => dialect.Literals.Format(t[i], types[i])).ToList())
            .ToList();
        return dialect.BuildTuplePredicate(columns, literals);
    }

    private static string Display(ObjectName table) =>
        string.IsNullOrEmpty(table.Namespace) || table.Namespace == "main" ? table.Name : table.Namespace + "." + table.Name;
}
