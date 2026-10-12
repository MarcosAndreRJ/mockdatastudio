using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Core.Hashing;

// Hash estrutural v1 (12 §6): SHA-256 da forma canônica do schema.
// Inclui nomes de namespace, tabela, coluna, tipo normalizado, nulabilidade, identidade, geração, default,
// PK, FK, UNIQUE, CHECK, índices, sequences, triggers, rotinas e views.
// Exclui servidor, banco, horário, opções, comentários, ordem das colunas e nomes de constraints/índices.
public sealed class StructureHasher : IStructureHasher
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public string Compute(SchemaModel model)
    {
        var namespaces = model.Namespaces
            .OrderBy(n => n.Name, StringComparer.Ordinal)
            .Select(NamespaceNode);

        var root = Obj(("ns", Sorted(namespaces)));
        return HashOf(root);
    }

    public string ComputeTable(ObjectName name, TableModel table) =>
        HashOf(Obj(("n", Text(name.Namespace)), ("t", TableNode(table))));

    public string ComputeColumnFingerprint(TableModel table, ColumnModel column)
    {
        var inPk = table.PrimaryKey?.Columns.Contains(column.Name, StringComparer.Ordinal) ?? false;
        var inFk = table.ForeignKeys.Any(fk => fk.Columns.Contains(column.Name, StringComparer.Ordinal));
        var type = column.Type;

        var input = string.Join('|',
            type.LogicalType.ToString(),
            NumText(type.Length),
            NumText(type.Precision),
            NumText(type.Scale),
            column.IsNullable ? "0" : "1",
            inPk ? "1" : "0",
            inFk ? "1" : "0");

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static JsonNode NamespaceNode(NamespaceModel ns) => Obj(
        ("n", Text(ns.Name)),
        ("v", Bool(ns.IsVirtual)),
        ("t", Sorted(ns.Tables.Select(TableNamed))),
        ("w", Sorted(ns.Views.Select(ViewNode))),
        ("s", Sorted(ns.Sequences.Select(SequenceNode))),
        ("r", Sorted(ns.Routines.Select(RoutineNode))),
        ("g", Sorted(ns.Triggers.Select(TriggerNode))));

    private static JsonNode TableNamed(TableModel table) => Obj(("n", Text(table.Name)), ("c", TableNode(table)));

    private static JsonObject TableNode(TableModel table)
    {
        var columns = table.Columns
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(ColumnNode);

        var pk = table.PrimaryKey is null
            ? null
            : new JsonArray(table.PrimaryKey.Columns.Select(c => (JsonNode)Text(c)).ToArray());

        return Obj(
            ("col", Sorted(columns)),
            ("pk", pk),
            ("fk", Sorted(table.ForeignKeys.Select(ForeignKeyNode))),
            ("uq", Sorted(table.UniqueConstraints.Select(UniqueNode))),
            ("ix", Sorted(table.Indexes.Where(i => !i.IsUnique).Select(IndexNode))),
            ("ck", Sorted(table.Checks.Select(c => Obj(("e", Text(ExpressionNormalizer.Normalize(c.Expression))))))));
    }

    private static JsonNode ColumnNode(ColumnModel column) => Obj(
        ("n", Text(column.Name)),
        ("t", TypeNode(column.Type)),
        ("nn", Bool(!column.IsNullable)),
        ("id", column.Identity is null ? null : Text(column.Identity.Kind.ToString())),
        ("g", column.Generated is null ? null : Obj(
            ("e", Text(ExpressionNormalizer.Normalize(column.Generated.Expression))),
            ("s", Bool(column.Generated.Stored)))),
        ("d", column.Default is null ? null : Text(ExpressionNormalizer.Normalize(column.Default.RawExpression))));

    private static JsonObject TypeNode(DataTypeInfo type) => Obj(
        ("nat", Text(type.NativeName.ToLowerInvariant())),
        ("len", Num(type.Length)),
        ("pre", Num(type.Precision)),
        ("sc", Num(type.Scale)),
        ("uns", type.IsUnsigned ? Bool(true) : null),
        ("lt", Text(type.LogicalType.ToString())),
        ("lu", type.LengthUnit == LengthUnit.Bytes ? Text("b") : null),
        ("enum", type.EnumValues is null ? null : new JsonArray(type.EnumValues.Select(v => (JsonNode)Text(v)).ToArray())));

    private static JsonNode ForeignKeyNode(ForeignKeyModel fk) => Obj(
        ("c", StringArray(fk.Columns)),
        ("r", Text(fk.Referenced.CanonicalId)),
        ("rc", StringArray(fk.ReferencedColumns)),
        ("od", Text(fk.OnDelete.ToString())),
        ("ou", Text(fk.OnUpdate.ToString())),
        ("df", Text(fk.IsDeferrable ? (fk.InitiallyDeferred ? "deferred" : "immediate") : "none")),
        ("ne", Bool(!fk.IsEnforced)));

    private static JsonNode UniqueNode(UniqueConstraintModel uq) => Obj(
        ("c", StringArray(uq.Columns)),
        ("x", Bool(uq.Source == UniqueSource.UniqueIndex)),
        ("nd", Bool(!uq.NullsDistinct)));

    private static JsonNode IndexNode(IndexModel index) => Obj(
        ("c", new JsonArray(index.Columns.Select(IndexColumnText).Select(t => (JsonNode)Text(t)).ToArray())),
        ("w", index.Predicate is null ? null : Text(ExpressionNormalizer.Normalize(index.Predicate))));

    private static string IndexColumnText(IndexColumn column)
    {
        var basis = column.Column ?? ExpressionNormalizer.Normalize(column.Expression);
        return column.Descending ? basis + " desc" : basis;
    }

    private static JsonNode ViewNode(ViewModel view) => Obj(
        ("n", Text(view.Name)),
        ("c", new JsonArray(view.Columns.OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => (JsonNode)Obj(("n", Text(c.Name)), ("t", TypeNode(c.Type)))).ToArray())),
        ("u", view.IsUpdatable is null ? null : Bool(view.IsUpdatable.Value)));

    private static JsonNode SequenceNode(SequenceModel seq) => Obj(
        ("n", Text(seq.Name)),
        ("s", Num(seq.Start)),
        ("i", Num(seq.Increment)),
        ("mn", Num(seq.MinValue)),
        ("mx", Num(seq.MaxValue)),
        ("cy", Bool(seq.IsCycle)));

    private static JsonNode RoutineNode(RoutineModel routine) => Obj(
        ("n", Text(routine.Name)),
        ("k", Text(routine.Kind.ToString())),
        ("p", Num(routine.ParameterCount)),
        ("rt", routine.ReturnType is null ? null : Text(routine.ReturnType)));

    private static JsonNode TriggerNode(TriggerModel trigger) => Obj(
        ("n", Text(trigger.Name)),
        ("t", trigger.Table is null ? null : Text(trigger.Table.Name)),
        ("tm", Text(trigger.Timing.ToString())),
        ("ev", Text(EventsText(trigger.Events))),
        ("a", Bool(trigger.IsActive)));

    private static string EventsText(TriggerEvents events)
    {
        var sb = new StringBuilder();
        if (events.HasFlag(TriggerEvents.Insert))
        {
            sb.Append('i');
        }

        if (events.HasFlag(TriggerEvents.Update))
        {
            sb.Append('u');
        }

        if (events.HasFlag(TriggerEvents.Delete))
        {
            sb.Append('d');
        }

        return sb.ToString();
    }

    // Objeto com chaves em ordem ordinal; valores nulos são omitidos (forma canônica sem campos vazios).
    private static JsonObject Obj(params (string Key, JsonNode? Value)[] properties)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (value is not null)
            {
                obj.Add(key, value);
            }
        }

        return obj;
    }

    // Lista ordenada pela representação canônica de cada item.
    private static JsonArray Sorted(IEnumerable<JsonNode> items)
    {
        var ordered = items
            .Select(item => (Node: item, Text: JsonSerializer.Serialize(item, CanonicalOptions)))
            .OrderBy(pair => pair.Text, StringComparer.Ordinal)
            .Select(pair => (JsonNode)pair.Node)
            .ToArray();
        return new JsonArray(ordered);
    }

    private static JsonArray StringArray(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode)Text(v)).ToArray());

    private static JsonValue Text(string value) => JsonValue.Create(value.Normalize(NormalizationForm.FormC))!;

    private static JsonValue Bool(bool value) => JsonValue.Create(value)!;

    private static JsonValue? Num(int? value) => value is null ? null : JsonValue.Create(value.Value);

    private static JsonValue? Num(long? value) => value is null ? null : JsonValue.Create(value.Value);

    private static string NumText(int? value) =>
        value is null ? string.Empty : value.Value.ToString(CultureInfo.InvariantCulture);

    private static string HashOf(JsonNode node)
    {
        var json = JsonSerializer.Serialize(node, CanonicalOptions);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
    }
}
