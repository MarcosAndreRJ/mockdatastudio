using System.Text;
using System.Text.Json;
using MockDataStudio.Output;
using Shouldly;
using Xunit;

namespace MockDataStudio.Output.Tests;

// Exportação de resultados (prompt, seção 9): NULL, encoding, tipos e streaming.
public sealed class ResultExporterTests
{
    private static async IAsyncEnumerable<object?[]> Rows(params object?[][] rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            await Task.Yield();
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Csv_PreservaNullComoCampoVazioSemAspas()
    {
        using var stream = new MemoryStream();
        var written = await ResultExporter.WriteCsvAsync(stream, ["id", "nome"],
            Rows([1L, null], [2L, ""]), TestContext.Current.CancellationToken);

        written.ShouldBe(2);
        var text = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
        text.ShouldBe("\"id\",\"nome\"\n1,\n2,\"\"\n");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Csv_EscapaSeparadorAspasEQuebraDeLinha()
    {
        using var stream = new MemoryStream();
        await ResultExporter.WriteCsvAsync(stream, ["texto"],
            Rows(["a,b"], ["diz \"oi\""], ["linha1\nlinha2"]), TestContext.Current.CancellationToken);

        var text = Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
        text.ShouldContain("\"a,b\"");
        text.ShouldContain("\"diz \"\"oi\"\"\"");
        text.ShouldContain("\"linha1\nlinha2\"");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Csv_UsaUtf8SemBomEPreservaAcentos()
    {
        using var stream = new MemoryStream();
        await ResultExporter.WriteCsvAsync(stream, ["cidade"], Rows(["São Paulo"]), TestContext.Current.CancellationToken);

        var bytes = stream.ToArray();
        bytes.Take(3).ToArray().ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).ShouldContain("São Paulo");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Json_PreservaNullTiposNumericosEDatas()
    {
        using var stream = new MemoryStream();
        var date = new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Unspecified);
        await ResultExporter.WriteJsonAsync(stream, ["id", "valor", "ativo", "criado", "nota"],
            Rows([1L, 12.50m, true, date, null]), TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(stream.ToArray());
        var row = document.RootElement[0];
        row.GetProperty("id").GetInt64().ShouldBe(1L);
        row.GetProperty("valor").GetRawText().ShouldBe("12.50");
        row.GetProperty("ativo").GetBoolean().ShouldBeTrue();
        row.GetProperty("criado").GetString().ShouldBe("2026-10-12T08:30:00.0000000");
        row.GetProperty("nota").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Json_ArrayVazioEValido()
    {
        using var stream = new MemoryStream();
        var count = await ResultExporter.WriteJsonAsync(stream, ["a"], Rows(), TestContext.Current.CancellationToken);

        count.ShouldBe(0);
        Encoding.UTF8.GetString(stream.ToArray()).ShouldBe("[]");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Exportacao_CancelamentoInterrompeAEscrita()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var stream = new MemoryStream();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            ResultExporter.WriteCsvAsync(stream, ["a"], Rows([1L]), cts.Token));
    }
}
