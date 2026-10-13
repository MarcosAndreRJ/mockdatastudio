using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Application;

public enum MappingSeverity
{
    Warning,
    Error,
}

// Diagnóstico de mapeamentos por projeto (C8a.4, FR-065, docs/14 §9). Função pura sobre o schema e os bindings.
// Códigos cobertos aqui: MAP-UNMAPPED, MAP-REVIEW, MAP-ORPHAN e MAP-LOWCONF.
// MAP-AMBIGUOUS e MAP-TYPE dependem das sugestões e do tipo mapeado, que não entram nesta análise (pendência registrada).
public sealed record MappingDiagnostic(string Code, MappingSeverity Severity, string ObjectPath, string Message);

public static class MappingDiagnostics
{
    public const string Unmapped = "MAP-UNMAPPED";
    public const string Review = "MAP-REVIEW";
    public const string Orphan = "MAP-ORPHAN";
    public const string LowConfidence = "MAP-LOWCONF";

    public static IReadOnlyList<MappingDiagnostic> Analyze(SchemaModel schema, IReadOnlyDictionary<string, ColumnBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(bindings);
        var found = new List<MappingDiagnostic>();

        foreach (var ns in schema.Namespaces)
        {
            foreach (var table in ns.Tables)
            {
                foreach (var column in table.Columns)
                {
                    var path = new ColumnRef(new ObjectName(ns.Name, table.Name), column.Name).CanonicalId;
                    var hasBinding = bindings.ContainsKey(path);
                    // Coluna que o banco preenche sozinha (default, identidade ou gerada) não precisa de binding.
                    var autoFilled = column.Default is not null || column.Identity is not null || column.Generated is not null;
                    if (!column.IsNullable && !autoFilled && !hasBinding)
                    {
                        found.Add(new MappingDiagnostic(Unmapped, MappingSeverity.Error, path,
                            "Coluna NOT NULL sem binding e sem default: bloqueia a geração da tabela."));
                    }
                }
            }
        }

        foreach (var binding in bindings.Values)
        {
            var path = binding.Column.CanonicalId;
            switch (binding.State)
            {
                case BindingState.NeedsReview:
                    found.Add(new MappingDiagnostic(Review, MappingSeverity.Warning, path,
                        "Vínculo precisa de revisão (a estrutura da coluna mudou)."));
                    break;
                case BindingState.Orphaned:
                    found.Add(new MappingDiagnostic(Orphan, MappingSeverity.Warning, path,
                        "Vínculo órfão: a coluna não existe mais no schema."));
                    break;
            }

            if (string.Equals(binding.ConfidenceLabel, "Low", StringComparison.Ordinal))
            {
                found.Add(new MappingDiagnostic(LowConfidence, MappingSeverity.Warning, path,
                    "Vínculo aplicado com confiança baixa (fallback): confirme ou ajuste."));
            }
        }

        return found
            .OrderBy(d => d.Code, StringComparer.Ordinal)
            .ThenBy(d => d.ObjectPath, StringComparer.Ordinal)
            .ToList();
    }
}
