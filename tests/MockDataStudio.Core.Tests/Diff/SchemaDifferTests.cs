using MockDataStudio.Core.Diff;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Diff;

public sealed class SchemaDifferTests
{
    private static readonly SchemaDiffer Differ = new(new StructureHasher());

    // Versão 2 do schema "loja" (exemplo SQLite) com mudanças conhecidas de cada severidade.
    private static SchemaModel LojaV2()
    {
        var v1 = DigestFixtures.LoadExample("sqlite");

        var v2 = DigestFixtures.ReplaceTable(v1, "cliente", t => t with
        {
            Columns = t.Columns
                .Where(c => c.Name != "cidade")
                .Select(c => c.Name == "email" ? c with { IsNullable = false } : c)
                .Append(ColumnNew("segmento", LogicalType.String, nullable: true))
                .Append(ColumnNew("tel_obrigatorio", LogicalType.String, nullable: false))
                .Append(ColumnNew("cidades", LogicalType.String, nullable: true))
                .ToList(),
        });

        v2 = DigestFixtures.ReplaceTable(v2, "produto", t => t with
        {
            Columns = t.Columns.Select(c => c.Name == "preco"
                ? c with { Type = c.Type with { Precision = 10 } }
                : c).ToList(),
            Checks = t.Checks.Append(new CheckConstraintModel("ck_estoque", "estoque >= 0", ["estoque"])).ToList(),
        });

        v2 = v2 with
        {
            Namespaces = v2.Namespaces.Select(ns => ns with
            {
                Tables = ns.Tables.Where(t => t.Name != "entrega").ToList(),
                Triggers = ns.Triggers.Where(t => t.Name != "trg_item_atualiza_total").ToList(),
            }).ToList(),
        };

        return v2;
    }

    private static ColumnModel ColumnNew(string name, LogicalType type, bool nullable) =>
        new(name, 100, new DataTypeInfo("varchar(20)", type, Length: 20), nullable);

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-005")]
    public void T_DIG_005_Diff_ClassificaSeveridadesConformeTabela12Secao8()
    {
        var diff = Differ.Compare(DigestFixtures.LoadExample("sqlite"), LojaV2());

        var actual = diff.Changes
            .Where(c => c.Kind != ChangeKind.RenamedCandidate)
            .Select(c => (c.Kind, c.Path, c.Severity))
            .OrderBy(c => c.Path, StringComparer.Ordinal)
            .ThenBy(c => c.Kind)
            .ToList();

        var expected = new List<(ChangeKind Kind, string Path, ChangeSeverity Severity)>
        {
            (ChangeKind.Added, "main.cliente.cidades", ChangeSeverity.Compatible),
            (ChangeKind.Added, "main.cliente.segmento", ChangeSeverity.Compatible),
            (ChangeKind.Added, "main.cliente.tel_obrigatorio", ChangeSeverity.Breaking),
            (ChangeKind.Removed, "main.cliente.cidade", ChangeSeverity.Breaking),
            (ChangeKind.Modified, "main.cliente.email", ChangeSeverity.Breaking),
            (ChangeKind.Modified, "main.produto.preco", ChangeSeverity.Breaking),
            (ChangeKind.Added, "main.produto CHECK", ChangeSeverity.Breaking),
            (ChangeKind.Removed, "main.entrega", ChangeSeverity.Breaking),
            (ChangeKind.Removed, "main.trg_item_atualiza_total", ChangeSeverity.Info),
        }
        .OrderBy(c => c.Path, StringComparer.Ordinal)
        .ThenBy(c => c.Kind)
        .ToList();

        actual.ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-006")]
    public void T_DIG_006_Diff_PropoeCandidatoDeRenomeacaoSemAplicar()
    {
        var before = DigestFixtures.LoadExample("sqlite");
        var diff = Differ.Compare(before, LojaV2());

        var candidates = diff.Changes.Where(c => c.Kind == ChangeKind.RenamedCandidate).ToList();

        candidates.Count.ShouldBe(1);
        candidates[0].Before.ShouldBe("cidade");
        candidates[0].After.ShouldBe("cidades");
        candidates[0].Severity.ShouldBe(ChangeSeverity.Info);
        candidates[0].Note!.ShouldContain("não aplicado");

        // A comparação não altera nenhum dos modelos de entrada.
        Differ.Compare(before, LojaV2()).Changes.Count.ShouldBe(diff.Changes.Count);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-005")]
    public void Diff_DeModelosIguais_EstaVazio()
    {
        var model = DigestFixtures.LoadExample("sqlite");

        var diff = Differ.Compare(model, model);

        diff.IsEmpty.ShouldBeTrue();
        diff.FromHash.ShouldBe(diff.ToHash);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void JaroWinkler_ComparaSimilaridadeEsperada()
    {
        JaroWinkler.Similarity("martha", "marhta").ShouldBe(0.961, 0.001);
        JaroWinkler.Similarity("abc", "abc").ShouldBe(1.0);
        JaroWinkler.Similarity("abc", "xyz").ShouldBe(0.0);
        JaroWinkler.Similarity("", "").ShouldBe(1.0);
    }
}
