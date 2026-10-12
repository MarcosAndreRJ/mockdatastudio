using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Mapping.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Mapping.Tests;

// Testes do mapeamento semântico (docs/26 §5.4): T-MAP-001..006 e 008. T-MAP-007 (aprendizado) é da Sprint 17.
public sealed class MappingTests
{
    private static readonly NameNormalizer Normalizer = new();

    // T-MAP-001: normalizador. Os cinco exemplos de docs/14 §2 mais casos adicionais; a tabela completa de 200 casos é pendente.
    [Theory]
    [InlineData("ZAP", null, "ZAP", "ZAP", "ZAP")]
    [InlineData("nro_cep", null, "NROCEP", "NUMCEP", "CEP")]
    [InlineData("dsEmailCliente", "cliente", "DSEMAILCLIENTE", "DESCRICAOEMAILCLIENTE", "EMAIL")]
    [InlineData("DT_NASC", null, "DTNASC", "DATANASCIMENTO", "NASCIMENTO")]
    [InlineData("Telefone Celular 2", null, "TELEFONECELULAR2", "TELEFONECELULAR2", "TELEFONE CELULAR")]
    [InlineData("CPFCliente", "cliente", "CPFCLIENTE", "CPFCLIENTE", "CPF")]
    [InlineData("E_MAIL", null, "EMAIL", "EMAIL", "EMAIL")]
    [InlineData("AÇÃO_CÓDIGO", null, "ACAOCODIGO", "ACAOCODIGO", "ACAO CODIGO")]
    [InlineData("SIGLA_UF", null, "SIGLAUF", "SIGLAUF", "SIGLA UF")]
    [InlineData("dtCadastro", "cliente", "DTCADASTRO", "DATACADASTRO", "CADASTRO")]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-001")]
    public void T_MAP_001_NormalizadorProduzFormasDocumentadas(string input, string? table, string compact, string expandedCompact, string core)
    {
        var result = Normalizer.Normalize(input, table);

        result.Compact.ShouldBe(compact);
        result.ExpandedCompact.ShouldBe(expandedCompact);
        string.Join(" ", result.CoreTokens).ShouldBe(core);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-001")]
    public void T_MAP_001_NumeroOrdinalEhExtraido()
    {
        var result = Normalizer.Normalize("Telefone Celular 2");

        result.Ordinal.ShouldBe(2);
        result.CoreTokens.ShouldNotContain("2");
    }

    // T-MAP-002: binding estrutural (docs/14 §3) não é sobrescrito pela pontuação.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-002")]
    public void T_MAP_002_BindingEstruturalParaGeradaIdentityFkEPk()
    {
        var binder = new StructuralBinder();
        var pedido = MappingFixtures.Table("pedido");
        var id = MappingFixtures.Column("id", "integer", nullable: false, identity: new IdentityInfo(IdentityKind.AutoIncrement));
        var clienteId = MappingFixtures.Column("cliente_id", "integer", nullable: false, ordinal: 2);
        var total = MappingFixtures.Column("total_com_frete", "numeric(14,2)", nullable: true, ordinal: 3,
            generated: new GeneratedInfo("total + 15.00", Stored: false));
        var fk = new ForeignKeyModel("fk_pedido_0", ["cliente_id"], new ObjectName("main", "cliente"), ["id"]);
        var table = TableModel.Create("pedido", [id, clienteId, total], new PrimaryKeyModel(null, ["id"]), [fk]).Value;

        binder.TryBind(null!, pedido, table, id)!.Generator.GeneratorId.ShouldBe("rel.key");
        binder.TryBind(null!, pedido, table, clienteId)!.Generator.GeneratorId.ShouldBe("rel.fk");
        binder.TryBind(null!, pedido, table, total)!.Generator.GeneratorId.ShouldBe("core.skip");
        binder.TryBind(null!, pedido, table, total)!.Origin.ShouldBe(BindingOrigin.Structural);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-002")]
    public void T_MAP_002_PkUuidUsaGeradorUuidEPkCompostaFicaSemanticaNaoEstrutural()
    {
        var binder = new StructuralBinder();
        var sessao = MappingFixtures.Table("sessao");
        var uuid = MappingFixtures.Column("token", "uuid", nullable: false);
        var table = TableModel.Create("sessao", [uuid], new PrimaryKeyModel(null, ["token"])).Value;
        binder.TryBind(null!, sessao, table, uuid)!.Generator.GeneratorId.ShouldBe("core.uuid");

        var a = MappingFixtures.Column("pedido_id", "integer", nullable: false, ordinal: 1);
        var b = MappingFixtures.Column("item_seq", "integer", nullable: false, ordinal: 2);
        var composite = TableModel.Create("item", [a, b], new PrimaryKeyModel(null, ["pedido_id", "item_seq"])).Value;
        binder.TryBind(null!, MappingFixtures.Table("item"), composite, a).ShouldBeNull();
    }

    // T-MAP-003: evidências e pontos. "ZAP" casa o alias exato de TELEFONE (88) + tipo compatível (+5) + caber no comprimento (+3) = 96.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-003")]
    public void T_MAP_003_ZapCasaTelefoneComEvidenciasEscoreEConfiancaHigh()
    {
        var knowledge = MappingFixtures.PackKnowledge();
        var cliente = MappingFixtures.Table("cliente");
        var zap = MappingFixtures.Column("zap", "varchar(20)");
        var schema = MappingFixtures.Schema(("cliente", [zap], null, null));

        var suggestion = new MappingEngine(new StructuralBinder()).SuggestColumn(MappingFixtures.Context(knowledge, schema), cliente, zap);

        var best = suggestion.Best!;
        best.Target.Ref.ShouldBe("TELEFONE");
        best.Confidence.ShouldBe(Confidence.High);
        best.Score.ShouldBe(96);
        best.Evidence.Select(e => e.Kind).ShouldContain(EvidenceKind.ExactAlias);
        best.Evidence.Select(e => e.Kind).ShouldContain(EvidenceKind.TypeCompatible);
        best.Evidence.Select(e => e.Kind).ShouldContain(EvidenceKind.LengthFits);
        suggestion.IsAmbiguous.ShouldBeFalse();
    }

    // T-MAP-004: determinismo. Mesma entrada → mesma saída, independentemente da ordem de carga dos aliases.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-004")]
    public void T_MAP_004_MesmaEntradaProduzMesmaSaidaSempre()
    {
        var engine = new MappingEngine(new StructuralBinder());
        var names = new List<string> { "nome_cliente", "uf", "cep_entrega", "zap", "email_dominio", "sobrenome" };
        var columns = names.Select(n => MappingFixtures.Column(n, "varchar(60)")).ToList();
        var schema = MappingFixtures.Schema(("cliente", columns, null, null));
        var table = MappingFixtures.Table("cliente");

        var first = Describe(engine.SuggestTable(MappingFixtures.Context(MappingFixtures.PackKnowledge(), schema), table));
        for (var i = 0; i < 20; i++)
        {
            Describe(engine.SuggestTable(MappingFixtures.Context(MappingFixtures.PackKnowledge(), schema), table)).ShouldBe(first);
        }
    }

    // T-MAP-004: desempate. Dois alvos com o mesmo escore: ordem por origem, depois ValueCount, depois código do alvo (ordinal).
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-004")]
    public void T_MAP_004_EmpateEhDesfeitoPorCodigoOrdinal()
    {
        var templates = new[]
        {
            new KnowledgeTemplate("ZETA", false, LogicalType.String, [], 5, 10, 10),
            new KnowledgeTemplate("ALFA", false, LogicalType.String, [], 5, 10, 10),
        };
        var aliases = new[]
        {
            new KnowledgeAlias("ZETA", null, "COR", 1.0, null),
            new KnowledgeAlias("ALFA", null, "COR", 1.0, null),
        };
        var knowledge = new MappingKnowledge(templates, aliases, [], Normalizer);
        var column = MappingFixtures.Column("cor", "varchar(60)");
        var schema = MappingFixtures.Schema(("produto", [column], null, null));

        var suggestion = new MappingEngine(new StructuralBinder()).SuggestColumn(MappingFixtures.Context(knowledge, schema), MappingFixtures.Table("produto"), column);

        suggestion.Candidates.Select(c => c.Target.Ref).ShouldBe(["ALFA", "ZETA"]);
    }

    // T-MAP-005: faixas de confiança e ambiguidade. Dois alvos igualmente fortes → ambíguo e nunca High.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-005")]
    public void T_MAP_005_AlvosEquivalentesSaoAmbiguosENaoAlcancamHigh()
    {
        var templates = new[]
        {
            new KnowledgeTemplate("CIDADE_A", false, LogicalType.String, [], 5, 10, 10),
            new KnowledgeTemplate("CIDADE_B", false, LogicalType.String, [], 5, 10, 10),
        };
        var aliases = new[]
        {
            new KnowledgeAlias("CIDADE_A", null, "LOCAL", 1.0, null),
            new KnowledgeAlias("CIDADE_B", null, "LOCAL", 1.0, null),
        };
        var knowledge = new MappingKnowledge(templates, aliases, [], Normalizer);
        var column = MappingFixtures.Column("local", "varchar(60)");
        var schema = MappingFixtures.Schema(("visita", [column], null, null));

        var suggestion = new MappingEngine(new StructuralBinder()).SuggestColumn(MappingFixtures.Context(knowledge, schema), MappingFixtures.Table("visita"), column);

        suggestion.IsAmbiguous.ShouldBeTrue();
        suggestion.Candidates.All(c => c.Confidence != Confidence.High).ShouldBeTrue();
    }

    // T-MAP-006: slots. cep_entrega e cidade_entrega (campos de ENDERECO) compartilham o resíduo ENTREGA → mesmo slot.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-006")]
    public void T_MAP_006_CamposComMesmoResiduoFormamSlot()
    {
        var knowledge = MappingFixtures.PackKnowledge();
        var engine = new MappingEngine(new StructuralBinder());
        var cep = MappingFixtures.Column("cep_entrega", "char(8)", ordinal: 1);
        var cidade = MappingFixtures.Column("cidade_entrega", "varchar(60)", ordinal: 2);
        var avulso = MappingFixtures.Column("cep", "char(8)", ordinal: 3);
        var schema = MappingFixtures.Schema(("pedido", [cep, cidade, avulso], null, null));
        var ctx = MappingFixtures.Context(knowledge, schema);
        var table = MappingFixtures.Table("pedido");

        var suggestions = new[] { cep, cidade, avulso }.Select(c => (c, engine.SuggestColumn(ctx, table, c))).ToList();
        var slots = MappingSlots.Assign(knowledge, "pedido", suggestions);

        suggestions[0].Item2.Best!.Target.FieldCode.ShouldBe("cep");
        suggestions[1].Item2.Best!.Target.FieldCode.ShouldBe("cidade");
        slots["cep_entrega"].ShouldBe("ENTREGA");
        slots["cidade_entrega"].ShouldBe("ENTREGA");
        slots.ContainsKey("cep").ShouldBeFalse();
    }

    // T-MAP-008: fallback por tipo (docs/14 §7): Low para tipos sem candidato; Medium para booleano; High para default de tempo.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-008")]
    public void T_MAP_008_FallbackPorTipoERespeitaConfiancaDocumentada()
    {
        var knowledge = MappingFixtures.PackKnowledge();
        var engine = new MappingEngine(new StructuralBinder());
        var inteiro = MappingFixtures.Column("saldo_anterior", "integer", ordinal: 1);
        var flag = MappingFixtures.Column("ativo_x", "boolean", ordinal: 2);
        var xml = MappingFixtures.Column("carga_xml", "xml", ordinal: 3);
        var criado = MappingFixtures.Column("criado_registro", "datetime", nullable: false, ordinal: 4,
            defaultValue: new DefaultValue("CURRENT_TIMESTAMP", DefaultKind.Function));
        var schema = MappingFixtures.Schema(("log", [inteiro, flag, xml, criado], null, null));
        var ctx = MappingFixtures.Context(knowledge, schema);
        var table = MappingFixtures.Table("log");

        var integerBest = engine.SuggestColumn(ctx, table, inteiro).Best!;
        integerBest.Target.Spec.GeneratorId.ShouldBe("core.random.int");
        integerBest.Confidence.ShouldBe(Confidence.Low);

        var boolBest = engine.SuggestColumn(ctx, table, flag).Best!;
        boolBest.Target.Spec.GeneratorId.ShouldBe("core.bool");
        boolBest.Confidence.ShouldBe(Confidence.Medium);

        engine.SuggestColumn(ctx, table, xml).Best.ShouldBeNull();

        var timeBest = engine.SuggestColumn(ctx, table, criado).Best!;
        timeBest.Target.Spec.GeneratorId.ShouldBe("core.date.relative");
        timeBest.Confidence.ShouldBe(Confidence.High);
    }

    // Auto-aplicação: só High não ambíguo (docs/14 §7); usuário nunca é sobrescrito.
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-MAP-005")]
    public void T_MAP_005_AutoBindAplicaSoAltaConfiancaEPreservaUsuario()
    {
        var knowledge = MappingFixtures.PackKnowledge();
        var engine = new MappingEngine(new StructuralBinder());
        var zap = MappingFixtures.Column("zap", "varchar(20)", ordinal: 1);
        var uf = MappingFixtures.Column("uf", "varchar(2)", ordinal: 2);
        var schema = MappingFixtures.Schema(("cliente", [zap, uf], null, null));
        var manual = new ColumnBinding(new ColumnRef(MappingFixtures.Table("cliente"), "uf"),
            new GeneratorSpec("core.fixed", 1, new System.Text.Json.Nodes.JsonObject { ["value"] = "SP" }),
            0, BindingOrigin.User, "fp");
        var bindings = new Dictionary<string, ColumnBinding> { [manual.Column.CanonicalId] = manual };

        var applied = engine.AutoBind(MappingFixtures.Context(knowledge, schema, bindings), MappingFixtures.Table("cliente"));

        applied.Select(b => b.Column.Column).ShouldContain("zap");
        applied.Select(b => b.Column.Column).ShouldNotContain("uf");
    }

    private static List<string> Describe(IReadOnlyList<MappingSuggestion> suggestions) =>
        suggestions.Select(s => s.Column.Column + ":" + string.Join(",", s.Candidates.Select(c => c.Target.Ref + "/" + c.Target.FieldCode + "/" + c.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/" + c.Confidence))).ToList();
}
