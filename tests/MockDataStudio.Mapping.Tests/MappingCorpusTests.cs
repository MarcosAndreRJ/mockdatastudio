using System.Globalization;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Mapping;
using MockDataStudio.Mapping.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Mapping.Tests;

// T-MAP-CORPUS (docs/14 §11): cobertura High ≥ 70%, precisão High ≥ 95%, nenhum TypeIncompatible com confiança ≥ Medium.
// O corpus é SINTÉTICO (gerado por tests/fixtures/mapping-corpus/generate-corpus.js a partir do pack CORE_PTBR).
// Mede consistência do pipeline; não substitui o corpus de nomes reais anonimizados exigido pela §11.
public sealed class MappingCorpusTests
{
    private static readonly string CorpusPath = Path.Combine(MappingFixtures.RepoRoot(), "tests", "fixtures", "mapping-corpus", "corpus-core-ptbr.csv");

    // Variantes com prefixo de tipo (14 §2 passo 6): fora da meta revisada (DA-044 / PV-018).
    private static readonly System.Text.RegularExpressions.Regex PrefixedNoise =
        new("^(DS|TX|NM|VL)_", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private sealed record Row(string Table, string Column, string NativeType, string Kind, string Template, string Field);

    [Fact]
    [Trait("Category", "Corpus")]
    [Trait("Id", "T-MAP-CORPUS")]
    public void T_MAP_CORPUS_AtingeMetasDeCoberturaPrecisaoETipo()
    {
        var rows = ReadCorpus();
        var knowledge = MappingFixtures.PackKnowledge();
        var engine = new MappingEngine(new StructuralBinder());

        var positives = 0;
        var positivesHigh = 0;
        var positivesNoPrefix = 0;
        var positivesHighNoPrefix = 0;
        var highRows = 0;
        var highCorrect = 0;
        var typeViolations = new List<string>();

        foreach (var row in rows)
        {
            var column = MappingFixtures.Column(row.Column, row.NativeType);
            var schema = MappingFixtures.Schema((row.Table, [column], null, null));
            var suggestion = engine.SuggestColumn(MappingFixtures.Context(knowledge, schema), MappingFixtures.Table(row.Table), column);
            var best = suggestion.Best;

            var noPrefix = !PrefixedNoise.IsMatch(row.Column);
            if (row.Kind != "none")
            {
                positives++;
                if (noPrefix)
                {
                    positivesNoPrefix++;
                }
            }

            if (best is { Confidence: Confidence.High })
            {
                highRows++;
                if (Matches(best, row))
                {
                    highCorrect++;
                }

                if (row.Kind != "none")
                {
                    positivesHigh++;
                    if (noPrefix)
                    {
                        positivesHighNoPrefix++;
                    }
                }
            }
            var typeViolation = best is not null && best.Confidence >= Confidence.Medium
                && best.Evidence.Any(e => e.Kind == EvidenceKind.TypeIncompatible);
            if (typeViolation)
            {
                typeViolations.Add($"{row.Table}.{row.Column} → {best!.Target.Ref}");
            }
        }

        var coverage = (double)positivesHigh / positives;
        var coverageNoPrefix = (double)positivesHighNoPrefix / positivesNoPrefix;
        var precision = highRows == 0 ? 0 : (double)highCorrect / highRows;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"T-MAP-CORPUS: linhas={rows.Count} positivas={positives} cobertura High={coverage:P1} " +
            $"High={highRows} precisão High={precision:P1} violações de tipo={typeViolations.Count}"));

        var metricsPath = Path.Combine(MappingFixtures.RepoRoot(), "TestResults", "mapping-corpus-metrics.json");
        Directory.CreateDirectory(Path.GetDirectoryName(metricsPath)!);
        var metricsJson = string.Create(CultureInfo.InvariantCulture,
            $"{{\"linhas\":{rows.Count},\"positivas\":{positives},\"coberturaHigh\":{coverage:0.0000},\"coberturaHighSemPrefixo\":{coverageNoPrefix:0.0000},\"positivasSemPrefixo\":{positivesNoPrefix},\"highTotal\":{highRows},\"precisaoHigh\":{precision:0.0000},\"violacoesTipo\":{typeViolations.Count}}}");
        File.WriteAllText(metricsPath, metricsJson + Environment.NewLine);

        var failures = new List<string>();
        if (coverageNoPrefix < 0.70)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"cobertura High sem prefixo {coverageNoPrefix:P1} abaixo da meta de 70%"));
        }

        if (precision < 0.95)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"precisão High {precision:P1} abaixo da meta de 95%"));
        }

        if (typeViolations.Count > 0)
        {
            failures.Add($"{typeViolations.Count} alvo(s) TypeIncompatible com confiança ≥ Medium");
        }

        // Falha lista todas as metas não atingidas (docs/14 §11), com os valores medidos.
        failures.ShouldBeEmpty(string.Join("; ", failures));
    }

    private static bool Matches(MappingCandidate candidate, Row row)
    {
        if (row.Kind == "none")
        {
            return false;
        }

        // Nome com dois alvos válidos (UF ou ENDERECO.uf, docs/14 §7): qualquer um conta como correto.
        if (row.Kind == "uf_either")
        {
            return candidate.Target.Ref == "UF" || (candidate.Target.Ref == row.Template && candidate.Target.FieldCode == row.Field);
        }

        if (candidate.Target.Ref != row.Template)
        {
            return false;
        }

        return row.Kind != "field" || string.Equals(candidate.Target.FieldCode, row.Field, StringComparison.Ordinal);
    }

    private static List<Row> ReadCorpus()
    {
        var lines = File.ReadAllLines(CorpusPath);
        return lines.Skip(1).Where(l => l.Length > 0).Select(line =>
        {
            var c = line.Split(',');
            return new Row(c[0], c[1], c[2], c[3], c[4], c[5]);
        }).ToList();
    }
}
