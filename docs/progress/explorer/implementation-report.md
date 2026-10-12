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

## 13. Explorer completo (sprint `sprint/EXP-explorer-completo`, 2026-10-12)

### 13.1 O que foi implementado

| Item do pedido | Situação | Onde |
|---|---|---|
| Busca/filtro da árvore | Feito (sobre o catálogo carregado; sem nova consulta ao banco) | `ExplorerFilter`, `ExplorerViewModel.RebuildTree` |
| Referências de entrada ("quem referencia") | Feito (SQLite verificado) | `ReferenceFinder`, `ExplorerViewModel.LoadReferencesAsync` |
| EXPLAIN | Feito para SQLite (`EXPLAIN QUERY PLAN`); PostgreSQL/MySQL montados mas **não verificados**; Firebird sem suporte | `ExplainService`, `SqlEditorViewModel.ExplainAsync` |
| Formatação de SQL | Feito (heurístico, verificado por teste de tokens e idempotência) | `SqlFormatter` |
| Salvar/abrir script (UTF-8) | Feito (testado sem UI) | `ScriptFileService`, `SqlEditorViewModel` |
| Exportar grade (CSV/JSON) | Feito para as linhas exibidas (primeira página) | `ResultExportService`, `SqlResultsViewModel.ExportAsync` |
| Aba de plano na grade | Feito (mesmo grid do resultado) | `SqlResultsViewModel.ShowPlan` |
| Barra de estado (conexão, ambiente, somente leitura aplicada) | Modelo pronto e testado; **sem elemento visual** na tela | `ConnectionStatus`, `SqlEditorViewModel.StatusBar` |
| Remoção da dependência de dados de exemplo no Explorer | Feito (`ColumnRow` de `App.Samples` substituído por `ExplorerColumnRow`) | `ExplorerViewModels.cs` |

### 13.2 Testes

- `ExplorerToolsTests` (20 casos, `MockDataStudio.Application.Tests`): filtro, formatador (literais, idempotência, tokens), status da conexão, exportação CSV/JSON, script UTF-8, referências e EXPLAIN sobre SQLite real, recusa de comandos que não são leitura, Firebird sem EXPLAIN.
- Suíte completa (Release): ver o relatório da sprint no PLANO-DE-ACAO.md (seção C5/C6).
- Interface (XAML e ViewModels do App): compila em Release; o smoke de UI existente passou. Não há testes FlaUI novos para as telas.

### 13.3 Não verificado

- EXPLAIN em PostgreSQL e MySQL: não executado (sem Docker nesta máquina).
- Qualquer caminho com PostgreSQL: a árvore do Explorer depende da B2 (extrator), que ainda não foi mesclada.
- Firebird: o perfil somente leitura **não é aplicado no servidor**. A barra de estado mostra o aviso; a execução não tem essa proteção (pendência registrada).

### 13.4 Pendências (não feitas nesta sprint)

1. Árvore lazy e virtualizada por nível (hoje a carga lista schemas, tabelas e views de uma vez, e não há triggers/sequences/rotinas).
2. Refresh por nó (hoje só o refresh geral).
3. Abas Indexes, Foreign Keys (com navegação) e Constraints (UNIQUE/CHECK), e Preview funcional no inspetor (hoje só Columns tem dados).
4. Menu de contexto: SELECT TOP/LIMIT, copiar nome qualificado, gerar DDL/INSERT, abrir no SQL Editor, ver referências (a ação existe no ViewModel, mas não há botão no XAML).
5. Autocomplete (C6.2): não iniciado.
6. Run da seleção, modos Manual/AutoCommit e `QuerySession` próprio.
7. Exportação completa por cursor (hoje só a primeira página exibida) e histórico de execuções.
8. Erros com linha e posição destacadas no editor.
9. Seletor de conexões no card do Explorer e reconexão automática com mensagem clara.
10. Cancelamento nas cargas do Explorer (hoje usam `CancellationToken.None`) e conexão dedicada para o editor, se o `Gate` causar travamento.
11. Barra de estado visível na tela (o modelo existe; falta o elemento no XAML).
12. Confirmação de produção com digitação da palavra (C5.2): hoje a confirmação é `MessageBox` sim/não.
13. `ExecutionGuard` como componente único (C5.1).

## 14. Explorer completo, rodada 2 (2026-10-12)

Base: `main` incorporada antes do trabalho (B2 PostgreSQL, A2 planejador, C1 infraestrutura). A base foi verificada verde antes das mudanças.

### 14.1 O que foi feito nesta rodada

| Item | Situação | Onde | Teste |
|---|---|---|---|
| Barra de estado visível (conexão, ambiente, somente leitura efetiva) | Feito no rodapé do editor; borda superior de 3 px (vermelha em Production, âmbar em Unknown) | `SqlEditorPageView.xaml`, `ConnectionStatus.ReadOnlyLabel` | `ExplorerToolsTests` (rótulo e aviso) |
| Menu de contexto da árvore | Feito: SELECT com limite, abrir no editor, copiar nome qualificado, CREATE TABLE, INSERT de exemplo, ver referências | `ExplorerView.xaml` (ItemContainerStyle), `ExplorerViewModel` | `ObjectScriptsTests` (DDL e INSERT executado no SQLite) |
| Scripts gerados do TableModel real | Feito (`ObjectScripts`), com o dialeto para citação e literais | `ObjectScripts.cs` | 2 testes SQLite (INSERT executado e desfeito) |
| Inspetor: dados de Indexes, Foreign Keys e Constraints | Dados prontos; navegação pela FK (`OpenReferencedTableCommand`) | `ExplorerViewModel` | Coberto pelo ObjectInspector existente |
| Confirmação de produção | `ExecutionGuard` decide o nível (nenhum, simples ou digitado); `MessageBox` substituído por `ConfirmDialog`/`TypedConfirmDialog` | `ExecutionGuard.cs`, `ExplorerViewModels.cs` | `ExecutionGuardTests` (5 casos, T-SQL-GUARD-001) |

### 14.2 Testes desta rodada

- Novos nesta rodada: `ObjectScriptsTests` (2), `ExecutionGuardTests` (5, incluindo a matriz em `InlineData`). `ExplorerToolsTests` (20) foi ajustado para a barra de estado.
- Suíte de Application: 91 aprovados, 0 falhas (inclui os testes da rodada 1 e a política de execução).
- Suíte completa, Release: todos os projetos verdes **exceto o smoke de UI** (ver 14.3).

### 14.3 Smoke de UI (App.UiTests): intermitente, não resolvido

- A falha aparece em rodadas diferentes e em telas diferentes: `Connections`, `Templates` e `Explorer`, com a mensagem "não exibiu o texto esperado". Passa em outras rodadas.
- Já falhava na `main` antes desta sprint (ver PV-102 em `docs/32`). Não há evidência de que a causa seja o conteúdo das telas.
- Mudança feita nesta rodada: o `Thread.Sleep(1200)` fixo virou espera ativa pelo marcador (até 10 s). A falha continuou intermitente.
- Causa provável não confirmada: a localização do item de navegação (`ByName`) e o tempo de renderização sob carga do UIA. Não foi investigada além disso.
- **Consequência:** a suíte completa não ficou 100% verde de forma reprodutível nesta rodada.

### 14.4 Não verificado

- Árvore do Explorer em PostgreSQL: depende de `ListViewsAsync`/`ListTriggersAsync` do extrator B2, que estão implementados no contrato `ISchemaExtractor`, mas **não foram executados contra servidor** (sem Docker). Nenhuma mudança na Trilha B foi necessária.
- EXPLAIN em PostgreSQL e MySQL: não executado.
- Firebird: o perfil somente leitura **não é aplicado no servidor**. A barra de estado avisa.
- Testes FlaUI de barra de estado e de menu de contexto: **não escritos**. O smoke existente não cobre esses elementos.

### 14.5 Pendências (ainda não feitas)

1. Árvore lazy e virtualizada por nível, com triggers, sequences e rotinas na árvore e estado de carregamento/erro por nó. Cargas ainda usam `CancellationToken.None`.
2. Refresh por nó.
3. Abas Indexes, Foreign Keys e Constraints no XAML (os dados existem no ViewModel; as abas do XAML ainda são estáticas) e aba Preview funcional.
4. Run da seleção; modos Manual e AutoCommit; `QuerySession` próprio.
5. Erros com linha e posição destacadas no editor; histórico de execuções; abas por conexão com recuperação.
6. Autocomplete básico em `SqlTooling/Completion` (C6.2, posse da Trilha C).
7. Exportação completa por cursor (hoje só a primeira página), com cancelamento, e "carregar mais" na grade.
8. Seletor de conexões no card do Explorer e reconexão com mensagem clara.
9. Conexão dedicada para o editor, se o `ActiveConnectionSession.Gate` causar travamento.
10. Barra de estado também na página do Explorer (hoje só no editor).
11. `ExecutionAuthorization` com fingerprint/PlanHash e validade de 10 minutos (C5.2); linhas de Explorer da matriz (inserção direta, limpeza, L4) em C5.1.
12. Smoke de UI intermitente (ver 14.3).
