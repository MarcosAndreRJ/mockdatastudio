using System.Text.Json;
using MockDataStudio.Catalog;
using MockDataStudio.Catalog.Editing;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using Shouldly;
using Xunit;

namespace MockDataStudio.Catalog.Tests;

// C8a.3 (packs JSON): exportação dos templates do usuário e reimportação num catálogo limpo.
public sealed class TemplatePackExportTests : IDisposable
{
    private readonly CatalogTestEnv _source = new();

    public void Dispose() => _source.Dispose();

    private async Task<string> ExportAsync(IReadOnlyList<string>? codes = null) =>
        (await new TemplatePackExporter(_source.Templates, _source.DataSource)
            .ExportAsync("TST_EXPORT", "Pack de teste", "Autor de teste", codes, TestContext.Current.CancellationToken))
        .Value!;

    private async Task SeedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var scalar = new TemplateEditor(_source.Templates, _source.DataSource);
        await scalar.SaveScalarAsync(new ScalarTemplateForm("TST_PACK_UF", "UF", "Sigla", LogicalType.String,
            ["SP", "RJ"], ["estado"]), ct);

        var composite = new CompositeTemplateEditor(_source.Templates, _source.DataSource);
        var fields = CompositeTemplateEditor.ParseFields("rua | string | 120 | sim\nnumero | integer | | não").Value!;
        var records = CompositeTemplateEditor.ParseRecords("Rua A | 10\nRua B |", fields).Value!;
        await composite.SaveAsync(new CompositeTemplateForm("TST_PACK_END", "Endereço", null, fields, ["via"], records), ct);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task ExportaOFormatoDoPackEOsTemplatesDoUsuario()
    {
        await SeedAsync();

        var json = await ExportAsync();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("format").GetString().ShouldBe("template-pack-1.0");
        root.GetProperty("code").GetString().ShouldBe("TST_EXPORT");
        var codes = root.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("code").GetString()).ToList();
        codes.ShouldContain("TST_PACK_UF");
        codes.ShouldContain("TST_PACK_END");
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task EmbutidosNaoSaoExportados()
    {
        var ct = TestContext.Current.CancellationToken;
        await _source.Pack.InstallAsync(ct);

        var json = await ExportAsync();

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("templates").EnumerateArray()
            .Select(t => t.GetProperty("code").GetString())
            .ShouldNotContain("NOME");
    }

    [Fact]
    [Trait("Category", "Contract")]
    [Trait("Id", "T-CAT-008")]
    public async Task PackExportadoEReimportadoPreservaConteudo()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync();
        var json = await ExportAsync(["TST_PACK_UF", "TST_PACK_END"]);

        using var target = new CatalogTestEnv();
        var analysis = await new TemplateImportService(target.Importer).AnalyzePackJsonAsync(json, MergeMode.Upsert, ct);
        analysis.IsSuccess.ShouldBeTrue(analysis.IsSuccess ? string.Empty : analysis.Error.Message);
        analysis.Value!.CanMerge.ShouldBeTrue();
        (await new TemplateImportService(target.Importer).MergeAsync(analysis.Value.StagingSessionId, ct)).IsSuccess.ShouldBeTrue();

        var uf = await target.Templates.GetAsync("TST_PACK_UF", ct);
        uf!.ValueCount.ShouldBe(2);
        var values = await new TemplateEditor(target.Templates, target.DataSource).LoadScalarAsync("TST_PACK_UF", ct);
        values.Value!.Values.OrderBy(v => v, StringComparer.Ordinal).ShouldBe(["RJ", "SP"]);

        var end = await new CompositeTemplateEditor(target.Templates, target.DataSource).LoadAsync("TST_PACK_END", ct);
        end.IsSuccess.ShouldBeTrue(end.IsSuccess ? string.Empty : end.Error.Message);
        end.Value!.Records.Count.ShouldBe(2);
        end.Value.Records.Select(r => r["rua"]).OrderBy(v => v, StringComparer.Ordinal).ShouldBe(["Rua A", "Rua B"]);
        end.Value.Fields.Select(f => f.FieldCode).ShouldBe(["rua", "numero"]);
        end.Value.Fields.Single(f => f.FieldCode == "rua").Required.ShouldBeTrue();
        end.Value.Fields.Single(f => f.FieldCode == "numero").Required.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task CodigoDePackInvalidoEhRecusado()
    {
        var exported = await new TemplatePackExporter(_source.Templates, _source.DataSource)
            .ExportAsync("minusculo", "x", "y", null, TestContext.Current.CancellationToken);

        exported.IsSuccess.ShouldBeFalse();
    }
}
