using MockDataStudio.Application.Explorer;
using Shouldly;
using Xunit;

namespace MockDataStudio.Application.Tests.Explorer;

// C6r.3 (recuperação de abas): gravação atômica e leitura tolerante.
public sealed class EditorTabStoreTests
{
    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mds-tabs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GravarELerPreservaAsAbas()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new EditorTabStore(Path.Combine(NewFolder(), "tabs.json"));
        var tabs = new List<TabSnapshot>
        {
            new(1, Guid.Parse("11111111-1111-1111-1111-111111111111"), "loja", "select 1;", null),
            new(2, null, null, "select 'ação';", "C:\\scripts\\teste.sql"),
        };

        await store.SaveAsync(tabs, ct);
        var back = await store.LoadAsync(ct);

        back.ShouldBe(tabs);
        File.Exists(store.Path + ".tmp").ShouldBeFalse("a gravação atômica não deixa temporário");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ArquivoAusente_DevolveListaVazia()
    {
        var store = new EditorTabStore(Path.Combine(NewFolder(), "nao-existe.json"));

        (await store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ArquivoCorrompido_DevolveListaVaziaSemLancar()
    {
        var path = Path.Combine(NewFolder(), "tabs.json");
        await File.WriteAllTextAsync(path, "{ isto não é json", TestContext.Current.CancellationToken);
        var store = new EditorTabStore(path);

        (await store.LoadAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }
}
