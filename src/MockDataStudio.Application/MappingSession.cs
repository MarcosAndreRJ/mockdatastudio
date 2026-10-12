using MockDataStudio.Application.Explorer;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Mapping;
using MockDataStudio.Projects;

namespace MockDataStudio.Application;

public enum FieldStatus
{
    Mapped,
    Suggested,
    NeedsReview,
    Ambiguous,
    Unmapped,
    Orphaned,
}

/// <summary>Uma coluna na tela de mapeamento: dados do schema, vínculo atual e melhor sugestão.</summary>
public sealed record MappingFieldRow(
    ColumnRef Column, string Name, string NativeType, string Constraints, FieldStatus Status,
    string? Generator, string? SuggestedTemplate, string ConfidenceLabel,
    ColumnBinding? Binding, MappingSuggestion Suggestion);

public sealed record MappingTableRow(ObjectName Table, int Fields, int Mapped, int Pending)
{
    public string Name => Table.Name;

    public bool IsComplete => Pending == 0;
}

/// <summary>
/// Sessão de mapeamento sobre o projeto aberto (docs/14, S07). Mantém o contexto de sugestões e aplica as escolhas pelo
/// <see cref="ProjectWorkspace"/>, de modo que cada ação entra no histórico de desfazer/refazer.
/// </summary>
public sealed class MappingSession(IMappingService mapping, ProjectWorkspace workspace)
{
    private MappingContext? _context;
    private readonly Dictionary<string, IReadOnlyList<MappingSuggestion>> _suggestions = new(StringComparer.Ordinal);

    public bool IsLoaded => _context is not null;

    public MappingContext? Context => _context;

    /// <summary>Monta o contexto (schema do snapshot ativo e conhecimento do catálogo). Chamar de novo após desfazer/refazer.</summary>
    public async Task<Result<bool>> LoadAsync(CancellationToken ct)
    {
        _suggestions.Clear();
        var project = workspace.Current;
        if (project is null)
        {
            _context = null;
            return Result<bool>.Fail(ErrorCodes.PrjSchema, "Abra ou crie um projeto primeiro.");
        }

        var built = await mapping.BuildContextAsync(project, ct);
        if (!built.IsSuccess)
        {
            _context = null;
            return Result<bool>.Fail(built.Error);
        }

        _context = built.Value;
        return Result<bool>.Ok(true);
    }

    public IReadOnlyList<MappingTableRow> Tables()
    {
        var context = _context;
        if (context is null)
        {
            return [];
        }

        var rows = new List<MappingTableRow>();
        foreach (var ns in context.Schema.Namespaces)
        {
            foreach (var table in ns.Tables)
            {
                var name = new ObjectName(ns.Name, table.Name);
                var fields = Fields(name);
                var mapped = fields.Count(f => f.Status == FieldStatus.Mapped);
                rows.Add(new MappingTableRow(name, fields.Count, mapped, fields.Count - mapped));
            }
        }

        return rows;
    }

    public IReadOnlyList<MappingFieldRow> Fields(ObjectName table)
    {
        var context = _context;
        var project = workspace.Current;
        var model = context is null ? null : FindTable(context.Schema, table);
        if (context is null || project is null || model is null)
        {
            return [];
        }

        var inspection = ObjectInspector.Inspect(table, model);
        var suggestions = SuggestionsFor(context, table);

        var rows = new List<MappingFieldRow>(model.Columns.Count);
        foreach (var column in model.Columns.OrderBy(c => c.Ordinal))
        {
            var columnRef = new ColumnRef(table, column.Name);
            var info = inspection.Columns.First(c => c.Name == column.Name);
            var suggestion = suggestions.First(s => s.Column == columnRef);
            project.Bindings.TryGetValue(columnRef.CanonicalId, out var binding);
            rows.Add(BuildRow(columnRef, column, info, binding, suggestion));
        }

        return rows;
    }

    /// <summary>Aplica as sugestões de confiança alta em todas as tabelas (vínculos do usuário são preservados).</summary>
    public void AutoMapAll()
    {
        var context = RequireContext();
        workspace.Update("Mapear automaticamente", project => mapping.AutoBindAll(project, context));
        Refresh();
    }

    /// <summary>Aceita o candidato de índice <paramref name="candidateIndex"/> da sugestão como vínculo do usuário.</summary>
    public async Task AcceptAsync(ColumnRef column, int candidateIndex, bool learn, CancellationToken ct)
    {
        var context = RequireContext();
        var suggestion = SuggestionsFor(context, column.Table).First(s => s.Column == column);
        var candidate = suggestion.Candidates.ElementAtOrDefault(candidateIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(candidateIndex));

        await SetGeneratorAsync(column, candidate.Target.Spec, learn, ct);
    }

    /// <summary>Define o gerador escolhido pelo usuário (editor de gerador). Preserva taxa de nulos, unicidade e nota.</summary>
    public async Task SetGeneratorAsync(ColumnRef column, GeneratorSpec spec, bool learn, CancellationToken ct)
    {
        var context = RequireContext();
        var project = workspace.Current ?? throw new InvalidOperationException("Não há projeto aberto.");
        var table = FindTable(context.Schema, column.Table) ?? throw new InvalidOperationException("Tabela fora do snapshot ativo.");
        var model = table.Columns.First(c => c.Name == column.Column);

        project.Bindings.TryGetValue(column.CanonicalId, out var existing);
        var binding = new ColumnBinding(
            column, spec, existing?.NullRate ?? 0, BindingOrigin.User, StructuralBinder.Fingerprint(model),
            BindingState.Active, "confirmed", existing?.Unique ?? false, existing?.Note);

        var updated = await mapping.SetBindingAsync(project, binding, learn, ct);
        workspace.Update($"Mapear {column.Table.Name}.{column.Column}", _ => updated);
        Refresh();
    }

    /// <summary>Altera taxa de nulos, unicidade e nota de um vínculo existente.</summary>
    public void SetOptions(ColumnRef column, double nullRate, bool unique, string? note)
    {
        if (nullRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nullRate), "A taxa de nulos deve estar entre 0 e 1.");
        }

        workspace.Update($"Opções de {column.Table.Name}.{column.Column}", project =>
        {
            if (!project.Bindings.TryGetValue(column.CanonicalId, out var existing))
            {
                return project;
            }

            var bindings = new Dictionary<string, ColumnBinding>(project.Bindings, StringComparer.Ordinal)
            {
                [column.CanonicalId] = existing with { NullRate = nullRate, Unique = unique, Note = string.IsNullOrWhiteSpace(note) ? null : note },
            };
            return project with { Bindings = bindings };
        });
    }

    /// <summary>Remove o vínculo de uma coluna (a coluna volta a ser sugerida).</summary>
    public void ClearBinding(ColumnRef column)
    {
        workspace.Update($"Limpar {column.Table.Name}.{column.Column}", project =>
        {
            if (!project.Bindings.ContainsKey(column.CanonicalId))
            {
                return project;
            }

            var bindings = new Dictionary<string, ColumnBinding>(project.Bindings, StringComparer.Ordinal);
            bindings.Remove(column.CanonicalId);
            return project with { Bindings = bindings };
        });
        Refresh();
    }

    // Os vínculos mudaram: o contexto passa a refletir o projeto atual e as sugestões são recalculadas sob demanda.
    private void Refresh()
    {
        if (_context is null || workspace.Current is null)
        {
            return;
        }

        _context = _context with { ExistingBindings = workspace.Current.Bindings };
        _suggestions.Clear();
    }

    /// <summary>Chamar após Undo/Redo do workspace para realinhar o contexto.</summary>
    public void SyncWithProject() => Refresh();

    private IReadOnlyList<MappingSuggestion> SuggestionsFor(MappingContext context, ObjectName table)
    {
        if (!_suggestions.TryGetValue(table.CanonicalId, out var list))
        {
            list = mapping.Suggest(context, table);
            _suggestions[table.CanonicalId] = list;
        }

        return list;
    }

    private MappingContext RequireContext() =>
        _context ?? throw new InvalidOperationException("A sessão de mapeamento não foi carregada.");

    private static TableModel? FindTable(SchemaModel schema, ObjectName table) =>
        schema.Namespaces.FirstOrDefault(n => n.Name == table.Namespace)?.Tables.FirstOrDefault(t => t.Name == table.Name);

    private static MappingFieldRow BuildRow(ColumnRef columnRef, ColumnModel column, ColumnInfo info, ColumnBinding? binding, MappingSuggestion suggestion)
    {
        var constraints = string.Join(", ", new[]
        {
            info.IsPrimaryKey ? "PK" : null,
            info.IsForeignKey ? "FK" : null,
            info.IsUnique ? "UQ" : null,
            info.IsIdentity ? "AUTO" : null,
            info.Nullable ? null : "NOT NULL",
        }.Where(s => s is not null));

        var best = suggestion.Best;
        var suggested = best is null ? null : DescribeTarget(best.Target);

        if (binding is not null)
        {
            var status = binding.State switch
            {
                BindingState.Orphaned => FieldStatus.Orphaned,
                BindingState.NeedsReview => FieldStatus.NeedsReview,
                _ => FieldStatus.Mapped,
            };
            return new MappingFieldRow(columnRef, column.Name, info.NativeType, constraints, status,
                binding.Generator.GeneratorId, suggested, binding.ConfidenceLabel ?? "confirmed", binding, suggestion);
        }

        var confident = best is not null && best.Confidence >= Confidence.Medium;
        var rowStatus = best is null || !confident
            ? FieldStatus.Unmapped
            : suggestion.IsAmbiguous ? FieldStatus.Ambiguous : FieldStatus.Suggested;
        return new MappingFieldRow(columnRef, column.Name, info.NativeType, constraints, rowStatus,
            best?.Target.Spec.GeneratorId, suggested, best?.Confidence.ToString() ?? "None", null, suggestion);
    }

    /// <summary>Nome legível do alvo de um candidato: template, campo de template, preset ou gerador.</summary>
    public static string DescribeTarget(MappingTarget target) => target.Kind switch
    {
        MappingTargetKind.Template => target.Ref,
        MappingTargetKind.TemplateField => $"{target.Ref}.{target.FieldCode}",
        _ => target.Ref,
    };
}
