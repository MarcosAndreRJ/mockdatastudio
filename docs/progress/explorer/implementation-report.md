# Relatório de implementação — Explorer

| Campo | Valor |
|---|---|
| Branch | `feature/explorer` (worktree `K:\Trabalho\Projetos\MockDataStudio-explorer`) |
| Base | `d9f3bf9` (`feature/connections`, "feat: implementando"). A branch de Connections não foi modificada |
| Escopo entregue | **Fase A** (opção 1 aprovada): camada de serviços sobre contratos existentes, testada com SQLite real |
| Status | Fase A concluída e testada. UI, Connections e os providers PostgreSQL/MySQL/Firebird **não** entram nesta entrega |
| Data | 2026-10-12 |

## 1. Diagnóstico inicial (auditoria)

- **Tela Explorer**: `ExplorerView.xaml` preservado, sem alterações. `ExplorerViewModel` ainda usa `SampleData` (dados demonstrativos). **Não foi ligado a dados reais nesta entrega.**
- **Connections**: existem apenas os tipos de dados do Core (`ConnectionProfile`, `ConnectionSettings`, `ConnectionEnvironment`, `ProviderCapabilities`). Não há serviço de conexões, repositório de perfis nem armazenamento de credenciais. O Explorer recebe um `ConnectionProfile`/`ConnectionSettings` já montado e não lê credenciais.
- **Providers**: SQLite completo; PostgreSQL, MySQL e Firebird apenas com `.csproj` (sprints 8–11).
- **Ambiente**: Docker indisponível. Somente SQLite é testado de ponta a ponta.
- **Camadas**: `Application` e `Output` estavam vazios; `SqlTooling` estava pronto e testado.

## 2. Funcionalidades implementadas (Fase A)

| Área | Implementação | Arquivo |
|---|---|---|
| Política de execução | Matriz de decisão por natureza do comando (leitura, alteração de dados, estrutural, administrativo, desconhecido), perfil somente leitura, produção, UPDATE/DELETE sem WHERE | `Application/Explorer/ExecutionPolicy.cs` |
| Navegação sob demanda | Esquemas, tabelas, views, triggers e metadados de **uma** tabela por vez; cache por sessão com `Refresh()` | `Application/Explorer/DatabaseNavigator.cs` |
| Object Inspector | Colunas (tipo, tamanho, precisão, nulo, padrão, PK/FK/UQ/identidade), índices, UNIQUE, foreign keys com regras de atualização/exclusão, checks | `Application/Explorer/ObjectInspector.cs` |
| Preview | `SELECT` com colunas explícitas e `LIMIT` obrigatório (padrão 100, máximo 1.000) | `Application/Explorer/ObjectInspector.cs` (`TablePreview`) |
| Execução SQL | Divisão por SqlTooling, política por comando, restrição de sessão, timeout, cancelamento, mensagens estruturadas com linha do erro | `Application/Explorer/QueryService.cs` |
| Paginação | `ResultCursor` mantém o leitor aberto e entrega páginas com lookahead; não carrega o conjunto inteiro | `Application/Explorer/QueryService.cs` |
| Exportação | CSV (RFC 4180, NULL como campo vazio sem aspas) e JSON (NULL como `null`), em streaming, com cancelamento | `Output/ResultExporter.cs` |

## 3. Arquivos criados e modificados

**Modificados**
- `src/MockDataStudio.Providers.Abstractions/ISchemaExtractor.cs`: + `ListViewsAsync`, `ListTriggersAsync` (listagem leve para navegação).
- `src/MockDataStudio.Providers.Sqlite/SqliteSchemaExtractor.cs`: implementação dos dois métodos acima.
- `src/MockDataStudio.App/App.xaml.cs`: base qualificada como `System.Windows.Application` (colisão com o namespace `MockDataStudio.Application`).
- `tests/MockDataStudio.App.UiTests/SmokeTests.cs`: `FlaUI.Core.Application.Launch` qualificado (mesma colisão).
- `tests/MockDataStudio.Application.Tests/MockDataStudio.Application.Tests.csproj`: referências a Providers.Sqlite e SqlTooling.

**Criados**
- `src/MockDataStudio.Application/Explorer/` — `ExecutionPolicy.cs`, `DatabaseNavigator.cs`, `ObjectInspector.cs`, `QueryService.cs`.
- `src/MockDataStudio.Output/ResultExporter.cs`.
- `tests/MockDataStudio.Application.Tests/TestSupport/SqliteWorkbench.cs` e `Explorer/` (navegação e inspetor, serviço de consultas, política).
- `tests/MockDataStudio.Output.Tests/ResultExporterTests.cs`.
- `docs/progress/explorer/implementation-report.md` (este arquivo).

## 4. Contratos reutilizados

- `IDatabaseProvider`, `ISchemaExtractor` (`ExtractTableAsync`, `ListTablesAsync`), `ISqlDialect`, `IErrorTranslator`: usados como estão, exceto a adição de dois métodos de listagem.
- `ISqlScriptSplitter`, `SqlCommandStatement`, `StatementKind`, `SqlDialectFeatures`: SqlTooling sem alterações.
- `TableModel`, `ColumnModel`, `ObjectName`, `TriggerModel`: Core sem alterações.
- `ConnectionProfile`, `ConnectionEnvironment`: tipos de dados lidos; nenhum serviço de Connections foi criado.

## 5. Dependências com Connections

- Não há `ConnectionService` a consumir. Hoje o Explorer aceita `DbConnection` + `ExecutionPolicy` montados pelo chamador.
- **Pendente de Connections:** resolver perfil, abrir conexão com credencial, indicar estado da conexão, alternar conexões.
- **Não se duplicou** nenhum armazenamento de credenciais nem o `ConnectionService`.

## 6. Bancos efetivamente testados

| SGBD | Testado | Observação |
|---|---|---|
| SQLite | **Sim** | Arquivo real em pasta temporária; provider de produção (`SqliteProvider`) |
| PostgreSQL | **Não** | Provider inexistente |
| MySQL | **Não** | Provider inexistente |
| Firebird | **Não** | Provider inexistente |

## 7. Resultados dos testes (Release)

| Suíte | Resultado |
|---|---|
| `MockDataStudio.Application.Tests` | **37 aprovados**, 0 falhas |
| `MockDataStudio.Output.Tests` | **6 aprovados**, 0 falhas |
| `MockDataStudio.Architecture.Tests` | **10 aprovados**, 0 falhas |
| `MockDataStudio.Providers.ContractTests` | 6 aprovados, 1 ignorado. Esta branch tem uma versão **anterior** da suíte (7 testes); a suíte completa de 14 testes está na árvore principal, não commitada |
| Build `MockDataStudio.slnx -c Release` | **0 erros** |

Não foram executadas nesta worktree as suítes de Core, SqlTooling e Catalog (já verdes na árvore principal, mas não rodadas aqui).

## 8. Limitações

1. **Cancelamento de consultas que agregam em uma única etapa.** `DbCommand.Cancel()` do Microsoft.Data.Sqlite **não interrompe** uma consulta como `count(*)` sobre uma CTE infinita: a chamada não retorna. O cancelamento é efetivo quando a consulta produz linhas (verificado). Mitigação: `CommandTimeoutSec`. Registrado como **PV-019**.
2. **Views e triggers via listagem própria.** `ListViewsAsync` e `ListTriggersAsync` foram acrescentados ao contrato `ISchemaExtractor` (apenas SQLite implementado).
3. **Preview usa `LIMIT`.** Firebird usa `FIRST`/`ROWS`; sem `ISqlDialect.LimitClause`, o preview não funciona nesse SGBD. Pendente com o provider.
4. **Restrição de sessão.** A restrição somente leitura é aplicada por SQL fornecido pela composição (`PRAGMA query_only = ON` no SQLite). Sem restrição disponível, a execução é bloqueada em vez de simulada.
5. **Sem UI.** Árvore, editor, grade, mensagens e menus de contexto não foram ligados ao `ExplorerViewModel`.
6. **Sem formatação de SQL, abas, salvar/abrir scripts** nesta fase: são funções de editor, não de serviço.
7. **Sem planos de execução**, sem Excel, sem propriedades de objeto além de tabelas e views.
8. **Triggers e sequences** aparecem somente como triggers (SQLite não tem sequences nem rotinas; procedures e functions não se aplicam).

## 9. Pendências

- Fase B: ligar Connections (`ConnectionService`), ViewModels reais, árvore, editor e grade.
- Providers PostgreSQL, MySQL e Firebird (sprints 8–11) e seus testes de integração com containers.
- `ISqlDialect.LimitClause` (ou equivalente) para o preview do Firebird.
- Plano de execução (`EXPLAIN QUERY PLAN` no SQLite; demais SGBDs dependem dos providers).
- Formatação de SQL, abas, salvar e abrir scripts.
- Confirmação de produção ligada à UI (o serviço já exige `AuthorizedProduction` + `Confirmed`).

## 10. Decisões arquiteturais

- **Serviços em `Application`** (namespace `MockDataStudio.Application.Explorer`). Esse namespace colide com a classe `Application` do WPF: o projeto App qualifica `System.Windows.Application`.
- **A política é uma matriz explícita** (`ExecutionPolicy.Evaluate`), sem depender de engine. Comandos desconhecidos exigem confirmação (bloqueados em produção).
- **Perfil somente leitura exige restrição efetiva.** Sem `SessionRestriction`, a execução é bloqueada: configuração visual não conta como proteção.
- **Cancelamento** é registrado no `DbCommand` (`Cancel()`) e também verificado a cada página. Exceções ocorridas com o token acionado viram `Cancelled`.
- **Erros** usam `IErrorTranslator` (códigos `PRV-*`); mensagens não incluem a string de conexão.
- **O Explorer e o editor SQL independente devem compartilhar `QueryService`** (recomendação do prompt).

## 11. Evidências

- Testes: ver seção 7 (saídas em `docs/progress/explorer/` e nos logs do scratchpad da sessão).
- Verificação de cancelamento: diagnóstico com `Cancel()` em `count(*)` sobre CTE infinita, sem retorno (ver PV-019).

## 12. Commits

Ver histórico da branch `feature/explorer` (commit de entrega desta fase, registrado após a execução).
