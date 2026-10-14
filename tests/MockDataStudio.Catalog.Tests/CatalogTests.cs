using System.Text.Json;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Catalog.Packs;
using MockDataStudio.Catalog.Database;
using Shouldly;
using Xunit;

namespace MockDataStudio.Catalog.Tests;

public sealed class CatalogTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MockDataStudio.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine([dir!.FullName, .. parts]);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-001")]
    public async Task T_CAT_001_MigracaoDoZero_CriaSchemaIntegroEVersionada()
    {
        using var env = new CatalogTestEnv();

        env.Version().ShouldBe(1);
        env.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'").ShouldBe("15");
        env.Scalar("SELECT checksum FROM schema_version WHERE version = 1").Length.ShouldBe(64);

        var health = (await env.Maintenance.CheckIntegrityAsync(CancellationToken.None));
        health.IntegrityOk.ShouldBeTrue(string.Join("; ", health.Messages));
        health.SchemaVersion.ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-001")]
    public void T_CAT_001_MigracaoComFalhaInjetada_PreservaCatalogoAnterior()
    {
        using var env = new CatalogTestEnv();
        env.Scalar("SELECT COUNT(*) FROM template_pack").ShouldBe("0");
        env.Exec("INSERT INTO template_pack (code, name, created_at, updated_at) VALUES ('X1','Marcador','a','a')");

        var v1 = Migration.LoadEmbedded().Single(m => m.Version == 1);
        var broken = new Migration(2, "quebrada", "CREATE TABLE nova (a INTEGER); ALTER TABLE inexistente ADD COLUMN x INTEGER;");

        var result = env.Migrate([v1, broken]);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.CatMigFailed);
        env.Version().ShouldBe(1);
        env.Scalar("SELECT COUNT(*) FROM template_pack WHERE code = 'X1'").ShouldBe("1");
        env.Scalar("SELECT COUNT(*) FROM sqlite_master WHERE name = 'nova'").ShouldBe("0");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-001")]
    public void T_CAT_001_MigracaoAlterada_FalhaComChecksum()
    {
        using var env = new CatalogTestEnv();
        var altered = Migration.LoadEmbedded().Single(m => m.Version == 1) with { Sql = "-- alterada" + Migration.LoadEmbedded()[0].Sql };

        env.Migrate([altered]).Error.Code.ShouldBe(ErrorCodes.CatMigChecksum);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-001")]
    public void T_CAT_001_DDL_DaMigracao_IdenticoAoDocumento()
    {
        var embedded = Migration.LoadEmbedded().Single(m => m.Version == 1).Sql.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var doc = File.ReadAllText(RepoFile("docs", "09-internal-database.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = doc.IndexOf("## 3. DDL", StringComparison.Ordinal);
        var block = doc[(doc.IndexOf("```sql", start, StringComparison.Ordinal) + "```sql".Length)..];
        var ddl = block[..block.IndexOf("```", StringComparison.Ordinal)].Trim();

        embedded.ShouldBe(ddl);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-002")]
    public async Task T_CAT_002_Embutidos_NaoPodemSerExcluidosNemTerKindAlterado()
    {
        using var env = new CatalogTestEnv();
        (await env.Pack.InstallAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();

        (await env.Templates.DeleteAsync("NOME", CancellationToken.None))
            .Error.Code.ShouldBe(ErrorCodes.CatBuiltinReadOnly);

        var changed = new TemplateDraft("NOME", "Nome", TemplateKind.Composite, null, null, [], [new DraftField("a", LogicalType.String, 1)], [], [], []);
        (await env.Templates.SaveAsync(changed, CancellationToken.None))
            .Error.Code.ShouldBe(ErrorCodes.CatBuiltinReadOnly);

        var raw = Should.Throw<Microsoft.Data.Sqlite.SqliteException>(() => env.Scalar("UPDATE template SET logical_type = 'integer' WHERE code = 'NOME'"));
        raw.Message.ShouldContain(ErrorCodes.CatBuiltinReadOnly);
    }

    public static TheoryData<string, string> CasosDeStaging()
    {
        return new TheoryData<string, string>
        {
            { ErrorCodes.CatStgForbidden, "CREATE TABLE x (a INTEGER);" },
            { ErrorCodes.CatStgForbidden, "DROP TABLE template;" },
            { ErrorCodes.CatStgForbidden, "ATTACH DATABASE '/tmp/x.db' AS x;" },
            { ErrorCodes.CatStgForbidden, "PRAGMA foreign_keys = OFF;" },
            { ErrorCodes.CatStgForbidden, "SELECT * FROM template;" },
        };
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-003")]
    [MemberData(nameof(CasosDeStaging))]
    public async Task T_CAT_003_Staging_RejeitaComandosProibidos(string code, string script)
    {
        using var env = new CatalogTestEnv();

        var result = (await env.AnalyzeAsync(script));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(code);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SEC-009")]
    public async Task T_SEC_009_ScriptComDrop_EhRejeitado_SemAlterarCatalogo()
    {
        using var env = new CatalogTestEnv();
        (await env.Pack.InstallAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var before = env.Count("template");

        var result = await env.AnalyzeAsync("INSERT INTO template (code, name) VALUES ('NOVO', 'x');\nDROP TABLE template_value;");

        result.Error.Code.ShouldBe(ErrorCodes.CatStgForbidden);
        env.Count("template").ShouldBe(before);
        env.Count("template_value").ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-004")]
    public async Task T_CAT_004_CadaCodigoImp_EhProvocado()
    {
        using var env = new CatalogTestEnv();
        await env.Pack.InstallAsync(CancellationToken.None);

        var cases = new (string Code, string Script)[]
        {
            (ErrorCodes.CatImpCode, "INSERT INTO template (code, name, kind, logical_type) VALUES ('minusculo', 'x', 'scalar', 'string');"),
            (ErrorCodes.CatImpKind, "INSERT INTO template (code, name, kind, logical_type) VALUES ('NOVO_K', 'x', 'bizarro', 'string');"),
            (ErrorCodes.CatImpUnknownTemplate, "INSERT INTO template_value (template_code, value) VALUES ('NAO_EXISTE', 'x');"),
            (ErrorCodes.CatImpField, "INSERT INTO template_alias (template_code, field_code, alias) VALUES ('ENDERECO', 'campo_inexistente', 'X');"),
            (ErrorCodes.CatImpRecordJson, "INSERT INTO template_record (template_code, data) VALUES ('ENDERECO', '{\"cep\":\"1\",\"extra\":\"x\"}');"),
            (ErrorCodes.CatImpRecordJson, "INSERT INTO template_record (template_code, data) VALUES ('ENDERECO', 'nao-e-json');"),
            (ErrorCodes.CatImpBuiltin, "INSERT INTO template (code, name, kind, logical_type) VALUES ('UF', 'UF', 'scalar', 'integer');"),
            (ErrorCodes.CatImpWeight, "INSERT INTO template_value (template_code, value, weight) VALUES ('UF', 'XX', 0);"),
            (ErrorCodes.CatImpRule, "INSERT INTO mapping_rule (column_pattern, target_kind, target_ref, priority) VALUES ('X*', 'template', 'NAO_EXISTE', 100);"),
            (ErrorCodes.CatImpValueType, "INSERT INTO template (code, name, kind, logical_type) VALUES ('NUMERO_TESTE', 'n', 'scalar', 'integer'); INSERT INTO template_value (template_code, value) VALUES ('NUMERO_TESTE', 'abc');"),
            (ErrorCodes.CatImpLength, "INSERT INTO template_record (template_code, data) VALUES ('ENDERECO', '{\"cep\":\"123456789\",\"logradouro\":\"x\",\"bairro\":\"x\",\"cidade\":\"x\",\"uf\":\"SP\"}');"),
            (ErrorCodes.CatImpDupValue, "INSERT INTO template_value (template_code, value) VALUES ('UF', 'SP');"),
        };

        foreach (var (code, script) in cases)
        {
            var analysis = await env.AnalyzeAsync(script);
            analysis.IsSuccess.ShouldBeTrue(analysis.IsSuccess ? string.Empty : analysis.Error.Message);
            analysis.Value.Issues.Select(i => i.Code).ShouldContain(code, $"Esperado {code} para: {script}");
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-004")]
    public async Task T_CAT_004_ExemploOficial_AnalisaSemErros()
    {
        using var env = new CatalogTestEnv();
        var script = File.ReadAllText(RepoFile("docs", "examples", "catalog-import-example.sql"));

        var analysis = await env.AnalyzeAsync(script);

        analysis.IsSuccess.ShouldBeTrue(analysis.IsSuccess ? string.Empty : analysis.Error.Message);
        analysis.Value.Issues.Where(i => i.Severity == IssueSeverity.Error).ShouldBeEmpty();
        analysis.Value.CanMerge.ShouldBeTrue();
        analysis.Value.TemplatesNew.ShouldBe(3); // TELEFONE, STATUS_PEDIDO, ENDERECO (docs/examples/catalog-import-example.sql)
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-005")]
    public async Task T_CAT_005_ModosDeMerge_ComportamentoPorModo()
    {
        using var env = new CatalogTestEnv();
        await env.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('CIDADE_T', 'Cidade', 'scalar', 'string');" +
            "INSERT INTO template_value (template_code, value, weight) VALUES ('CIDADE_T', 'A', 1), ('CIDADE_T', 'B', 1);", MergeMode.InsertOnly);

        // InsertOnly: nome do template existente é mantido e valores novos são acrescentados.
        await env.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('CIDADE_T', 'Outro nome', 'scalar', 'string');" +
            "INSERT INTO template_value (template_code, value, weight) VALUES ('CIDADE_T', 'C', 1);", MergeMode.InsertOnly);
        env.Scalar("SELECT name FROM template WHERE code = 'CIDADE_T'").ShouldBe("Cidade");
        env.Scalar("SELECT COUNT(*) FROM template_value v JOIN template t ON t.id = v.template_id WHERE t.code = 'CIDADE_T'").ShouldBe("3");

        // Upsert: nome atualizado; kind/logical_type inalterados.
        await env.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('CIDADE_T', 'Cidade BR', 'scalar', 'string');", MergeMode.Upsert);
        env.Scalar("SELECT name FROM template WHERE code = 'CIDADE_T'").ShouldBe("Cidade BR");

        // ReplaceTemplateData: substitui todos os valores do template.
        await env.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('CIDADE_T', 'Cidade BR', 'scalar', 'string');" +
            "INSERT INTO template_value (template_code, value, weight) VALUES ('CIDADE_T', 'Z', 1);", MergeMode.ReplaceTemplateData);
        env.Scalar("SELECT COUNT(*) FROM template_value v JOIN template t ON t.id = v.template_id WHERE t.code = 'CIDADE_T'").ShouldBe("1");
        env.Scalar("SELECT value FROM template_value v JOIN template t ON t.id = v.template_id WHERE t.code = 'CIDADE_T'").ShouldBe("Z");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-006")]
    public async Task T_CAT_006_BackupERestauracao_RecuperamEstado()
    {
        using var env = new CatalogTestEnv();
        await env.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('BKP_T', 'Backup', 'scalar', 'string');");

        var backup = await env.Maintenance.BackupAsync(null, CancellationToken.None);
        await env.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('DEPOIS_T', 'Depois', 'scalar', 'string');");
        env.Scalar("SELECT COUNT(*) FROM template WHERE code = 'DEPOIS_T'").ShouldBe("1");

        var restored = await env.Maintenance.RestoreAsync(backup, CancellationToken.None);

        restored.IsSuccess.ShouldBeTrue();
        env.Scalar("SELECT COUNT(*) FROM template WHERE code = 'BKP_T'").ShouldBe("1");
        env.Scalar("SELECT COUNT(*) FROM template WHERE code = 'DEPOIS_T'").ShouldBe("0");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-007")]
    public async Task T_CAT_007_IntegrityChecker_DesabilitaRegraInvalida_SemExcluir()
    {
        using var env = new CatalogTestEnv();
        env.Exec("INSERT INTO mapping_rule (column_pattern, target_kind, target_ref, source, scope, priority, enabled, created_at, updated_at) " +
                 "VALUES ('ZZ*', 'template', 'NAO_EXISTE', 'user', 'global', 100, 1, 'a', 'a')");

        var health = await env.Maintenance.CheckIntegrityAsync(CancellationToken.None);

        health.Messages.ShouldContain(m => m.Contains("desabilitada", StringComparison.Ordinal));
        env.Scalar("SELECT enabled FROM mapping_rule WHERE column_pattern = 'ZZ*'").ShouldBe("0");
        env.Scalar("SELECT COUNT(*) FROM mapping_rule WHERE column_pattern = 'ZZ*'").ShouldBe("1");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-009")]
    public async Task T_CAT_009_ContentHash_IndependeDaOrdemDeInsercao()
    {
        using var first = new CatalogTestEnv();
        using var second = new CatalogTestEnv();

        await first.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('ORD_T', 'Ordem', 'scalar', 'string');" +
            "INSERT INTO template_value (template_code, value, weight) VALUES ('ORD_T', 'b', 1), ('ORD_T', 'a', 2), ('ORD_T', 'c', 1);");
        await second.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('ORD_T', 'Ordem', 'scalar', 'string');" +
            "INSERT INTO template_value (template_code, value, weight) VALUES ('ORD_T', 'c', 1), ('ORD_T', 'a', 2), ('ORD_T', 'b', 1);");

        var h1 = (await first.DataSource.LoadAsync("ORD_T", CancellationToken.None)).ContentHash;
        var h2 = (await second.DataSource.LoadAsync("ORD_T", CancellationToken.None)).ContentHash;

        h1.ShouldBe(h2);
        h1.ShouldStartWith("sha256:");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-009")]
    public async Task T_CAT_009_FonteDeDados_EntregaOrdenadoComPesos()
    {
        using var env = new CatalogTestEnv();
        await env.ImportAsync("INSERT INTO template (code, name, kind, logical_type) VALUES ('PESO_T', 'Peso', 'scalar', 'string');" +
            "INSERT INTO template_value (template_code, value, weight) VALUES ('PESO_T', 'zeta', 3), ('PESO_T', 'alfa', 1);");

        var set = await env.DataSource.LoadAsync("PESO_T", CancellationToken.None);

        set.Kind.ShouldBe(TemplateKind.Scalar);
        set.Count.ShouldBe(2);
        set.GetScalar(0).ShouldBe("alfa");
        set.GetWeight(1).ShouldBe(3);
        set.HasNonUniformWeights.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-010")]
    public async Task T_CAT_010_PackEmbutido_InstalaESegundaInstalacaoEIdempotente()
    {
        using var env = new CatalogTestEnv();

        var first = await env.Pack.InstallAsync(CancellationToken.None);
        first.IsSuccess.ShouldBeTrue(first.IsSuccess ? string.Empty : first.Error.Message);
        first.Value.TemplatesNew.ShouldBe(6);

        env.Count("template_value").ShouldBeGreaterThan(0);
        env.Count("template_record").ShouldBe(200);
        env.Scalar("SELECT COUNT(*) FROM template_value v JOIN template t ON t.id = v.template_id WHERE t.code = 'NOME'").ShouldBe("200");
        env.Scalar("SELECT COUNT(*) FROM template WHERE is_builtin = 1").ShouldBe("6");

        var snapshot = (env.Count("template"), env.Count("template_value"), env.Count("template_alias"), env.Count("template_record"), env.Count("template_field"));

        var second = await env.Pack.InstallAsync(CancellationToken.None);
        second.IsSuccess.ShouldBeTrue();

        (env.Count("template"), env.Count("template_value"), env.Count("template_alias"), env.Count("template_record"), env.Count("template_field"))
            .ShouldBe(snapshot);
        second.Value.TemplatesNew.ShouldBe(0);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-CAT-010")]
    public void T_CAT_010_PackEmbutido_ContemFontesEContagensMvp()
    {
        using var stream = typeof(MockDataStudio.Catalog.Packs.BuiltInPackInstaller).Assembly.GetManifestResourceStream("MockDataStudio.Catalog.Seeds.pack-core-ptbr.json")!;
        using var document = JsonDocument.Parse(stream);
        var counts = document.RootElement.GetProperty("templates").EnumerateArray()
            .ToDictionary(t => t.GetProperty("code").GetString()!,
                t => t.TryGetProperty("values", out var v) ? v.GetArrayLength() : t.GetProperty("records").GetArrayLength());

        counts["NOME"].ShouldBe(200);
        counts["SOBRENOME"].ShouldBe(150);
        counts["EMAIL_DOMINIO"].ShouldBe(20);
        counts["UF"].ShouldBe(27);
        counts["ENDERECO"].ShouldBe(200);
        File.Exists(RepoFile("src", "MockDataStudio.Catalog", "Seeds", "SOURCES.md")).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Repositorios_FeedbackEExecucoes_PersistemEConsultam()
    {
        using var env = new CatalogTestEnv();

        await env.Feedback.RecordAsync("NOMECLIENTE", RuleTargetKind.Template, "NOME", accepted: true, CancellationToken.None);
        await env.Feedback.RecordAsync("NOMECLIENTE", RuleTargetKind.Template, "NOME", accepted: true, CancellationToken.None);
        await env.Feedback.RecordAsync("NOMECLIENTE", RuleTargetKind.Template, "NOME", accepted: false, CancellationToken.None);
        var feedback = await env.Feedback.ListAsync("NOMECLIENTE", CancellationToken.None);
        feedback.Count.ShouldBe(1);
        feedback[0].AcceptedCount.ShouldBe(2);
        feedback[0].RejectedCount.ShouldBe(1);

        var started = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
        await env.Runs.StartAsync(new GenerationRunInfo("run-1", "proj-1", null, "perfil", started, null, "running", "42",
            "plan", "struct", "sqlScript", null, null, 0, null, null), CancellationToken.None);
        await env.Runs.FinishAsync("run-1", "completed", started.AddMinutes(1), 1000, "{\"ok\":true}", CancellationToken.None);
        var runs = await env.Runs.ListAsync("proj-1", 10, CancellationToken.None);
        runs.Count.ShouldBe(1);
        runs[0].Status.ShouldBe("completed");
        runs[0].RowsTotal.ShouldBe(1000);
        runs[0].ReportJson.ShouldBe("{\"ok\":true}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GeneratorPreset_UpsertFalhaAoTentarAlterarEmbutido()
    {
        using var env = new CatalogTestEnv();
        var ct = CancellationToken.None;

        // Insere preset embutido
        var builtInPreset = new GeneratorPresetInfo(0, "PRESET_BUILTIN", "Builtin", "core.dummy", 1, "{}", null, true);
        var res1 = await env.Presets.UpsertAsync(builtInPreset, ct);
        res1.IsSuccess.ShouldBeTrue();

        // Tenta alterar o preset embutido
        var attemptUpdate = new GeneratorPresetInfo(0, "PRESET_BUILTIN", "Alterado", "core.dummy", 2, "{\"x\":1}", null, false);
        var res2 = await env.Presets.UpsertAsync(attemptUpdate, ct);

        res2.IsSuccess.ShouldBeFalse("não deve permitir alterar preset embutido");
        res2.Error.Code.ShouldBe(ErrorCodes.CatBuiltinReadOnly);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Staging_AnaliseGeraListaDeTemplatesComConflitos()
    {
        using var env = new CatalogTestEnv();
        var ct = CancellationToken.None;

        var script = """
            INSERT INTO template (code, name, kind, logical_type, locale, description) VALUES ('TST_STG_1', 'Staging 1', 'scalar', 'string', 'pt-BR', 'Desc');
            INSERT INTO template_value (template_code, value, weight) VALUES ('TST_STG_1', 'val1', 1.0);
            """;

        var analysis = await env.Importer.AnalyzeAsync(new StagingImportRequest(script, MergeMode.Upsert, null), null, ct);
        analysis.IsSuccess.ShouldBeTrue();
        analysis.Value.Templates.ShouldNotBeNull();
        analysis.Value.Templates.Count.ShouldBe(1);

        var t1 = analysis.Value.Templates[0];
        t1.Code.ShouldBe("TST_STG_1");
        t1.ItemCount.ShouldBe(1);
        t1.ExistsInCatalog.ShouldBeFalse();
        t1.HasConflict.ShouldBeFalse();
    }
}
