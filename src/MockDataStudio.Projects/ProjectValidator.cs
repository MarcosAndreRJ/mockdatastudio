using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;

namespace MockDataStudio.Projects;

public sealed record ProjectIssue(string Code, string Message, string JsonPath, bool IsError);

/// <summary>
/// Verificações semânticas de um projeto já lido (docs/22 §5). A validação estrutural (PRJ-JSON, PRJ-SCHEMA, PRJ-DIGEST)
/// é feita pelo <see cref="ProjectSerializer"/> ao abrir; aqui ficam os avisos que não impedem a abertura.
/// </summary>
public static class ProjectValidator
{
    /// <param name="project">Projeto aberto.</param>
    /// <param name="catalogHashes">Template do catálogo local → hash de conteúdo atual. Nulo = catálogo não consultado.</param>
    public static IReadOnlyList<ProjectIssue> Validate(MdsProject project, IReadOnlyDictionary<string, string>? catalogHashes = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<ProjectIssue>();

        // PRJ-BINDING-COLUMN: binding aponta para coluna inexistente no snapshot ativo.
        var active = project.Snapshots.FirstOrDefault(s => s.Id == project.ActiveSnapshotId);
        if (active is not null)
        {
            var columns = ColumnsOf(active.DigestJson);
            foreach (var key in project.Bindings.Keys.Where(k => !columns.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
            {
                issues.Add(new ProjectIssue(ErrorCodes.PrjBindingColumn,
                    $"O vínculo de \"{key}\" referencia uma coluna que não existe no snapshot ativo.", $"$.bindings.{key}", false));
            }
        }

        // PRJ-CATALOG: dependência ausente ou alterada no catálogo local.
        if (catalogHashes is not null)
        {
            for (var i = 0; i < project.CatalogDependencies.Count; i++)
            {
                var dependency = project.CatalogDependencies[i];
                if (!catalogHashes.TryGetValue(dependency.TemplateCode, out var hash))
                {
                    issues.Add(new ProjectIssue(ErrorCodes.PrjCatalog,
                        $"O template \"{dependency.TemplateCode}\" não existe no catálogo. Importe o pack que o contém.",
                        $"$.catalogDependencies[{i}]", false));
                }
                else if (!string.Equals(hash, dependency.ContentHash, StringComparison.Ordinal))
                {
                    issues.Add(new ProjectIssue(ErrorCodes.PrjCatalog,
                        $"O conteúdo do template \"{dependency.TemplateCode}\" mudou desde a criação do projeto: a geração pode não ser reprodutível.",
                        $"$.catalogDependencies[{i}]", false));
                }
            }
        }

        return issues;
    }

    private static HashSet<string> ColumnsOf(string digestJson)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (JsonNode.Parse(digestJson)?["namespaces"] is not JsonArray namespaces)
        {
            return result;
        }

        foreach (var ns in namespaces.OfType<JsonObject>())
        {
            var nsName = ns["name"]?.GetValue<string>() ?? string.Empty;
            foreach (var table in (ns["tables"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                var tableName = table["name"]?.GetValue<string>() ?? string.Empty;
                foreach (var col in (table["cols"] as JsonArray)?.OfType<JsonArray>() ?? [])
                {
                    if (col.Count > 0 && col[0]?.GetValue<string>() is { } columnName)
                    {
                        result.Add(new ColumnRef(new ObjectName(nsName, tableName), columnName).CanonicalId);
                    }
                }
            }
        }

        return result;
    }
}
