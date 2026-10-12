using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Mapping;

// Binding estrutural (docs/14 §3). Precede o semântico: PK identity, FK, coluna gerada e PK uuid/inteira são
// decididos pelo schema e não são sobrescritos pela pontuação.
public sealed class StructuralBinder : IStructuralBinder
{
    public ColumnBinding? TryBind(SchemaModel schema, ObjectName table, TableModel tableModel, ColumnModel column)
    {
        ArgumentNullException.ThrowIfNull(tableModel);
        ArgumentNullException.ThrowIfNull(column);

        var columnRef = new ColumnRef(table, column.Name);
        var fingerprint = Fingerprint(column);
        var primaryKey = tableModel.PrimaryKey?.Columns;
        var singlePk = primaryKey is { Count: 1 } && string.Equals(primaryKey[0], column.Name, StringComparison.Ordinal);

        if (column.Generated is not null)
        {
            return Bind(columnRef, "core.skip", new JsonObject(), fingerprint, "Confirmed");
        }

        if (column.Identity is not null && singlePk)
        {
            return Bind(columnRef, "rel.key", new JsonObject { ["strategy"] = "explicit" }, fingerprint, "Confirmed");
        }

        var fk = tableModel.ForeignKeys.FirstOrDefault(f => f.Columns.Contains(column.Name, StringComparer.Ordinal));
        if (fk is not null)
        {
            return Bind(columnRef, "rel.fk",
                new JsonObject { ["fk"] = fk.Name, ["referenced"] = fk.Referenced.CanonicalId }, fingerprint, "Confirmed");
        }

        if (singlePk && column.Type.LogicalType == LogicalType.Uuid)
        {
            return Bind(columnRef, "core.uuid", new JsonObject { ["version"] = 4, ["deterministic"] = true }, fingerprint, "Confirmed");
        }

        if (singlePk && column.Type.LogicalType == LogicalType.Integer)
        {
            return Bind(columnRef, "rel.key", new JsonObject { ["strategy"] = "explicit", ["sequential"] = true }, fingerprint, "Confirmed");
        }

        // PK composta sem FK nestas colunas: semântico por coluna + unicidade da combinação (14 §3).
        return null;
    }

    // Impressão digital estável da coluna para detectar mudanças de schema (MAP-TYPE, docs/14 §9).
    public static string Fingerprint(ColumnModel column)
    {
        var text = string.Join("|", column.Name, column.Type.NativeName, column.Type.Length?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            column.Type.Precision?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, column.Type.Scale?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            column.IsNullable ? "null" : "notnull", column.Default?.RawExpression ?? string.Empty);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static ColumnBinding Bind(ColumnRef column, string generatorId, JsonObject parameters, string fingerprint, string label) =>
        new(column, new GeneratorSpec(generatorId, 1, parameters), NullRate: 0, Origin: BindingOrigin.Structural,
            ColumnFingerprint: fingerprint, ConfidenceLabel: label);
}
