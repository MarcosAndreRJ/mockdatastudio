using System.Security.Cryptography;
using System.Text;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Generation.Planning;

// Planejador relacional (C06, 16 §1..§5): grafo, componentes, ciclos, faixas de chave e diagnósticos.
// Escopo desta etapa: tabelas com origem Generated, chaves Explicit inteiras e ChildDriven. Fontes Existing/Fixed/Dataset/Query,
// ParentDriven, 1:1, FKs compostas e sobreposição (16 §6..§8) ainda não são planejadas: ver PLANO (A2) e o relatório da sprint.
public sealed class GenerationPlanner : IGenerationPlanner
{
    // Estimativa grosseira por célula, usada apenas para exibir tamanho aproximado do script.
    private const long EstimatedBytesPerCell = 16;

    public PlanResult Plan(PlanningInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var diagnostics = new List<PlanDiagnostic>();
        var schema = input.Schema;
        var profile = input.Profile;

        var included = new SortedSet<ObjectName>();
        foreach (var (canonicalId, settings) in profile.Tables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!settings.Include)
            {
                continue;
            }

            var name = ObjectName.ParseCanonical(canonicalId);
            if (schema.FindTable(name) is null)
            {
                diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Error, "PLN-TABLE-MISSING",
                    $"A tabela '{canonicalId}' está no perfil mas não existe no schema capturado.", canonicalId,
                    "Recapture o schema ou remova a tabela do perfil."));
                continue;
            }

            included.Add(name);
        }

        var includedSet = included.ToHashSet();
        foreach (var (child, fkName, parent) in DependencyGraphBuilder.FindExcludedParents(schema, includedSet, profile.Tables))
        {
            diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Error, "PLN-FK-PARENT-EXCLUDED",
                $"A FK '{fkName}' de '{child.CanonicalId}' referencia '{parent.CanonicalId}', que não está incluída e tem origem Generated.",
                child.CanonicalId,
                "Inclua a tabela pai no perfil ou use registros existentes (Existing/Fixed/Dataset/Query) para a FK."));
        }

        var graph = DependencyGraphBuilder.Build(schema, includedSet, profile.Tables);
        var steps = new List<PlanStep>();
        var order = 0;
        var tables = new Dictionary<ObjectName, TablePlan>();

        foreach (var component in graph.StronglyConnectedComponents())
        {
            var selfRef = graph.Edges.Where(e => e.IsSelfReference && component.Contains(e.Child)).ToList();
            var cyclic = component.Count > 1;
            var deferredByTable = new Dictionary<ObjectName, List<DeferredFk>>();
            var deferredUpdates = new List<DeferredEdge>();

            if (cyclic)
            {
                var resolution = CycleResolver.Resolve(component, graph.Edges,
                    allowDeferred: input.Target.Capabilities.SupportsDeferrableConstraints,
                    allowDisableChecks: profile.Output.AllowDisableFkChecks && input.Target.Capabilities.SupportsDisableFkChecksPerSession);
                if (!resolution.Breakable)
                {
                    diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Error, "PLN-CYCLE-UNBREAKABLE",
                        $"Ciclo sem aresta adiável: {string.Join(", ", component.Select(c => c.CanonicalId))}.",
                        component[0].CanonicalId,
                        "Torne uma FK anulável ou deferrable no banco, use fonte Existing para uma delas, ou exclua uma tabela do ciclo."));
                }
                else
                {
                    foreach (var deferred in resolution.Deferred)
                    {
                        deferredUpdates.Add(deferred);
                        if (!deferredByTable.TryGetValue(deferred.Edge.Child, out var list))
                        {
                            deferredByTable[deferred.Edge.Child] = list = [];
                        }

                        list.Add(new DeferredFk(deferred.Edge.ForeignKeyName, deferred.Strategy));
                    }

                    if (deferredUpdates.Count > 0)
                    {
                        var names = string.Join(", ", deferredUpdates.Select(d => d.Edge.Child.CanonicalId + "." + d.Edge.ForeignKeyName));
                        diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Info, "PLN-CYCLE-NULLUPDATE",
                            $"Ciclo resolvido adiando: {names}.", component[0].CanonicalId, null));
                    }

                    if (deferredUpdates.Any(d => d.Strategy == CycleBreakStrategy.DisableChecks))
                    {
                        diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Warning, "PLN-FKCHECKS-OFF",
                            "Verificação de FKs desligada por sessão para este ciclo.", component[0].CanonicalId,
                            "Confirme que os valores gerados respeitam as FKs."));
                    }
                }
            }

            foreach (var table in component)
            {
                var tablePlan = BuildTablePlan(input, table, deferredByTable.GetValueOrDefault(table) ?? [], diagnostics);
                tables[table] = tablePlan;
                steps.Add(new InsertTableStep(order++, table));
            }

            foreach (var deferred in deferredUpdates.Where(d => d.Strategy == CycleBreakStrategy.NullThenUpdate))
            {
                steps.Add(new DeferredUpdateStep(order++, deferred.Edge.Child, deferred.Edge.ForeignKeyName));
            }

            if (selfRef.Count > 0)
            {
                diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Info, "PLN-SELF-REF",
                    $"Autorreferência em '{selfRef[0].Child.CanonicalId}' tratada pelo gerador (16 §4).", selfRef[0].Child.CanonicalId, null));
            }
        }

        foreach (var table in tables.Values)
        {
            foreach (var key in table.Keys.Where(k => k.SequenceToResync is not null))
            {
                steps.Add(new ResyncSequenceStep(order++, table.Table, key.Column, key.SequenceToResync));
            }
        }

        foreach (var table in tables.Values.Where(t => t.RowCount == 0))
        {
            diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Info, "PLN-NO-ROWS",
                $"A tabela '{table.Table.CanonicalId}' foi incluída com 0 linhas.", table.Table.CanonicalId, null));
        }

        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return new PlanResult(null, diagnostics);
        }

        var tablesOrdered = tables.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        var estimatedRows = tablesOrdered.Values.Sum(t => t.RowCount);
        var estimatedBytes = tablesOrdered.Values.Sum(t => t.RowCount * t.InsertColumns.Count * EstimatedBytesPerCell);
        var hash = ComputePlanHash(steps, tablesOrdered, diagnostics);
        var plan = new GenerationPlan(steps, tablesOrdered, diagnostics, hash, estimatedRows, estimatedBytes);
        return new PlanResult(plan, diagnostics);
    }

    private static TablePlan BuildTablePlan(PlanningInput input, ObjectName table, List<DeferredFk> deferredFks,
        List<PlanDiagnostic> diagnostics)
    {
        var profile = input.Profile;
        var model = input.Schema.FindTable(table)!;
        var tableId = table.CanonicalId;
        var settings = profile.Tables[tableId];
        var strategy = settings.KeyStrategy ?? profile.DefaultKeyStrategy;
        var offline = input.Target.IsOffline;

        var keys = new List<KeyColumnPlan>();
        var omitted = new HashSet<string>(StringComparer.Ordinal);
        if (model.PrimaryKey is { Columns.Count: 1 } pk)
        {
            var column = model.FindColumn(pk.Columns[0])!;
            var sequence = column.Identity?.SequenceName;
            var needsOverride = column.Identity?.Kind == IdentityKind.IdentityAlways;

            if (strategy == KeyStrategy.DatabaseDefault)
            {
                keys.Add(new KeyColumnPlan(column.Name, KeyStrategy.DatabaseDefault, null, 1, false, null));
                omitted.Add(column.Name);
            }
            else if (strategy == KeyStrategy.Explicit && IsInteger(column.Type))
            {
                var maxKey = input.Target.MaxIntegerKeys.GetValueOrDefault(tableId + "." + column.Name);
                var nextSeq = sequence is null ? null : input.Target.SequenceNext.GetValueOrDefault(sequence);
                var (plan, keyDiagnostics) = KeyPlanner.PlanExplicitRange(tableId, column.Name, column.Type,
                    settings.RowCount, maxKey, nextSeq, settings.KeyStartOverride, profile.KeyOffset, offline,
                    needsOverride && input.Target.Capabilities.SupportsIdentityOverride, sequence);
                keys.Add(plan);
                diagnostics.AddRange(keyDiagnostics);
            }
            else
            {
                keys.Add(new KeyColumnPlan(column.Name, strategy, null, 1, needsOverride, sequence));
            }
        }

        var insertColumns = model.Columns
            .Where(c => c.IsInsertable && !omitted.Contains(c.Name))
            .OrderBy(c => c.Ordinal)
            .Select(c => c.Name)
            .ToList();

        return new TablePlan(
            table,
            settings.RowCount,
            insertColumns,
            EvaluationOrder: insertColumns,
            keys,
            deferredFks,
            settings.Relations ?? new Dictionary<string, RelationSettings>(),
            ParentDrivenRowCount: false);
    }

    private static bool IsInteger(DataTypeInfo type) => type.LogicalType == LogicalType.Integer;

    // SHA-256 sobre a representação textual canônica do plano. Mesmo plano → mesmo hash, independentemente de MaxDOP.
    private static string ComputePlanHash(IEnumerable<PlanStep> steps, IReadOnlyDictionary<ObjectName, TablePlan> tables,
        IReadOnlyList<PlanDiagnostic> diagnostics)
    {
        var sb = new StringBuilder();
        foreach (var step in steps)
        {
            sb.Append(step.GetType().Name).Append('|').Append(step.Order).Append('|');
            sb.Append(step switch
            {
                InsertTableStep s => s.Table.CanonicalId,
                DeferredUpdateStep s => s.Table.CanonicalId + "." + s.ForeignKeyName,
                ResyncSequenceStep s => s.Table.CanonicalId + "." + s.Column + "." + s.Sequence,
                _ => string.Empty,
            }).Append('\n');
        }

        foreach (var (name, table) in tables)
        {
            sb.Append(name.CanonicalId).Append('|').Append(table.RowCount).Append('|');
            sb.Append(string.Join(",", table.InsertColumns)).Append('|');
            foreach (var key in table.Keys)
            {
                sb.Append(key.Column).Append(':').Append(key.Strategy).Append(':').Append(key.Start).Append(':').Append(key.Step).Append(';');
            }

            sb.Append('|');
            foreach (var deferred in table.DeferredFks)
            {
                sb.Append(deferred.ForeignKeyName).Append(':').Append(deferred.Strategy).Append(';');
            }

            sb.Append('\n');
        }

        foreach (var diagnostic in diagnostics.OrderBy(d => d.Code, StringComparer.Ordinal))
        {
            sb.Append(diagnostic.Code).Append('|').Append(diagnostic.ObjectPath).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
