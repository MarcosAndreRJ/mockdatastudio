using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Infrastructure.Storage;
using Shouldly;
using Xunit;

namespace MockDataStudio.App.UiTests;

public sealed class FlowTests
{
    private const string Schema = """
        CREATE TABLE cliente (id INTEGER PRIMARY KEY, nome TEXT NOT NULL, email TEXT UNIQUE, ativo BOOLEAN NOT NULL DEFAULT 1);
        CREATE TABLE pedido (id INTEGER PRIMARY KEY, cliente_id INTEGER NOT NULL REFERENCES cliente(id), total NUMERIC(12,2));
        """;

    // Fluxo MVP até o mapeamento: projeto novo → conexão SQLite salva → digest capturado → mapeamento automático.
    // Não cobre: salvar pelo diálogo de arquivo (lógica coberta em Application.Tests) nem navegação exclusivamente por teclado.
    [Fact]
    [Trait("Category", "UI")]
    [Trait("Id", "T-UI-FLOW-001")]
    public async Task T_UI_FLOW_001_Projeto_Conexao_Digest_Mapeamento()
    {
        var ct = TestContext.Current.CancellationToken;
        var dataDir = Path.Combine(Path.GetTempPath(), "mds-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);

        var databaseFile = Path.Combine(dataDir, "loja.db");
        await using (var connection = new SqliteConnection("Data Source=" + databaseFile))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = Schema;
            await command.ExecuteNonQueryAsync(ct);
        }

        // Perfil de conexão cadastrado antes de abrir o app (o cadastro pela tela é coberto em T-UI-SMOKE e nos testes de Connections).
        using (var store = new FileConnectionProfileStore(Path.Combine(dataDir, "connections.json")))
        {
            var profile = new ConnectionProfile(Guid.NewGuid(), "Loja SQLite", DatabaseEngine.Sqlite,
                new ConnectionSettings(null, null, databaseFile, null), ConnectionEnvironment.Development, null, true, false, null);
            await store.SaveAsync(profile, null, ct);
        }

        using var session = new AppSession(dataDir);

        session.Navigate("Home");
        session.ClickButton("Novo projeto");
        session.WaitForText("Projeto novo criado");

        session.Navigate("Schema Digest");
        session.WaitEnabled("Extrair digest", TimeSpan.FromSeconds(30)); // habilita quando a conexão salva é carregada e selecionada
        session.ClickButton("Extrair digest");
        session.WaitForText("Estrutura capturada", TimeSpan.FromSeconds(60));

        session.Navigate("Mapping");
        session.WaitForText("Tabelas (2)", TimeSpan.FromSeconds(60));
        session.ClickButton("Mapear automaticamente");
        session.WaitForText("Mapeamento automático aplicado");
    }
}
