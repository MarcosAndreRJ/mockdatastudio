using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Types;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Core.Diff;

// Comparação entre duas versões de schema (12 §8): mudanças com severidade e candidatos de renomeação.
// Candidatos nunca são aplicados automaticamente.
public sealed class SchemaDiffer(IStructureHasher hasher) : ISchemaDiffer
{
    private const double RenameThreshold = 0.85;

    public SchemaDiff Compare(SchemaModel before, SchemaModel after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<SchemaChange>();
        var fromNs = before.Namespaces.ToDictionary(n => n.Name, StringComparer.Ordinal);
        var toNs = after.Namespaces.ToDictionary(n => n.Name, StringComparer.Ordinal);

        foreach (var name in Union(fromNs.Keys, toNs.Keys))
        {
            if (!toNs.TryGetValue(name, out var target))
            {
                changes.Add(new SchemaChange(ChangeKind.Removed, name, name, null, ChangeSeverity.Breaking, "Namespace removido"));
                continue;
            }

            if (!fromNs.TryGetValue(name, out var source))
            {
                changes.Add(new SchemaChange(ChangeKind.Added, name, null, name, ChangeSeverity.Compatible, "Namespace adicionado"));
                continue;
            }

            CompareNamespace(source, target, changes);
        }

        return new SchemaDiff(hasher.Compute(before), hasher.Compute(after), changes);
    }

    private static void CompareNamespace(NamespaceModel from, NamespaceModel to, List<SchemaChange> changes)
    {
        var fromTables = from.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var toTables = to.Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var removedTables = new List<TableModel>();
        var addedTables = new List<TableModel>();

        foreach (var name in Union(fromTables.Keys, toTables.Keys))
        {
            var path = new ObjectName(from.Name, name).CanonicalId;

            if (!toTables.TryGetValue(name, out var target))
            {
                removedTables.Add(fromTables[name]);
                changes.Add(new SchemaChange(ChangeKind.Removed, path, path, null, ChangeSeverity.Breaking, "Tabela removida"));
                continue;
            }

            if (!fromTables.TryGetValue(name, out var source))
            {
                addedTables.Add(toTables[name]);
                changes.Add(new SchemaChange(ChangeKind.Added, path, null, path, ChangeSeverity.Compatible, "Nova tabela"));
                continue;
            }

            CompareTable(from.Name, source, target, changes);
        }

        AddRenameCandidates(removedTables.Select(t => (t.Name, (object)t)), addedTables.Select(t => (t.Name, (object)t)),
            from.Name, changes, "tabela");

        CompareNamedObjects(from.Name, from.Views.Select(v => (v.Name, (object)v)), to.Views.Select(v => (v.Name, (object)v)),
            changes, "view", ChangeSeverity.Info, ChangeSeverity.Info);

        CompareNamedObjects(from.Name, from.Sequences.Select(s => (s.Name, (object)s)), to.Sequences.Select(s => (s.Name, (object)s)),
            changes, "sequence", ChangeSeverity.Info, ChangeSeverity.Info);

        CompareNamedObjects(from.Name, from.Routines.Select(r => (r.Name, (object)r)), to.Routines.Select(r => (r.Name, (object)r)),
            changes, "rotina", ChangeSeverity.Info, ChangeSeverity.Info);

        CompareNamedObjects(from.Name, from.Triggers.Select(t => (t.Name, (object)t)), to.Triggers.Select(t => (t.Name, (object)t)),
            changes, "trigger", ChangeSeverity.Compatible, ChangeSeverity.Info);
    }

    private static void CompareTable(string ns, TableModel from, TableModel to, List<SchemaChange> changes)
    {
        var tablePath = new ObjectName(ns, from.Name).CanonicalId;

        var fromCols = from.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var toCols = to.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var removed = new List<(string Name, object Column)>();
        var added = new List<(string Name, object Column)>();

        foreach (var name in Union(fromCols.Keys, toCols.Keys))
        {
            var colPath = new ColumnRef(new ObjectName(ns, from.Name), name).CanonicalId;

            if (!toCols.TryGetValue(name, out var target))
            {
                removed.Add((name, fromCols[name]));
                changes.Add(new SchemaChange(ChangeKind.Removed, colPath, colPath, null, ChangeSeverity.Breaking, "Coluna removida"));
                continue;
            }

            if (!fromCols.TryGetValue(name, out var source))
            {
                added.Add((name, toCols[name]));
                var mandatory = !target.IsNullable && target.Default is null && target.Identity is null && target.Generated is null;
                changes.Add(new SchemaChange(ChangeKind.Added, colPath, null, colPath,
                    mandatory ? ChangeSeverity.Breaking : ChangeSeverity.Compatible,
                    mandatory ? "Coluna NOT NULL sem default exige binding" : "Nova coluna"));
                continue;
            }

            CompareColumn(colPath, source, target, changes);
        }

        AddRenameCandidates(removed, added, ns, changes, "coluna", tablePath);

        ComparePrimaryKey(tablePath, from.PrimaryKey, to.PrimaryKey, changes);
        CompareKeyed(tablePath, from.ForeignKeys, to.ForeignKeys, ForeignKeyKey, changes,
            added: ChangeSeverity.Breaking, removed: ChangeSeverity.Info, "FK");
        CompareKeyed(tablePath, from.UniqueConstraints, to.UniqueConstraints, UniqueKey, changes,
            added: ChangeSeverity.Breaking, removed: ChangeSeverity.Info, "UNIQUE");
        CompareKeyed(tablePath, from.Checks, to.Checks, c => Normalize(c.Expression), changes,
            added: ChangeSeverity.Breaking, removed: ChangeSeverity.Info, "CHECK");
        CompareKeyed(tablePath, from.Indexes.Where(i => !i.IsUnique), to.Indexes.Where(i => !i.IsUnique), IndexKey, changes,
            added: ChangeSeverity.Compatible, removed: ChangeSeverity.Info, "índice");
    }

    private static void CompareColumn(string path, ColumnModel from, ColumnModel to, List<SchemaChange> changes)
    {
        var fromType = from.Type;
        var toType = to.Type;

        if (fromType.LogicalType != toType.LogicalType)
        {
            changes.Add(Modified(path, fromType.LogicalType.ToString(), toType.LogicalType.ToString(),
                ChangeSeverity.Breaking, "Mudança de família de tipo lógico"));
        }
        else if (!string.Equals(fromType.NativeName, toType.NativeName, StringComparison.OrdinalIgnoreCase))
        {
            changes.Add(Modified(path, fromType.NativeName, toType.NativeName, ChangeSeverity.Compatible, "Tipo nativo alterado na mesma família"));
        }

        if (IsSmaller(fromType.Length, toType.Length) || IsSmaller(fromType.Precision, toType.Precision))
        {
            changes.Add(Modified(path, Describe(fromType), Describe(toType), ChangeSeverity.Breaking, "Redução de comprimento ou precisão"));
        }
        else if (IsBigger(fromType.Length, toType.Length) || IsBigger(fromType.Precision, toType.Precision))
        {
            changes.Add(Modified(path, Describe(fromType), Describe(toType), ChangeSeverity.Compatible, "Aumento de comprimento ou precisão"));
        }

        if (from.IsNullable && !to.IsNullable)
        {
            changes.Add(Modified(path, "NULL", "NOT NULL", ChangeSeverity.Breaking, "Nullable → NOT NULL"));
        }
        else if (!from.IsNullable && to.IsNullable)
        {
            changes.Add(Modified(path, "NOT NULL", "NULL", ChangeSeverity.Compatible, "NOT NULL → nullable"));
        }

        var fromDefault = from.Default?.RawExpression;
        var toDefault = to.Default?.RawExpression;
        if (!string.Equals(Normalize(fromDefault), Normalize(toDefault), StringComparison.Ordinal))
        {
            changes.Add(Modified(path, fromDefault ?? "(sem default)", toDefault ?? "(sem default)", ChangeSeverity.Info, "Default alterado"));
        }

        if (from.Identity?.Kind != to.Identity?.Kind)
        {
            changes.Add(Modified(path, from.Identity?.Kind.ToString() ?? "(nenhuma)", to.Identity?.Kind.ToString() ?? "(nenhuma)",
                ChangeSeverity.Info, "Identidade alterada"));
        }

        if (!string.Equals(Normalize(from.Generated?.Expression), Normalize(to.Generated?.Expression), StringComparison.Ordinal))
        {
            changes.Add(Modified(path, from.Generated?.Expression ?? "(não gerada)", to.Generated?.Expression ?? "(não gerada)",
                ChangeSeverity.Info, "Expressão de coluna gerada alterada"));
        }
    }

    private static void ComparePrimaryKey(string tablePath, PrimaryKeyModel? from, PrimaryKeyModel? to, List<SchemaChange> changes)
    {
        var fromCols = from is null ? null : string.Join(',', from.Columns);
        var toCols = to is null ? null : string.Join(',', to.Columns);

        if (fromCols == toCols)
        {
            return;
        }

        changes.Add(Modified(tablePath + " PK", fromCols ?? "(sem PK)", toCols ?? "(sem PK)", ChangeSeverity.Breaking, "Chave primária alterada"));
    }

    // Comparação de constraints/índices por chave de conteúdo (nomes não entram no critério).
    private static void CompareKeyed<T>(
        string tablePath, IEnumerable<T> from, IEnumerable<T> to, Func<T, string> key, List<SchemaChange> changes,
        ChangeSeverity added, ChangeSeverity removed, string label)
    {
        var fromKeys = from.Select(key).ToHashSet(StringComparer.Ordinal);
        var toKeys = to.Select(key).ToHashSet(StringComparer.Ordinal);

        foreach (var k in fromKeys.Except(toKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new SchemaChange(ChangeKind.Removed, tablePath + " " + label, k, null, removed, $"{label} removido"));
        }

        foreach (var k in toKeys.Except(fromKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            changes.Add(new SchemaChange(ChangeKind.Added, tablePath + " " + label, null, k, added, $"Novo {label}"));
        }
    }

    private static void CompareNamedObjects(
        string ns, IEnumerable<(string Name, object Value)> from, IEnumerable<(string Name, object Value)> to,
        List<SchemaChange> changes, string label, ChangeSeverity added, ChangeSeverity removed)
    {
        var fromNames = from.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var toNames = to.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var name in fromNames.Except(toNames, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var path = new ObjectName(ns, name).CanonicalId;
            changes.Add(new SchemaChange(ChangeKind.Removed, path, path, null, removed, $"{Capitalize(label)} removido"));
        }

        foreach (var name in toNames.Except(fromNames, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var path = new ObjectName(ns, name).CanonicalId;
            var severity = label == "trigger" && IsInsertTrigger(to, name) ? ChangeSeverity.Compatible : added;
            var note = severity == ChangeSeverity.Compatible && label == "trigger"
                ? "Trigger de INSERT adicionada: verificar efeito na geração"
                : $"Nova {label}";
            changes.Add(new SchemaChange(ChangeKind.Added, path, null, path, severity, note));
        }
    }

    private static bool IsInsertTrigger(IEnumerable<(string Name, object Value)> items, string name) =>
        items.Any(x => x.Name == name && x.Value is TriggerModel t && t.Events.HasFlag(TriggerEvents.Insert));

    // Candidatos: pares remoção/adição com nome parecido (Jaro-Winkler ≥ 0,85) e mesma família de tipo
    // (colunas) ou mesma função (tabelas). Apenas informativo.
    private static void AddRenameCandidates(
        IEnumerable<(string Name, object Value)> removed, IEnumerable<(string Name, object Value)> added,
        string ns, List<SchemaChange> changes, string label, string? tablePath = null)
    {
        var usedAdded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (oldName, oldValue) in removed)
        {
            var best = added
                .Where(a => !usedAdded.Contains(a.Name))
                .Select(a => (a.Name, a.Value, Score: JaroWinkler.Similarity(Compact(oldName), Compact(a.Name))))
                .Where(a => a.Score >= RenameThreshold && SameShape(oldValue, a.Value))
                .OrderByDescending(a => a.Score)
                .FirstOrDefault();

            if (best.Name is null)
            {
                continue;
            }

            usedAdded.Add(best.Name);
            var path = tablePath is null
                ? new ObjectName(ns, oldName).CanonicalId
                : new ColumnRef(ObjectName.ParseCanonical(tablePath), oldName).CanonicalId;

            changes.Add(new SchemaChange(ChangeKind.RenamedCandidate, path, oldName, best.Name, ChangeSeverity.Info,
                $"Candidato de renomeação de {label} (não aplicado automaticamente)"));
        }
    }

    private static bool SameShape(object before, object after) => (before, after) switch
    {
        (ColumnModel a, ColumnModel b) => a.Type.LogicalType == b.Type.LogicalType
            || a.Ordinal == b.Ordinal && a.Type.NativeName == b.Type.NativeName,
        (TableModel, TableModel) => true,
        _ => false,
    };

    // ---------- Chaves de comparação ----------

    private static string ForeignKeyKey(ForeignKeyModel fk) =>
        string.Join(',', fk.Columns) + "->" + fk.Referenced.CanonicalId + "(" + string.Join(',', fk.ReferencedColumns) + ")";

    private static string UniqueKey(UniqueConstraintModel uq) => string.Join(',', uq.Columns.OrderBy(c => c, StringComparer.Ordinal));

    private static string IndexKey(IndexModel index) =>
        string.Join(',', index.Columns.Select(c => (c.Column ?? Normalize(c.Expression)) + (c.Descending ? " desc" : "")))
        + (index.Predicate is null ? string.Empty : " where " + Normalize(index.Predicate));

    // ---------- Auxiliares ----------

    private static SchemaChange Modified(string path, string? before, string? after, ChangeSeverity severity, string note) =>
        new(ChangeKind.Modified, path, before, after, severity, note);

    private static bool IsSmaller(int? from, int? to) => from is { } f && to is { } t && t < f;

    private static bool IsBigger(int? from, int? to) => from is { } f && to is { } t && t > f;

    private static string Describe(DataTypeInfo type) => NativeTypeParser.Format(type);

    private static string Normalize(string? expression) => ExpressionNormalizer.Normalize(expression);

    private static string Compact(string name) =>
        new(name.Where(c => c is not ('_' or ' ')).Select(char.ToLowerInvariant).ToArray());

    private static IEnumerable<string> Union(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Concat(b).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
