using System.Text;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Output;
using Shouldly;
using Xunit;

namespace MockDataStudio.Output.Tests;

public sealed class SinkAndLiteralTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "mds-sink-" + Guid.NewGuid().ToString("N"));

    // ---------------------------------------------------------------- SegmentedTextSink

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-002")]
    public async Task Sink_RolloverNeverSplitsAStatement_AndBomOffsetsStayCorrect()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = TempDir();
        try
        {
            var options = new SegmentedSinkOptions(120, WriteBom: true, Header: k => $"-- seg {k:D3} de {{n}}",
                BeforeRollover: () => ["COMMIT;"], AfterRollover: () => ["BEGIN;"]);
            await using var sink = new SegmentedTextSink(dir, "saida", ".sql", "script", options);

            var statement = "INSERT INTO t VALUES ('" + new string('x', 60) + "');";
            for (var i = 0; i < 6; i++)
            {
                await sink.WriteStatementAsync(statement, ct);
            }

            await sink.FinishAsync(ct);

            sink.Completed.Count.ShouldBeGreaterThan(2);
            var total = sink.Completed.Count.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var artifact in sink.Completed)
            {
                var bytes = await File.ReadAllBytesAsync(artifact.Path, ct);
                bytes.Take(3).ToArray().ShouldBe([0xEF, 0xBB, 0xBF]);
                var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                text.ShouldStartWith($"-- seg {artifact.SegmentIndex:D3} de {total}\n"); // total gravado na posição certa, depois do BOM
                text.Split('\n').Where(l => l.StartsWith("INSERT", StringComparison.Ordinal)).ShouldAllBe(l => l == statement);
                artifact.Kind.ShouldBe("script");
            }

            Directory.GetFiles(dir, "*.partial").ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-002")]
    public async Task Sink_AtomicBlock_ExceedsLimitWithWarning_InsteadOfSplitting()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = TempDir();
        try
        {
            await using var sink = new SegmentedTextSink(dir, "saida", ".sql", "script", new SegmentedSinkOptions(100));
            sink.BeginAtomic();
            for (var i = 0; i < 5; i++)
            {
                await sink.WriteStatementAsync("SELECT '" + new string('y', 50) + "';", ct);
            }

            sink.EndAtomic();
            await sink.FinishAsync(ct);

            sink.Completed.Count.ShouldBe(1, "um trecho atômico não é dividido");
            sink.Warnings.ShouldContain(w => w.StartsWith("OUT-SEGMENT-OVERSIZE", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Sink_Abort_LeavesPartialFile_AndEmptyOutputStillHasAHeader()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = TempDir();
        try
        {
            await using (var aborted = new SegmentedTextSink(dir, "a", ".sql", "script", new SegmentedSinkOptions(1000)))
            {
                await aborted.WriteStatementAsync("SELECT 1;", ct);
                await aborted.AbortAsync();
            }

            File.Exists(Path.Combine(dir, "a.sql")).ShouldBeFalse();
            File.Exists(Path.Combine(dir, "a.sql.partial")).ShouldBeTrue();

            await using var empty = new SegmentedTextSink(dir, "b", ".sql", "script", new SegmentedSinkOptions(1000, Header: k => "-- vazio {n}"));
            await empty.FinishAsync(ct);
            (await File.ReadAllTextAsync(Path.Combine(dir, "b.sql"), ct)).ShouldBe("-- vazio 001\n");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("INSERT INTO t VALUES\n(1,'a'),\n(2,'b');", "INSERT INTO t VALUES\r\n(1,'a'),\r\n(2,'b');")]
    [InlineData("INSERT INTO t VALUES ('a\nb');", "INSERT INTO t VALUES ('a\nb');")]
    [InlineData("INSERT INTO t VALUES ('it''s\nok');\nSELECT 1;", "INSERT INTO t VALUES ('it''s\nok');\r\nSELECT 1;")]
    [InlineData("SELECT \"col\nname\" FROM t;\nSELECT 2;", "SELECT \"col\nname\" FROM t;\r\nSELECT 2;")]
    [InlineData("x\r\ny", "x\r\ny")]
    public void EolConverter_ConvertsOnlyStructuralNewlines(string input, string expected) =>
        EolConverter.Convert(input, "\r\n").ShouldBe(expected);

    // ---------------------------------------------------------------- literais adversariais (T-OUT-LIT, SQLite)

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-LIT-001")]
    public async Task Script_AdversarialStrings_RoundTripThroughSqlite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var names = new[]
        {
            "O'Brien", "aspas \"duplas\"", @"barra \ invertida \\ dupla", "linha1\nlinha2\r\nlinha3", "tab\tsepara",
            "comentário -- não é comentário", "/* bloco */", "ponto e vírgula; DROP TABLE categoria;", "$$ dólar $$",
            "emoji 😀🚀 e acentos áéíõç", "nul\0no meio", "'; DELETE FROM categoria; --", string.Empty, "   espaços   ",
        };

        var settings = new OutputSettings(OutputKind.SqlScript, IncludeCleanupScript: false);
        await using var writer = new SqlScriptWriter(new OutputWriterOptions(settings, fixture.Dialect, null));
        await writer.BeginAsync(fixture.Context(settings), ct);
        var table = LojaFixture.Name("categoria");
        await writer.BeginTableAsync(new MockDataStudio.Generation.Engine.TablePlan(table, [], names.Length, 1, DateTimeOffset.UnixEpoch,
            TimeZoneInfo.Utc, new Dictionary<string, MockDataStudio.Core.Templates.TemplateDataSet>()), ct);
        var start = fixture.Start("categoria");
        var rows = names.Select((n, i) => new object?[] { start + i, n + "#" + i, null }).ToArray();
        await writer.WriteBatchAsync(new RowBatch(table, 0, ["id", "nome", "pai_id"], rows), ct);
        await writer.EndTableAsync(new TableRunResult(table, names.Length, []), ct);
        var result = await writer.CompleteAsync(RunStatus.Completed, ct);

        await using var target = await LojaFixture.CreateDatabaseAsync(Path.Combine(fixture.Dir, "destino.db"), ct);
        await using (var run = target.CreateCommand())
        {
            run.CommandText = File.ReadAllText(result.Artifacts.First(a => a.Kind == "script").Path);
            await run.ExecuteNonQueryAsync(ct);
        }

        await using var read = target.CreateCommand();
        read.CommandText = "SELECT nome FROM categoria ORDER BY id";
        await using var reader = await read.ExecuteReaderAsync(ct);
        var stored = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            stored.Add(reader.GetString(0));
        }

        stored.ShouldBe(rows.Select(r => (string)r[1]!).ToList());
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-LIT-002")]
    public async Task Script_NonRepresentableValue_FailsWithOutLiteral()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = new OutputSettings(OutputKind.SqlScript, IncludeCleanupScript: false);
        await using var writer = new SqlScriptWriter(new OutputWriterOptions(settings, fixture.Dialect, null));
        await writer.BeginAsync(fixture.Context(settings), ct);
        var table = LojaFixture.Name("produto");

        var error = await Should.ThrowAsync<OutputException>(() => writer.WriteBatchAsync(
            new RowBatch(table, 0, ["id", "sku", "descricao", "categoria_id", "preco", "estoque"],
                [[1L, "X", "x", 1L, double.NaN, 1L]]), ct));

        error.Code.ShouldBe("OUT-LITERAL");
        error.Message.ShouldContain("produto.preco");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Writer_RequiresBeginAndDialect_AndHeaderHasNoSensitiveData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var settings = new OutputSettings(OutputKind.SqlScript);

        await using var notStarted = new SqlScriptWriter(new OutputWriterOptions(settings, fixture.Dialect, null));
        await Should.ThrowAsync<InvalidOperationException>(() => notStarted.WriteBatchAsync(
            new RowBatch(LojaFixture.Name("categoria"), 0, ["id"], [[1L]]), ct));

        await using var noDialect = new SqlScriptWriter(new OutputWriterOptions(settings, null, null));
        var ctx = fixture.Context(settings) with { Dialect = null };
        (await Should.ThrowAsync<OutputException>(() => noDialect.BeginAsync(ctx, ct))).Code.ShouldBe("OUT-IO");

        // Nome de projeto com quebra de linha não escapa do comentário do cabeçalho.
        await using var writer = new SqlScriptWriter(new OutputWriterOptions(settings with { IncludeCleanupScript = false }, fixture.Dialect, null));
        var hostile = fixture.Context(settings with { IncludeCleanupScript = false }) with { ProjectName = "Loja\nDROP TABLE x;" };
        var result = await fixture.RunAsync(writer, hostile, ct: ct);
        var script = File.ReadAllText(result.Artifacts.First(a => a.Kind == "script").Path);
        script.ShouldContain("Projeto: Loja DROP TABLE x;");
        script.Split('\n').Where(l => l.Contains("DROP TABLE x;", StringComparison.Ordinal)).ShouldAllBe(l => l.StartsWith("--", StringComparison.Ordinal));
    }
}

public sealed class SessionAndDeferredTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-008")]
    public async Task Resync_UsesDialectStatement_AndSqliteExplainsWhyNotNeeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var table = LojaFixture.Name("categoria");
        var tablePlan = fixture.Plan.Tables[table] with
        {
            Keys = [new MockDataStudio.Generation.Planning.KeyColumnPlan("id", KeyStrategy.Explicit, 1, 1, false, "categoria_id_seq")],
        };
        var plan = fixture.Plan with { Tables = new Dictionary<MockDataStudio.Core.Naming.ObjectName, MockDataStudio.Generation.Planning.TablePlan>(fixture.Plan.Tables) { [table] = tablePlan } };
        var step = new MockDataStudio.Generation.Planning.ResyncSequenceStep(99, table, "id", "categoria_id_seq");
        var settings = new OutputSettings(OutputKind.SqlScript, IncludeCleanupScript: false);

        var pgDialect = new MockDataStudio.Providers.PostgreSql.PostgreSqlDialect();
        await using var pg = new SqlScriptWriter(new OutputWriterOptions(settings, pgDialect, null));
        await pg.BeginAsync(fixture.Context(settings, pgDialect, "postgresql", "17") with { Plan = plan }, ct);
        await pg.ExecuteSessionStepAsync(step, ct);
        var pgText = File.ReadAllText((await pg.CompleteAsync(RunStatus.Completed, ct)).Artifacts.First(a => a.Kind == "script").Path);
        pgText.ShouldContain("SELECT setval('categoria_id_seq', 4, false);"); // início 1 + 3 linhas

        var sqliteSettings = settings with { Directory = Path.Combine(fixture.Dir, "sqlite") };
        await using var sqlite = new SqlScriptWriter(new OutputWriterOptions(sqliteSettings, fixture.Dialect, null));
        await sqlite.BeginAsync(fixture.Context(sqliteSettings) with { Plan = plan }, ct);
        await sqlite.ExecuteSessionStepAsync(step, ct);
        var sqliteText = File.ReadAllText((await sqlite.CompleteAsync(RunStatus.Completed, ct)).Artifacts.First(a => a.Kind == "script").Path);
        sqliteText.ShouldContain("-- Ressincronização: não necessária para categoria.id");

        // ResyncSequences=false: nada é emitido.
        var offSettings = settings with { ResyncSequences = false, Directory = Path.Combine(fixture.Dir, "off") };
        await using var off = new SqlScriptWriter(new OutputWriterOptions(offSettings, pgDialect, null));
        await off.BeginAsync(fixture.Context(offSettings, pgDialect, "postgresql", "17") with { Plan = plan }, ct);
        await off.ExecuteSessionStepAsync(step, ct);
        File.ReadAllText((await off.CompleteAsync(RunStatus.Completed, ct)).Artifacts.First(a => a.Kind == "script").Path)
            .ShouldNotContain("setval");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-OUT-009")]
    public async Task DeferredUpdates_AndCleanup_NullTheCycleForeignKeysFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = await LojaFixture.CreateAsync(ct);
        var table = LojaFixture.Name("categoria");
        var fkName = fixture.Schema.FindTable(table)!.ForeignKeys.Single().Name!;
        var tablePlan = fixture.Plan.Tables[table] with
        {
            DeferredFks = [new MockDataStudio.Generation.Planning.DeferredFk(fkName, CycleBreakStrategy.NullThenUpdate)],
        };
        var plan = fixture.Plan with { Tables = new Dictionary<MockDataStudio.Core.Naming.ObjectName, MockDataStudio.Generation.Planning.TablePlan>(fixture.Plan.Tables) { [table] = tablePlan } };
        var settings = new OutputSettings(OutputKind.SqlScript);
        await using var writer = new SqlScriptWriter(new OutputWriterOptions(settings, fixture.Dialect, null));

        var result = await fixture.RunAsync(writer, fixture.Context(settings) with { Plan = plan }, ct: ct);
        var script = File.ReadAllText(result.Artifacts.First(a => a.Kind == "script").Path);

        // O writer da chamada acima não recebeu updates adiados; o lote a seguir mostra o formato emitido.
        await using var second = new SqlScriptWriter(new OutputWriterOptions(settings with { Directory = Path.Combine(fixture.Dir, "b") }, fixture.Dialect, null));
        await second.BeginAsync(fixture.Context(settings with { Directory = Path.Combine(fixture.Dir, "b") }) with { Plan = plan }, ct);
        await second.WriteDeferredUpdatesAsync(new DeferredUpdateBatch(table, fkName, ["id"], ["pai_id"], [[2L], [3L]], [[1L], [2L]]), ct);
        var deferredText = File.ReadAllText((await second.CompleteAsync(RunStatus.Completed, ct)).Artifacts.First(a => a.Kind == "script").Path);
        deferredText.ShouldContain("UPDATE \"categoria\" SET \"pai_id\" = 1 WHERE \"id\" = 2;");
        deferredText.ShouldContain("UPDATE \"categoria\" SET \"pai_id\" = 2 WHERE \"id\" = 3;");

        var cleanup = File.ReadAllText(result.Artifacts.Single(a => a.Kind == "cleanup").Path);
        cleanup.IndexOf("UPDATE \"categoria\" SET \"pai_id\" = NULL", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        cleanup.IndexOf("UPDATE \"categoria\" SET \"pai_id\" = NULL", StringComparison.Ordinal)
            .ShouldBeLessThan(cleanup.IndexOf("DELETE FROM", StringComparison.Ordinal), "FKs de ciclo são zeradas antes dos DELETEs");
        script.ShouldNotBeNullOrEmpty();
    }
}
