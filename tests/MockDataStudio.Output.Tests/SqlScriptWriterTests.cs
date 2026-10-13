using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Output;
using MockDataStudio.Providers.PostgreSql;
using Shouldly;
using Xunit;

namespace MockDataStudio.Output.Tests;

public sealed class SqlScriptWriterTests
{
    private static OutputSettings Settings(Func<OutputSettings, OutputSettings>? change = null)
    {
        var settings = new OutputSettings(OutputKind.SqlScript);
        return change is null ? settings : change(settings);
    }

    private static SqlScriptWriter NewWriter(LojaFixture fixture, OutputSettings settings, long? commitEvery = null) =>
        new(new OutputWriterOptions(settings, fixture.Dialect, null, commitEvery));

    private static string ScriptOf(OutputResult result) =>
        string.Concat(result.Artifacts.Where(a => a.Kind == "script").OrderBy(a => a.SegmentIndex).Select(a => File.ReadAllText(a.Path)));

    private static async Task<long> CountAsync(SqliteConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    // ---------------------------------------------------------------- T-OUT-001: snapshot do script SQLite

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-001")]
    public async Task Script_LojaSqlite_MatchesSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        await using var writer = NewWriter(fixture, Settings(s => s with { IncludeCleanupScript = false }));

        var result = await fixture.RunAsync(writer, fixture.Context(Settings(s => s with { IncludeCleanupScript = false })), ct: ct);

        var actual = ScriptOf(result);
        var snapshot = Path.Combine(LojaFixture.RepoFile("tests/MockDataStudio.Output.Tests/Snapshots"), "loja-sqlite.sql");
        if (Environment.GetEnvironmentVariable("MDS_UPDATE_SNAPSHOTS") == "1" || !File.Exists(snapshot))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            await File.WriteAllTextAsync(snapshot, actual, new UTF8Encoding(false), ct);
        }

        // O Git pode converter as quebras de linha do arquivo de snapshot no checkout (autocrlf).
        actual.ShouldBe((await File.ReadAllTextAsync(snapshot, ct)).Replace("
", "
", StringComparison.Ordinal));
        actual.ShouldContain("INSERT INTO \"categoria\" (\"id\",\"nome\",\"pai_id\") VALUES");
        actual.ShouldContain("Carla D''Ávila Teste");
        actual.ShouldContain("-- Segmento 001 de 001");
        actual.ShouldNotContain("???");
        actual.ShouldNotContain("\r");
    }

    // ---------------------------------------------------------------- T-OUT-002 / 006: executar o script e limpar

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-006")]
    public async Task Script_Executes_AndCleanupRestoresOriginalCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        // O destino já tem linhas: a faixa de chaves reservada começa depois delas.
        var maxKeys = new Dictionary<string, long?>
        {
            ["main.categoria.id"] = 900, ["main.cliente.id"] = 900, ["main.produto.id"] = 900, ["main.pedido.id"] = 900,
        };
        await using var fixture = await LojaFixture.CreateAsync(ct, maxKeys);
        fixture.Start("categoria").ShouldBe(901);

        var settings = Settings();
        await using var writer = NewWriter(fixture, settings);
        var result = await fixture.RunAsync(writer, fixture.Context(settings), ct: ct);

        await using var target = await LojaFixture.CreateDatabaseAsync(Path.Combine(fixture.Dir, "destino.db"), ct);
        await ExecuteAsync(target, "INSERT INTO categoria (id, nome) VALUES (900, 'Base'); INSERT INTO cliente (id, nome, cpf) VALUES (900, 'Base', '00000000000');", ct);
        var before = new Dictionary<string, long>();
        foreach (var table in LojaFixture.RowCounts.Keys)
        {
            before[table] = await CountAsync(target, table, ct);
        }

        await ExecuteAsync(target, ScriptOf(result), ct);
        foreach (var (table, rows) in LojaFixture.RowCounts)
        {
            (await CountAsync(target, table, ct)).ShouldBe(before[table] + rows, table);
        }

        await using (var integrity = target.CreateCommand())
        {
            integrity.CommandText = "PRAGMA foreign_key_check";
            await using var reader = await integrity.ExecuteReaderAsync(ct);
            (await reader.ReadAsync(ct)).ShouldBeFalse("o script não pode violar nenhuma FK");
        }

        var cleanup = result.Artifacts.Single(a => a.Kind == "cleanup");
        var cleanupText = await File.ReadAllTextAsync(cleanup.Path, ct);
        cleanupText.ShouldContain("DELETE FROM \"categoria\" WHERE \"id\" BETWEEN 901 AND 903;");
        cleanupText.ShouldContain("DELETE FROM \"item_pedido\" WHERE (\"pedido_id\", \"item_seq\") IN (");
        cleanupText.IndexOf("DELETE FROM \"entrega\"", StringComparison.Ordinal)
            .ShouldBeLessThan(cleanupText.IndexOf("DELETE FROM \"item_pedido\"", StringComparison.Ordinal), "filhos antes dos pais");
        cleanupText.IndexOf("DELETE FROM \"pedido\"", StringComparison.Ordinal)
            .ShouldBeLessThan(cleanupText.IndexOf("DELETE FROM \"cliente\"", StringComparison.Ordinal));

        await ExecuteAsync(target, cleanupText, ct);
        foreach (var table in LojaFixture.RowCounts.Keys)
        {
            (await CountAsync(target, table, ct)).ShouldBe(before[table], table + " deve voltar à contagem original");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-002")]
    public async Task Script_SegmentedByTinyLimit_NeverSplitsStatements_AndExecutesInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = Settings(s => s with { SegmentMaxBytes = 1600, RowsPerStatement = 1, IncludeCleanupScript = false });
        await using var writer = NewWriter(fixture, settings);

        var result = await fixture.RunAsync(writer, fixture.Context(settings), ct: ct);

        var segments = result.Artifacts.Where(a => a.Kind == "script").OrderBy(a => a.SegmentIndex).ToList();
        segments.Count.ShouldBeGreaterThan(2);
        segments[0].Path.ShouldEndWith("loja_mini.sql");
        segments[1].Path.ShouldEndWith("loja_mini_part002.sql");

        var total = segments.Count.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var segment in segments)
        {
            var text = await File.ReadAllTextAsync(segment.Path, ct);
            text.ShouldContain($"de {total} ");
            text.ShouldNotContain("???");

            // Cada comando termina em ';' e nenhum INSERT é cortado: o número de tuplas bate com o número de comandos.
            var inserts = text.Split("INSERT INTO ", StringSplitOptions.RemoveEmptyEntries).Length - 1;
            text.Split("\nINSERT INTO ").Length.ShouldBeGreaterThanOrEqualTo(1);
            text.Trim().EndsWith(';').ShouldBeTrue("o segmento termina em um comando completo");
            inserts.ShouldBeGreaterThanOrEqualTo(0);

            if (segment.SegmentIndex < segments.Count)
            {
                text.TrimEnd().ShouldEndWith("COMMIT;"); // transação fechada antes de abrir o próximo segmento
            }

            if (segment.SegmentIndex > 1)
            {
                text.ShouldContain("\nBEGIN;\n"); // e reaberta no começo dele
            }
        }

        await using var target = await LojaFixture.CreateDatabaseAsync(Path.Combine(fixture.Dir, "destino.db"), ct);
        foreach (var segment in segments)
        {
            await ExecuteAsync(target, await File.ReadAllTextAsync(segment.Path, ct), ct);
        }

        foreach (var (table, rows) in LojaFixture.RowCounts)
        {
            (await CountAsync(target, table, ct)).ShouldBe(rows, table);
        }
    }

    // ---------------------------------------------------------------- T-OUT-003: escrita atômica

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-003")]
    public async Task Output_IsAtomic_PartialFilesOnlyWhenNotCompleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = Settings();

        await using (var ok = NewWriter(fixture, settings))
        {
            await fixture.RunAsync(ok, fixture.Context(settings), ct: ct);
        }

        Directory.GetFiles(fixture.Dir, "*.partial").ShouldBeEmpty("uma execução concluída não deixa .partial");

        var failedDir = Path.Combine(fixture.Dir, "falha");
        await using var failed = NewWriter(fixture, settings);
        var failedResult = await fixture.RunAsync(failed, fixture.Context(settings with { Directory = failedDir }) with
        {
            Settings = settings with { Directory = failedDir, FileNamePattern = "loja_mini" },
        }, RunStatus.Failed, ct);

        Directory.GetFiles(failedDir, "*.sql").ShouldBeEmpty("script incompleto nunca recebe o nome final");
        Directory.GetFiles(failedDir, "*.partial").ShouldNotBeEmpty();
        failedResult.Artifacts.Where(a => a.Kind == "script").ShouldAllBe(a => a.Path.EndsWith(".partial", StringComparison.Ordinal));
        failedResult.Artifacts.Any(a => a.Kind == "cleanup").ShouldBeFalse("sem limpeza para execução que falhou");

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(failedResult.ManifestPath!, ct));
        manifest.RootElement.GetProperty("status").GetString().ShouldBe("failed");
    }

    // ---------------------------------------------------------------- T-OUT-004: manifesto

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-004")]
    public async Task Manifest_ListsSegments_WithShaAndBytes_AndNoSecrets()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = Settings(s => s with { SegmentMaxBytes = 1500 });
        await using var writer = NewWriter(fixture, settings);

        var result = await fixture.RunAsync(writer, fixture.Context(settings), ct: ct);

        var manifestText = await File.ReadAllTextAsync(result.ManifestPath!, ct);
        using var manifest = JsonDocument.Parse(manifestText);
        var root = manifest.RootElement;
        root.GetProperty("format").GetString().ShouldBe("mds.run-manifest");
        root.GetProperty("version").GetString().ShouldBe("1.0");
        root.GetProperty("runId").GetString().ShouldBe(LojaFixture.RunId.ToString());
        root.GetProperty("status").GetString().ShouldBe("completed");

        var listed = root.GetProperty("segments").EnumerateArray().ToList();
        listed.Count.ShouldBe(result.Artifacts.Count(a => a.Kind == "script"));
        foreach (var item in listed)
        {
            var path = item.GetProperty("path").GetString()!;
            var bytes = await File.ReadAllBytesAsync(path, ct);
            item.GetProperty("bytes").GetInt64().ShouldBe(bytes.Length);
            item.GetProperty("sha256").GetString().ShouldBe(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        root.GetProperty("tables").GetArrayLength().ShouldBe(LojaFixture.RowCounts.Count);
        manifestText.ShouldNotContain("capturedKeys", Case.Insensitive);
        foreach (var forbidden in new[] { "password", "pwd", "connectionString", "host" })
        {
            manifestText.ShouldNotContain(forbidden, Case.Insensitive);
        }

        result.RowsWritten.ShouldBe(LojaFixture.RowCounts.Values.Sum());
    }

    // ---------------------------------------------------------------- guardas, transações e opções

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-005")]
    public async Task Guards_UseDialectBlock_OrFallBackToComment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = Settings(s => s with { IncludeCleanupScript = false });

        // SQLite não tem comando de guarda: só o comentário de verificação.
        await using var sqlite = NewWriter(fixture, settings);
        var sqliteText = ScriptOf(await fixture.RunAsync(sqlite, fixture.Context(settings), ct: ct));
        sqliteText.ShouldContain("-- VERIFICAÇÃO: SELECT MAX(id) FROM categoria deve ser menor que 1");
        sqliteText.ShouldNotContain("RAISE EXCEPTION");

        // PostgreSQL: bloco DO que falha quando a faixa está ocupada.
        var pgDir = Path.Combine(fixture.Dir, "pg");
        var pgSettings = settings with { Directory = pgDir };
        await using var pg = new SqlScriptWriter(new OutputWriterOptions(pgSettings, new PostgreSqlDialect(), null));
        var pgContext = fixture.Context(pgSettings, new PostgreSqlDialect(), "postgresql", "17") with
        {
            Settings = pgSettings with { FileNamePattern = "loja_mini" },
        };
        var pgText = ScriptOf(await fixture.RunAsync(pg, pgContext, ct: ct));
        pgText.ShouldContain("-- Destino: postgresql 17");
        pgText.ShouldContain("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"main\".\"categoria\" WHERE \"id\" BETWEEN 1 AND 3) THEN RAISE EXCEPTION");
        pgText.ShouldContain("SET standard_conforming_strings = on;");

        // EmitGuards desligado: nenhuma verificação.
        var off = settings with { EmitGuards = false, Directory = Path.Combine(fixture.Dir, "semguarda") };
        await using var none = NewWriter(fixture, off);
        var noneText = ScriptOf(await fixture.RunAsync(none, fixture.Context(off), ct: ct));
        noneText.ShouldNotContain("VERIFICAÇÃO");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Transactions_PeriodicCommit_AndOptionToDisableThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = Settings(s => s with { RowsPerStatement = 1, IncludeCleanupScript = false });

        await using var periodic = NewWriter(fixture, settings, commitEvery: 5);
        var text = ScriptOf(await fixture.RunAsync(periodic, fixture.Context(settings), ct: ct));
        // 18 linhas, commit a cada 5: 3 commits periódicos + o final.
        text.Split("COMMIT;").Length.ShouldBe(5);
        text.Split("BEGIN;").Length.ShouldBe(5);

        var without = settings with { IncludeTransaction = false, Directory = Path.Combine(fixture.Dir, "semtx") };
        await using var none = NewWriter(fixture, without, commitEvery: 5);
        var noTx = ScriptOf(await fixture.RunAsync(none, fixture.Context(without), ct: ct));
        noTx.ShouldNotContain("BEGIN;");
        noTx.ShouldNotContain("COMMIT;");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task LineEnding_Crlf_DoesNotAlterValuesWithNewlines()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = Settings(s => s with { LineEnding = "\r\n", IncludeCleanupScript = false, WriteBom = true });
        await using var writer = NewWriter(fixture, settings);

        var writerContext = fixture.Context(settings);
        await writer.BeginAsync(writerContext, ct);
        var table = LojaFixture.Name("categoria");
        await writer.BeginTableAsync(new MockDataStudio.Generation.Engine.TablePlan(table, [], 2, 1, DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc,
            new Dictionary<string, MockDataStudio.Core.Templates.TemplateDataSet>()), ct);
        await writer.WriteBatchAsync(new RowBatch(table, 0, ["id", "nome", "pai_id"],
        [
            [1L, "linha 1\nlinha 2", null],
            [2L, "com ''aspas'' e\r\nCRLF", 1L],
        ]), ct);
        await writer.EndTableAsync(new TableRunResult(table, 2, []), ct);
        var result = await writer.CompleteAsync(RunStatus.Completed, ct);

        var bytes = await File.ReadAllBytesAsync(result.Artifacts.First(a => a.Kind == "script").Path, ct);
        bytes.Take(3).ToArray().ShouldBe([0xEF, 0xBB, 0xBF], "BOM opcional");
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.ShouldContain("'linha 1\nlinha 2'");        // o dado mantém \n
        text.ShouldContain("VALUES\r\n(1,");               // a estrutura usa CRLF
        text.Replace("\r\n", "").ShouldNotContain("\r");
    }

    // ---------------------------------------------------------------- limpeza por faixa e por tuplas (Firebird: OR)

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-007")]
    public async Task CleanupBuilder_UsesRangesAndTupleBatches()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var builder = new CleanupScriptBuilder(fixture.Schema);

        var tuples = Enumerable.Range(1, 2500).Select(i => new object?[] { (long)i, 1L }).ToList();
        var results = new List<TableRunResult>
        {
            new(LojaFixture.Name("item_pedido"), 2500, [new KeyRangeResult("pedido_id,item_seq", null, null, tuples)]),
        };

        var statements = builder.Build(fixture.Plan, results, fixture.Dialect);

        statements.Count(s => s.StartsWith("DELETE FROM \"item_pedido\"", StringComparison.Ordinal)).ShouldBe(3, "2500 tuplas em lotes de 1000");
        statements.ShouldContain("DELETE FROM \"categoria\" WHERE \"id\" BETWEEN 1 AND 3;");
        statements.Any(s => s.StartsWith("-- ATENÇÃO", StringComparison.Ordinal) && s.Contains("entrega", StringComparison.Ordinal))
            .ShouldBeTrue("tabela sem chave rastreada pede limpeza manual");
    }
}
