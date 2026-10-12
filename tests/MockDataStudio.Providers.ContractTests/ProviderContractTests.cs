using System.Data.Common;
using System.Diagnostics;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Models;
using MockDataStudio.Providers.ContractTests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Providers.ContractTests;

// Suíte abstrata de contrato de provider (docs/26 §4). Cada SGBD herda esta classe e implementa
// ProvisionAsync; os testes só usam as interfaces de C02. Os IDs T-PRV-<X>-NNN são o número NNN aqui,
// com X definido pela subclasse.
public abstract class ProviderContractTests : IDisposable
{
    private const string LojaExample = "docs/examples/schema-loja-sqlite.sql";
    private const string LojaDigestExample = "docs/examples/digest-sqlite.json";
    private const string LojaDatabaseName = "loja.db";

    private readonly List<string> _tempFolders = [];

    protected abstract IDatabaseProvider Provider { get; }

    // Cria um banco novo com o script de schema e devolve as configurações para conectar.
    protected abstract Task<ConnectionSettings> ProvisionAsync(string schemaSql, string databaseName);

    // Pasta temporária criada pela subclasse; removida no Dispose.
    protected void TrackFolder(string folder) => _tempFolders.Add(folder);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var folder in _tempFolders)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Arquivo ainda referenciado pelo driver: a pasta temporária é limpa pelo sistema.
            }
        }
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-001")]
    public async Task Contrato001_ConectaEObtemServerInfoComFingerprintEstavel()
    {
        var settings = await ProvisionAsync(await File.ReadAllTextAsync(RepoPaths.Combine(LojaExample), TestContext.Current.CancellationToken), LojaDatabaseName);

        await using var first = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);
        var info1 = await Provider.GetServerInfoAsync(first, TestContext.Current.CancellationToken);
        await using var second = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);
        var info2 = await Provider.GetServerInfoAsync(second, TestContext.Current.CancellationToken);

        info1.Version.ShouldNotBeNullOrWhiteSpace();
        info1.DatabaseName.ShouldBe(LojaDatabaseName);
        info1.FingerprintHash.ShouldStartWith("sha256:");
        info2.FingerprintHash.ShouldBe(info1.FingerprintHash);
        Provider.GetCapabilities(info1.Version).MaxParametersPerCommand.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-002")]
    public async Task Contrato002_ExtraiLojaCujoDigestEhIgualAoExemplo()
    {
        var settings = await ProvisionAsync(await File.ReadAllTextAsync(RepoPaths.Combine(LojaExample), TestContext.Current.CancellationToken), LojaDatabaseName);
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);

        var extraction = await Provider.CreateSchemaExtractor()
            .ExtractAsync(connection, new CaptureOptions(IncludeRoutines: false), null, TestContext.Current.CancellationToken);
        extraction.IsSuccess.ShouldBeTrue(extraction.IsSuccess ? string.Empty : extraction.Error.Message);

        var serializer = new DigestSerializer(new StructureHasher());
        var actual = JsonNode.Parse(serializer.Serialize(extraction.Value, new DigestWriteOptions(Indented: true)))!.AsObject();
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(RepoPaths.Combine(LojaDigestExample), TestContext.Current.CancellationToken))!.AsObject();

        // Excluídos do comparativo: hash (recalculado), capturedAt e server (dependem do ambiente), conforme docs/28 TASK-02.03.
        foreach (var volatileKey in new[] { "hash", "capturedAt", "server" })
        {
            actual.Remove(volatileKey);
            expected.Remove(volatileKey);
        }

        JsonNode.DeepEquals(actual, expected).ShouldBeTrue(
            "Digest extraído diverge do exemplo.\nEsperado:\n" + expected.ToJsonString() + "\nObtido:\n" + actual.ToJsonString());
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-003")]
    public async Task Contrato003_MapeiaTiposDoFixtureParaLogicalType()
    {
        var settings = await ProvisionAsync(await File.ReadAllTextAsync(RepoPaths.Combine("tests", "fixtures", "schemas", "types.sqlite.sql"), TestContext.Current.CancellationToken), "types.db");
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);

        var table = await ExtractTableAsync(connection, "tipos");
        var expected = new Dictionary<string, LogicalType>(StringComparer.Ordinal)
        {
            ["c_bool"] = LogicalType.Boolean,
            ["c_int"] = LogicalType.Integer,
            ["c_smallint"] = LogicalType.Integer,
            ["c_bigint"] = LogicalType.Integer,
            ["c_dec"] = LogicalType.Decimal,
            ["c_real"] = LogicalType.Float,
            ["c_varchar"] = LogicalType.String,
            ["c_text"] = LogicalType.Text,
            ["c_blob"] = LogicalType.Binary,
            ["c_date"] = LogicalType.Date,
            ["c_time"] = LogicalType.Time,
            ["c_datetime"] = LogicalType.DateTime,
            ["c_timestamp"] = LogicalType.DateTime,
            ["c_uuid"] = LogicalType.Uuid,
            ["c_json"] = LogicalType.Json,
        };

        foreach (var (name, logical) in expected)
        {
            table.FindColumn(name).ShouldNotBeNull(name);
            table.FindColumn(name)!.Type.LogicalType.ShouldBe(logical, name);
        }
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-004")]
    public async Task Contrato004_DetectaIdentityDaChavePrimariaInteiraSemChaveComposta()
    {
        var settings = await ProvisionAsync(await File.ReadAllTextAsync(RepoPaths.Combine(LojaExample), TestContext.Current.CancellationToken), LojaDatabaseName);
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);

        var categoria = await ExtractTableAsync(connection, "categoria");
        categoria.FindColumn("id")!.Identity.ShouldNotBeNull();
        categoria.FindColumn("id")!.Identity!.Kind.ShouldBe(IdentityKind.AutoIncrement);

        var itemPedido = await ExtractTableAsync(connection, "item_pedido");
        itemPedido.FindColumn("pedido_id")!.Identity.ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-005")]
    public async Task Contrato005_ListaChecksTriggersEViewsSemCorpoDeRotina()
    {
        var settings = await ProvisionAsync(await File.ReadAllTextAsync(RepoPaths.Combine(LojaExample), TestContext.Current.CancellationToken), LojaDatabaseName);
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);

        var extraction = await Provider.CreateSchemaExtractor()
            .ExtractAsync(connection, new CaptureOptions(IncludeRoutines: false), null, TestContext.Current.CancellationToken);
        extraction.IsSuccess.ShouldBeTrue();

        var main = extraction.Value.Namespaces.Single();
        main.Tables.Single(t => t.Name == "produto").Checks.Select(c => c.Expression).ShouldContain("preco >= 0");
        main.Views.Select(v => v.Name).ShouldContain("vw_pedidos_cliente");
        main.Triggers.Select(t => t.Name).ShouldContain("trg_item_atualiza_total");
        main.Routines.ShouldBeEmpty();
        main.Sequences.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-006")]
    public async Task Contrato006_BulkInsertGravaDezMilLinhas()
    {
        var settings = await ProvisionAsync(CargaSchema, "carga.db");
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);
        var table = await ExtractTableAsync(connection, "carga");

        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var inserter = Provider.CreateBulkInserter(connection, "3");
        await inserter.PrepareAsync(new ObjectName("main", "carga"), table.Columns, new InsertOptions(OverrideIdentity: false, CaptureColumns: null), TestContext.Current.CancellationToken);

        var rows = Enumerable.Range(1, 10_000)
            .Select(i => new object?[] { (long)i, "linha " + i, DateTime.UnixEpoch.AddSeconds(i) })
            .ToList();
        var result = await inserter.InsertAsync(rows, transaction, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        result.RowsInserted.ShouldBe(10_000);
        (await Provider.CreateTargetInspector(connection, "3").CountRowsAsync(new ObjectName("main", "carga"), TestContext.Current.CancellationToken))
            .ShouldBe(10_000);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-007")]
    public async Task Contrato007_InspectorLeMaximoContagemTriggersEChaves()
    {
        var settings = await ProvisionAsync(CargaSchema, "carga.db");
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);
        await ExecAsync(connection, "INSERT INTO carga (id, valor, criado) VALUES (1,'a',NULL),(2,'b',NULL),(3,'c',NULL)");

        var inspector = Provider.CreateTargetInspector(connection, "3");
        var carga = new ObjectName("main", "carga");
        (await inspector.GetMaxIntegerAsync(carga, "id", TestContext.Current.CancellationToken)).ShouldBe(3L);
        (await inspector.CountRowsAsync(carga, TestContext.Current.CancellationToken)).ShouldBe(3L);
        (await inspector.AllKeysExistAsync(carga, ["id"], [[1L], [2L]], TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await inspector.AllKeysExistAsync(carga, ["id"], [[1L], [99L]], TestContext.Current.CancellationToken)).ShouldBeFalse();

        var keys = new List<object?[]>();
        await foreach (var key in inspector.ReadKeysAsync(carga, ["id"], 2, TestContext.Current.CancellationToken))
        {
            keys.Add(key);
        }

        keys.Count.ShouldBe(2);
        keys[0][0].ShouldBe(1L);

        var lojaSettings = await ProvisionAsync(await File.ReadAllTextAsync(RepoPaths.Combine(LojaExample), TestContext.Current.CancellationToken), LojaDatabaseName);
        await using var loja = await Provider.OpenConnectionAsync(lojaSettings, null, TestContext.Current.CancellationToken);
        var triggers = await Provider.CreateTargetInspector(loja, "3")
            .GetActiveInsertTriggersAsync(new ObjectName("main", "item_pedido"), TestContext.Current.CancellationToken);
        triggers.Select(t => t.Name).ShouldContain("trg_item_atualiza_total");
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-008")]
    public async Task Contrato008_ErrosDeConstraintESintaxeSaoClassificados()
    {
        var settings = await ProvisionAsync(await File.ReadAllTextAsync(RepoPaths.Combine(LojaExample), TestContext.Current.CancellationToken), LojaDatabaseName);
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);
        await ExecAsync(connection, "INSERT INTO categoria (id, nome) VALUES (1, 'Eletronicos')");

        var cases = new (string Sql, DbErrorCategory Expected)[]
        {
            ("INSERT INTO categoria (id, nome) VALUES (2, 'Eletronicos')", DbErrorCategory.UniqueViolation),
            ("INSERT INTO produto (id, sku, descricao, categoria_id, preco, estoque) VALUES (1, 'S1', 'd', 999, 1, 0)", DbErrorCategory.ForeignKeyViolation),
            ("INSERT INTO categoria (id, nome) VALUES (3, NULL)", DbErrorCategory.NotNullViolation),
            ("INSERT INTO produto (id, sku, descricao, categoria_id, preco, estoque) VALUES (2, 'S2', 'd', 1, -1, 0)", DbErrorCategory.CheckViolation),
            ("SELEC 1", DbErrorCategory.Syntax),
        };

        foreach (var (sql, expected) in cases)
        {
            var error = await CaptureAsync(() => ExecAsync(connection, sql));
            error.ShouldNotBeNull(sql);
            Provider.ErrorTranslator.Categorize(error).ShouldBe(expected, sql);
            Provider.ErrorTranslator.Translate(error).Code.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-009")]
    public async Task Contrato009_CancelamentoDeConsultaLongaTerminaEmSeisSegundos()
    {
        var settings = await ProvisionAsync(CargaSchema, "carga.db");
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();
        var error = await CaptureAsync(async () =>
        {
            await using var command = connection.CreateCommand();
            // Consulta que produz linhas: o cancelamento atua entre linhas. Consultas que agregam numa única etapa
            // não são interrompidas pelo Cancel() do Microsoft.Data.Sqlite (PV-019); ver o caso Skip em SqliteProviderContractTests.
            command.CommandText = "WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c) SELECT x FROM c";
            await using var reader = await command.ExecuteReaderAsync(cts.Token);
            while (await reader.ReadAsync(cts.Token))
            {
            }
        });
        clock.Stop();

        error.ShouldNotBeNull("A consulta longa deveria ser cancelada.");
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(6));
        Provider.ErrorTranslator.Categorize(error).ShouldBe(DbErrorCategory.Cancelled);
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-PRV-NNN-010")]
    public async Task Contrato010_InsertSemChaveDepoisDeChavesExplicitasUsaUltimoMaisUm()
    {
        var settings = await ProvisionAsync(CargaSchema, "carga.db");
        await using var connection = await Provider.OpenConnectionAsync(settings, null, TestContext.Current.CancellationToken);

        await ExecAsync(connection, "INSERT INTO carga (id, valor, criado) VALUES (100, 'explicita', NULL)");
        await ExecAsync(connection, "INSERT INTO carga (valor, criado) VALUES ('automatica', NULL)");

        var next = await ScalarAsync(connection, "SELECT id FROM carga WHERE valor = 'automatica'");
        next.ShouldBe(101L);
    }

    protected const string CargaSchema =
        "CREATE TABLE carga (id INTEGER PRIMARY KEY, valor TEXT NOT NULL, criado DATETIME);";

    protected async Task<TableModel> ExtractTableAsync(DbConnection connection, string table)
    {
        var result = await Provider.CreateSchemaExtractor()
            .ExtractTableAsync(connection, new ObjectName("main", table), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? string.Empty : result.Error.Message);
        return result.Value;
    }

    protected static async Task ExecAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    protected static async Task<object?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    protected static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
