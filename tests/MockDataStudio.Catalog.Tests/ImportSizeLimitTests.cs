using MockDataStudio.Catalog.Staging;
using MockDataStudio.Core.Common;
using Shouldly;
using Xunit;

namespace MockDataStudio.Catalog.Tests;

public sealed class ImportSizeLimitTests
{
    // Aloca ~200 MB de texto: fora da suíte rápida (Category=Slow).
    [Fact]
    [Trait("Category", "Slow")]
    [Trait("Id", "T-CAT-IMP-SIZE-001")]
    public async Task Analyze_ScriptAbove200MB_FailsWithCatImpSize()
    {
        using var env = new CatalogTestEnv();
        var script = new string('x', 200 * 1024 * 1024 + 1);

        var result = await env.Importer.AnalyzeAsync(
            new StagingImportRequest(script, MergeMode.InsertOnly, null), null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ErrorCodes.CatImpSize);
    }
}
