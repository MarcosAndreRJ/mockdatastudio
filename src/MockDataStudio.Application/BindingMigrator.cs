using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Application;

// Migração de vínculos ao trocar de snapshot (C7a.2, docs/14). Compara o fingerprint estrutural de cada coluna vinculada
// com o novo schema e decide o estado do vínculo:
//   - coluna igual (mesmo fingerprint)                         → mantido (Active)
//   - coluna sumiu, mas há UMA coluna da mesma tabela com o mesmo fingerprint → renomeado (Active)
//   - coluna existe, mas a estrutura mudou                     → revisar (NeedsReview)
//   - coluna e tabela sumiram, ou o candidato a renomeação é ambíguo → órfão (Orphaned)
public enum MigrationOutcome
{
    Kept,
    Renamed,
    NeedsReview,
    Orphaned,
}

public sealed record BindingMigrationEntry(string FromId, string ToId, MigrationOutcome Outcome);

public sealed record BindingMigrationResult(
    IReadOnlyDictionary<string, ColumnBinding> Bindings,
    IReadOnlyList<BindingMigrationEntry> Entries);

public static class BindingMigrator
{
    public static BindingMigrationResult Migrate(
        IReadOnlyDictionary<string, ColumnBinding> bindings, SchemaModel newSchema, IStructureHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(newSchema);
        ArgumentNullException.ThrowIfNull(hasher);

        var result = new Dictionary<string, ColumnBinding>(StringComparer.Ordinal);
        var entries = new List<BindingMigrationEntry>();
        var pending = new List<(string Key, ColumnBinding Binding)>();

        // Primeiro os vínculos que continuam existindo, para que renomeações não reclamem colunas já vinculadas.
        foreach (var (key, binding) in bindings.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            var table = newSchema.FindTable(binding.Column.Table);
            var column = table?.FindColumn(binding.Column.Column);
            if (table is not null && column is not null)
            {
                var fingerprint = hasher.ComputeColumnFingerprint(table, column);
                var outcome = fingerprint == binding.ColumnFingerprint ? MigrationOutcome.Kept : MigrationOutcome.NeedsReview;
                var updated = outcome == MigrationOutcome.Kept
                    ? binding with { State = BindingState.Active }
                    : binding with
                    {
                        State = BindingState.NeedsReview,
                        Note = "Estrutura da coluna mudou desde o último snapshot: revisar o vínculo.",
                    };
                Put(result, entries, key, binding.Column.CanonicalId, updated, outcome);
            }
            else
            {
                pending.Add((key, binding));
            }
        }

        // Depois, colunas que sumiram: renomeação só quando há exatamente um candidato com o mesmo fingerprint na tabela.
        foreach (var (key, binding) in pending)
        {
            var table = newSchema.FindTable(binding.Column.Table);
            if (table is null)
            {
                Put(result, entries, key, binding.Column.CanonicalId, Orphan(binding), MigrationOutcome.Orphaned);
                continue;
            }

            var claimed = result.Keys.ToHashSet(StringComparer.Ordinal);
            var candidates = table.Columns
                .Where(c => !claimed.Contains(new ColumnRef(binding.Column.Table, c.Name).CanonicalId))
                .Where(c => hasher.ComputeColumnFingerprint(table, c) == binding.ColumnFingerprint)
                .ToList();

            if (candidates.Count == 1)
            {
                var target = new ColumnRef(binding.Column.Table, candidates[0].Name);
                var renamed = binding with { Column = target, State = BindingState.Active };
                Put(result, entries, key, target.CanonicalId, renamed, MigrationOutcome.Renamed);
            }
            else
            {
                Put(result, entries, key, binding.Column.CanonicalId, Orphan(binding), MigrationOutcome.Orphaned);
            }
        }

        return new BindingMigrationResult(result, entries);
    }

    private static ColumnBinding Orphan(ColumnBinding binding) =>
        binding with
        {
            State = BindingState.Orphaned,
            Note = "Coluna não existe mais no schema capturado.",
        };

    // Nunca sobrescreve um vínculo já colocado no mesmo destino: o segundo vira revisão, sem perder o original.
    private static void Put(
        Dictionary<string, ColumnBinding> result, List<BindingMigrationEntry> entries,
        string fromKey, string toKey, ColumnBinding binding, MigrationOutcome outcome)
    {
        if (result.ContainsKey(toKey))
        {
            var collided = binding with
            {
                State = BindingState.NeedsReview,
                Note = "Dois vínculos apontam para a mesma coluna após a migração: revisar.",
            };
            var reviewKey = fromKey + "#revisar";
            result[reviewKey] = collided;
            entries.Add(new BindingMigrationEntry(fromKey, reviewKey, MigrationOutcome.NeedsReview));
            return;
        }

        result[toKey] = binding;
        entries.Add(new BindingMigrationEntry(fromKey, toKey, outcome));
    }
}
