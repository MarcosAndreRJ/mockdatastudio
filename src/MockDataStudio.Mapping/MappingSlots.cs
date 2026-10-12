using MockDataStudio.Core.Schema;

namespace MockDataStudio.Mapping;

// Slots de templates compostos (docs/14 §7). Colunas que casam campos do mesmo template na mesma tabela e
// deixam o mesmo token residual formam um slot nomeado por esse token (ex.: cep_entrega e cidade_entrega → ENTREGA).
public static class MappingSlots
{
    // Retorna o slot de cada coluna por nome (vazio = slot padrão, sem agrupamento).
    public static IReadOnlyDictionary<string, string> Assign(MappingKnowledge knowledge, string tableName,
        IEnumerable<(ColumnModel Column, MappingSuggestion Suggestion)> columns)
    {
        var residuals = new Dictionary<string, (string Template, string Residual)>(StringComparer.Ordinal);

        foreach (var (column, suggestion) in columns)
        {
            var best = suggestion.Best;
            if (best is null || best.Target.Kind != MappingTargetKind.TemplateField || best.Target.FieldCode is null)
            {
                continue;
            }

            var norm = knowledge.Normalizer.Normalize(column.Name, tableName);
            var fieldTokens = knowledge.Normalizer.Normalize(best.Target.FieldCode).CoreTokens;
            var residual = norm.CoreTokens.Where(t => !fieldTokens.Contains(t, StringComparer.Ordinal)).ToList();
            if (residual.Count == 0)
            {
                continue;
            }

            residuals[column.Name] = (best.Target.Ref, string.Join("_", residual));
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in residuals.GroupBy(r => (r.Value.Template, r.Value.Residual)))
        {
            var slot = group.Count() >= 2 ? group.Key.Residual : string.Empty;
            foreach (var entry in group)
            {
                result[entry.Key] = slot;
            }
        }

        return result;
    }
}
