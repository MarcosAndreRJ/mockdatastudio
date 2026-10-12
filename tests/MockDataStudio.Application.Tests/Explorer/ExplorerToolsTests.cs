using System.Text;
using MockDataStudio.Application.Explorer;
using MockDataStudio.Application.Tests.TestSupport;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Naming;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// C5.4 / C6 (TASK-12.02, 12.03, 13.01..13.04): filtro, referências, explain, formatação, exportação e script.
public sealed class ExplorerToolsTests
{
    [Theory]
    [InlineData("cliente", null, true)]
    [InlineData("cliente", "", true)]
    [InlineData("cliente", "CLI", true)]
    [InlineData("cliente", "pedido", false)]
    public void Filtro_CasaTextoSemDistinguirMaiusculas(string candidate, string? query, bool expected)
    {
        ExplorerFilter.Matches(candidate, query).ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Filtro_ListaObjetosPorNomeOuSchema()
    {
        var objects = new[] { new ObjectName("public", "cliente"), new ObjectName("public", "pedido"), new ObjectName("audit", "log") };

        ExplorerFilter.Filter(objects, "audit").ShouldBe([new ObjectName("audit", "log")]);
        ExplorerFilter.Filter(objects, "ped").ShouldBe([new ObjectName("public", "pedido")]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Formatador_PalavrasChaveEmMaiusculasEClausulasEmNovaLinha()
    {
        var formatted = SqlFormatter.Format("select id, nome from cliente where ativo = 1 order by nome");

        formatted.ShouldContain("SELECT id,");
        formatted.ShouldContain("\nFROM cliente");
        formatted.ShouldContain("\nWHERE ativo = 1");
        formatted.ShouldContain("\nORDER BY nome");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Formatador_PreservaLiteraisComAspaDobrada()
    {
        var formatted = SqlFormatter.Format("select 'it''s from here' as x from t");

        formatted.ShouldContain("'it''s from here'");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Formatador_EhIdempotente()
    {
        const string input = "select a.id, count(*) from cliente a left join pedido b on b.cliente_id = a.id and b.total > 0 where a.ativo = 1 group by a.id having count(*) > 1";

        var once = SqlFormatter.Format(input);
        var twice = SqlFormatter.Format(once);

        twice.ShouldBe(once);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Formatador_NaoAlteraOsTokensSemContarCaixaEEspacos()
    {
        const string input = "select nome, count(*) from cliente c join pedido p on p.cliente_id = c.id where c.nome <> 'x y' -- comentário\norder by nome";

        var before = Normalize(input);
        var after = Normalize(SqlFormatter.Format(input));

        after.ShouldBe(before);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Formatador_SeparaJoinComMaiusculasEIndentaOnEAndOr()
    {
        var formatted = SqlFormatter.Format("select * from a left outer join b on b.a_id = a.id and b.ok = 1");

        formatted.ShouldContain("LEFT OUTER JOIN b");
        formatted.ShouldContain("  ON b.a_id = a.id");
        formatted.ShouldContain("  AND b.ok = 1");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Status_AvisaQuandoSomenteLeituraNaoFoiAplicada()
    {
        var status = ConnectionStatus.Build("Loja", DatabaseEngine.Firebird, "loja.fdb", ConnectionEnvironment.Production,
            readOnlyRequested: true, readOnlyEnforced: false);

        status.IsProduction.ShouldBeTrue();
        status.ReadOnlyEnforced.ShouldBeFalse();
        status.Warning.ShouldNotBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Status_SemAvisoQuandoSomenteLeituraEstaAplicada()
    {
        var status = ConnectionStatus.Build("Loja", DatabaseEngine.Sqlite, "loja.db", ConnectionEnvironment.Development,
            readOnlyRequested: true, readOnlyEnforced: true);

        status.IsProduction.ShouldBeFalse();
        status.Warning.ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Exportacao_CsvSeguePadraoComNullVazioEAspas()
    {
        var rows = new List<object?[]> { new object?[] { 1, "Ana, \"A\"" }, new object?[] { 2, null } };
        await using var stream = new MemoryStream();

        await ResultExportService.ExportAsync(stream, ExportFormat.Csv, ["id", "nome"], rows, TestContext.Current.CancellationToken);

        Encoding.UTF8.GetString(stream.ToArray()).ShouldBe("\"id\",\"nome\"\r\n1,\"Ana, \"\"A\"\"\"\r\n2,\r\n");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Exportacao_JsonGravaNull()
    {
        var rows = new List<object?[]> { new object?[] { 2, null } };
        await using var stream = new MemoryStream();

        await ResultExportService.ExportAsync(stream, ExportFormat.Json, ["id", "nome"], rows, TestContext.Current.CancellationToken);

        Encoding.UTF8.GetString(stream.ToArray()).ShouldContain("null");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Script_SalvarEAbrirPreservaTextoEmUtf8()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mds-script-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "consulta.sql");
        const string text = "select 'ação' from cliente;";

        await ScriptFileService.SaveAsync(path, text, TestContext.Current.CancellationToken);
        var back = await ScriptFileService.OpenAsync(path, TestContext.Current.CancellationToken);

        back.ShouldBe(text);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Referencias_EncontraFkQueAponta()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);

        var incoming = await new ReferenceFinder(bench.Provider)
            .FindIncomingAsync(connection, new ObjectName("main", "cliente"), TestContext.Current.CancellationToken);

        incoming.Select(r => r.Child.Name).ShouldBe(["pedido"]);
        incoming.Single().ReferencedColumns.ShouldBe(["id"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Explain_SqliteRetornaPlanoSemExecutarAConsulta()
    {
        await using var bench = await SqliteWorkbench.CreateAsync(ct: TestContext.Current.CancellationToken);
        await using var connection = await bench.OpenAsync(TestContext.Current.CancellationToken);

        var plan = await ExplainService.ExplainAsync(connection, DatabaseEngine.Sqlite,
            "select * from pedido where cliente_id = 1", TestContext.Current.CancellationToken);

        plan.Columns.ShouldContain("detail");
        plan.Rows.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("update cliente set nome = 'x'")]
    [InlineData("delete from pedido")]
    public void Explain_RecusaComandosQueNaoSaoLeitura(string sql)
    {
        Should.Throw<ArgumentException>(() => ExplainService.BuildStatement(DatabaseEngine.Sqlite, sql));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Explain_FirebirdNaoTemComandoNestaVersao()
    {
        ExplainService.BuildStatement(DatabaseEngine.Firebird, "select 1 from rdb$database").ShouldBeNull();
    }

    private static string Normalize(string sql) =>
        string.Concat(sql.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
