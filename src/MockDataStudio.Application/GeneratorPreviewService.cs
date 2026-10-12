using System.Globalization;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Application;

/// <summary>Amostras ao vivo de um gerador (S07): mesma função pura usada pelo motor, com semente e linha explícitas.</summary>
public sealed class GeneratorPreviewService(IGeneratorRegistry registry, ITemplateDataSource templates)
{
    /// <param name="spec">Gerador e parâmetros.</param>
    /// <param name="table">Tabela da coluna (só para o escopo do fluxo aleatório).</param>
    /// <param name="column">Coluna alvo (tipo e tamanho influenciam alguns geradores).</param>
    /// <param name="seed">Semente: a mesma semente produz as mesmas amostras.</param>
    /// <param name="count">Quantidade de amostras.</param>
    public async Task<Result<IReadOnlyList<string>>> PreviewAsync(
        GeneratorSpec spec, ObjectName table, ColumnModel column, ulong seed, int count, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(column);

        if (!registry.TryGet(spec.GeneratorId, out var factory))
        {
            return Result<IReadOnlyList<string>>.Fail(ErrorCodes.GenParamType, $"Gerador desconhecido: {spec.GeneratorId}.");
        }

        var datasets = new Dictionary<string, TemplateDataSet>(StringComparer.Ordinal);
        foreach (var code in TemplateCodesOf(spec))
        {
            try
            {
                datasets[code] = await templates.LoadAsync(code, ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                return Result<IReadOnlyList<string>>.Fail(ErrorCodes.GenParamType, $"Template '{code}' não encontrado no catálogo.");
            }
        }

        var reference = DateTimeOffset.UtcNow;
        var created = factory.Create(spec, new GeneratorCreateContext(seed, table, column, datasets, reference, TimeZoneInfo.Utc, registry));
        if (!created.IsSuccess)
        {
            return Result<IReadOnlyList<string>>.Fail(created.Error);
        }

        var generator = created.Value;
        var key = StreamKey.Derive(seed, table.CanonicalId + "." + column.Name, "preview");
        var values = new List<string>(count);
        try
        {
            for (var row = 0L; row < Math.Max(1, count); row++)
            {
                var cell = new GenerationCell(row, DeterministicRandom.For(key, row), new RowContext(row));
                values.Add(Format(generator.Generate(ref cell)));
            }
        }
        catch (InvalidOperationException ex)
        {
            // Gerador que depende de outras colunas da linha não tem preview isolado.
            return Result<IReadOnlyList<string>>.Fail(ErrorCodes.GenParamType, ex.Message);
        }

        return Result<IReadOnlyList<string>>.Ok(values);
    }

    private static IEnumerable<string> TemplateCodesOf(GeneratorSpec spec)
    {
        if (spec.Parameters["template"]?.GetValue<string>() is { } code)
        {
            yield return code;
        }

        foreach (var child in spec.Children ?? [])
        {
            foreach (var childCode in TemplateCodesOf(child))
            {
                yield return childCode;
            }
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "NULL",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
