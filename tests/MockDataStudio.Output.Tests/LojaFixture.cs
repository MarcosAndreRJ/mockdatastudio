using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Planning;
using MockDataStudio.Output;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Models;
using MockDataStudio.Providers.Sqlite;
using ExecTable = MockDataStudio.Generation.Engine.TablePlan;

namespace MockDataStudio.Output.Tests;

// Schema "loja" (SQLite) real, plano do planejador e linhas escritas à mão: exercita o writer sem depender do motor multi-tabela.
internal sealed class LojaFixture : IAsyncDisposable
{
    public static readonly Guid RunId = Guid.Parse("5b1f0c7e-2a44-4d61-9b0e-7f3c2d1a9e80");

    private static readonly string[] Tables = ["categoria", "cliente", "produto", "pedido", "item_pedido", "entrega"];

    private LojaFixture(string dir, SqliteProvider provider, SchemaModel schema, GenerationPlan plan, ISqlDialect dialect, string structureHash)
    {
        Dir = dir;
        Provider = provider;
        Schema = schema;
        Plan = plan;
        Dialect = dialect;
        StructureHash = structureHash;
    }

    public string Dir { get; }

    public SqliteProvider Provider { get; }

    public SchemaModel Schema { get; }

    public GenerationPlan Plan { get; }

    public ISqlDialect Dialect { get; }

    public string StructureHash { get; }

    public static ObjectName Name(string table) => new("main", table);

    public static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MockDataStudio.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada."), relative);
    }

    public static async Task<LojaFixture> CreateAsync(CancellationToken ct, IReadOnlyDictionary<string, long?>? maxKeys = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mds-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var provider = new SqliteProvider();

        await using var source = await CreateDatabaseAsync(Path.Combine(dir, "origem.db"), ct);
        var schemaResult = await provider.CreateSchemaExtractor().ExtractAsync(source, new CaptureOptions(), null, ct);
        if (!schemaResult.IsSuccess)
        {
            throw new InvalidOperationException(schemaResult.Error.Message);
        }

        var schema = schemaResult.Value;
        var profileTables = Tables.ToDictionary(t => Name(t).CanonicalId, t => new TableSettings(true, RowCounts[t]));
        var profile = new GenerationProfile("mini", 20261011UL, new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(-3)),
            ReferenceInstantMode.Fixed, "UTC", profileTables, KeyStrategy.Explicit, new OutputSettings(OutputKind.SqlScript), new ValidationSettings());
        var target = new TargetState(DatabaseEngine.Sqlite, "3.46", "fp", provider.GetCapabilities("3.46"),
            maxKeys ?? new Dictionary<string, long?>(), new Dictionary<string, long?>(),
            new Dictionary<string, IReadOnlyList<TriggerModel>>(), IsOffline: false);

        var planned = new GenerationPlanner().Plan(new PlanningInput(schema, new Dictionary<string, ColumnBinding>(), profile, target, "ref"));
        if (planned.Plan is null)
        {
            throw new InvalidOperationException("Plano bloqueado: " + string.Join("; ", planned.Diagnostics.Select(d => d.Code + " " + d.Message)));
        }

        return new LojaFixture(dir, provider, schema, planned.Plan, provider.GetDialect("3.46"), new StructureHasher().Compute(schema));
    }

    public static readonly IReadOnlyDictionary<string, long> RowCounts = new Dictionary<string, long>
    {
        ["categoria"] = 3, ["cliente"] = 3, ["produto"] = 3, ["pedido"] = 3, ["item_pedido"] = 4, ["entrega"] = 2,
    };

    // Banco novo com o schema da loja (docs/examples/schema-loja-sqlite.sql).
    public static async Task<SqliteConnection> CreateDatabaseAsync(string file, CancellationToken ct)
    {
        var connection = new SqliteConnection("Data Source=" + file + ";Pooling=False");
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = await File.ReadAllTextAsync(RepoFile("docs/examples/schema-loja-sqlite.sql"), ct);
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }

    public ScriptHeader Header(string engine = "sqlite", string? version = "3.46") =>
        new("1.0.0", StructureHash, Plan.PlanHash, 20261011UL, new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.FromHours(-3)),
            new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.Zero), engine, version, new Dictionary<string, int>());

    public OutputContext Context(OutputSettings settings, ISqlDialect? dialect = null, string? engine = null, string? version = null) =>
        new(RunId, "Loja Demo", "mini", Plan, Schema, settings with { Directory = Dir, FileNamePattern = "loja_mini" },
            dialect ?? Dialect, engine is null ? Header() : Header(engine, version));

    public long Start(string table) => Plan.Tables[Name(table)].Keys[0].Start ?? 1;

    // Linhas da loja, no formato do exemplo docs/examples/output-sqlite.sql; as chaves seguem a faixa reservada pelo plano.
    public IEnumerable<RowBatch> Batches()
    {
        long s(string t) => Start(t);
        DateTime d(string text) => DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        yield return Batch("categoria", ["id", "nome", "pai_id"],
        [
            [s("categoria"), "Eletrônicos", null],
            [s("categoria") + 1, "Informática", s("categoria")],
            [s("categoria") + 2, "Papelaria", null],
        ]);
        yield return Batch("cliente", ["id", "nome", "cpf", "email", "zap", "nro_cep", "cidade", "uf", "nascimento", "ativo", "criado_em"],
        [
            [s("cliente"), "Ana Exemplo Souza", "52998224725", "ana@exemplo.com.br", "(31) 99812-4410", "30130010", "Belo Horizonte", "MG", d("1987-04-12"), true, d("2026-03-02 10:15:33")],
            [s("cliente") + 1, "Bruno Fictício Lima", "11144477735", null, "(11) 98765-0021", "01310100", "São Paulo", "SP", d("1979-11-30"), true, d("2026-07-19 08:02:11")],
            [s("cliente") + 2, "Carla D'Ávila Teste", "39053344705", "carla@teste.dev", null, "80010000", "Curitiba", "PR", d("2001-01-05"), false, d("2025-12-24 23:59:59")],
        ]);
        yield return Batch("produto", ["id", "sku", "descricao", "categoria_id", "preco", "estoque", "atributos"],
        [
            [s("produto"), "KQZ-48213", "Notebook Modelo Exemplo 14\"", s("categoria") + 1, 4299.90m, 12L, "{}"],
            [s("produto") + 1, "BHA-00917", "Caderno Fictício 200 folhas", s("categoria") + 2, 32.50m, 340L, "{}"],
            [s("produto") + 2, "TRM-55102", "Fone de Ouvido Teste", s("categoria"), 149.00m, 0L, "{}"],
        ]);
        yield return Batch("pedido", ["id", "cliente_id", "data_pedido", "status", "total"],
        [
            [s("pedido"), s("cliente"), d("2026-09-30 14:22:05"), "PAGO", 0m],
            [s("pedido") + 1, s("cliente"), d("2026-10-02 09:11:47"), "ENVIADO", 0m],
            [s("pedido") + 2, s("cliente") + 2, d("2026-08-15 19:40:00"), "ABERTO", 0m],
        ]);
        yield return Batch("item_pedido", ["pedido_id", "item_seq", "produto_id", "quantidade", "preco_unit"],
        [
            [s("pedido"), 1L, s("produto"), 1L, 4299.90m],
            [s("pedido"), 2L, s("produto") + 2, 2L, 149.00m],
            [s("pedido") + 1, 1L, s("produto") + 1, 5L, 32.50m],
            [s("pedido") + 2, 1L, s("produto") + 2, 1L, 149.00m],
        ]);
        yield return Batch("entrega", ["id", "pedido_id", "item_seq", "previsao", "cep_entrega"],
        [
            ["3f2504e0-4f89-41d3-9a0c-0305e82c3301", s("pedido"), 1L, d("2026-10-14"), "30130010"],
            ["a8098c1a-f86e-41da-b7c5-2b1f3e0d7c22", s("pedido") + 1, 1L, null, "01310100"],
        ]);
    }

    private static RowBatch Batch(string table, string[] columns, object?[][] rows) => new(Name(table), 0, columns, rows);

    // Percorre o plano como o motor faria: tabelas na ordem dos passos, depois as ressincronizações.
    public async Task<OutputResult> RunAsync(IOutputWriter writer, OutputContext ctx, RunStatus status = RunStatus.Completed, CancellationToken ct = default)
    {
        await writer.BeginAsync(ctx, ct);
        var batches = Batches().ToDictionary(b => b.Table);
        foreach (var step in Plan.Steps)
        {
            switch (step)
            {
                case InsertTableStep insert:
                    var batch = batches[insert.Table];
                    await writer.BeginTableAsync(new ExecTable(insert.Table, [], batch.Rows.Length, 1, DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc,
                        new Dictionary<string, TemplateDataSet>()), ct);
                    await writer.WriteBatchAsync(batch, ct);
                    await writer.EndTableAsync(new TableRunResult(insert.Table, batch.Rows.Length, []), ct);
                    break;
                default:
                    await writer.ExecuteSessionStepAsync(step, ct);
                    break;
            }
        }

        return await writer.CompleteAsync(status, ct);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Dir, recursive: true);
        }
        catch (IOException)
        {
            // Arquivo ainda aberto: a pasta temporária é limpa pelo sistema.
        }

        return ValueTask.CompletedTask;
    }
}
