# Plano de Ação — Mock Data Studio

| Campo | Valor |
|---|---|
| Objetivo | Sequência de execução em sprints, do estado atual (protótipo de UI) até a V1. |
| Base | Backlog detalhado em [docs/28-implementation-backlog.md](docs/28-implementation-backlog.md) (cada etapa cita a `TASK-xx.yy` correspondente) |
| Regras da IA executora | [docs/33-executor-instructions.md](docs/33-executor-instructions.md) |
| Definição de concluído | [docs/29-acceptance-criteria.md §1](docs/29-acceptance-criteria.md) |
| Criado em | 2026-10-12 |

---

## 🤖 Instruções obrigatórias para a IA executora

1. **Execute os sprints na ordem** em que aparecem. Dentro de um sprint, siga a ordem das etapas, salvo quando a etapa disser que pode correr em paralelo.
2. **Ao concluir cada etapa, marque o checkbox** trocando `- [ ]` por `- [x]` e preencha os campos da própria linha:
   `Concluído em: AAAA-MM-DD · Evidência: <link/arquivo/resumo>`.
3. **Só marque `[x]` quando a etapa estiver realmente concluída**: build Release sem warnings, testes da etapa executados e passando, documentação afetada atualizada. Nunca marque etapas parciais.
4. Se a etapa estiver **bloqueada**, deixe `[ ]` e acrescente `⚠ BLOQUEADO: <motivo>` na linha. Registre o bloqueio em [docs/32](docs/32-decisions-and-assumptions.md).
5. Se a etapa foi feita só em parte, deixe `[ ]` e acrescente `⏳ PARCIAL: <o que falta>`.
6. Ao fim de cada sprint, marque os itens de **"Verificação do sprint"** e preencha o **"Relatório do sprint"**: o que foi feito, testes (passados/falhos/ignorados), decisões registradas, pendências e riscos.
7. Não comece um sprint enquanto a verificação do sprint anterior não estiver toda marcada, salvo autorização explícita do responsável.
8. Tudo o que **não** foi verificado deve ser declarado como não verificado. Nunca afirme sucesso sem evidência.

### Legenda
`- [ ]` pendente · `- [x]` concluído · `⚠ BLOQUEADO` · `⏳ PARCIAL`

---

## Estado atual (ponto de partida)

| Item | Situação em 2026-10-12 |
|---|---|
| Especificação | Completa em `docs/` |
| Solução | `MockDataStudio.sln` com **um único projeto**: `src/MockDataStudio.App` (WPF, net10.0-windows, AvalonEdit 6.3.0.90). Compila sem erros nem warnings |
| Interface | Protótipo construído a partir dos mockups (`images/`). Telas prontas: Home, Connections, Explorer (reaproveitada para "SQL Editor"), Schema Digest, Templates, Mapping. Generation, History e Settings são `PlaceholderView` |
| Dados da UI | Fixos, em `Samples/SampleData.cs`, ligados por `x:Static` no XAML. Sem MVVM, sem injeção de dependência (DI), navegação em code-behind |
| Backend | Inexistente |
| Controle de versão | Ainda **não** é repositório Git |

**Diretriz para a UI existente:** preservar o visual dos mockups. As telas passam a ser alimentadas por ViewModels, e o `SampleData` só sai quando a respectiva fonte real existir. A UI nunca deve ficar quebrada entre sprints.

---

## Visão geral dos sprints

| Sprint | Tema | Marco |
|---|---|---|
| 0 | Fundação do repositório e alinhamento da UI existente | M0 |
| 1 | Base MVVM/DI da interface | M0 |
| 2 | Modelo canônico e Schema Digest | M1 |
| 3 | SqlTooling e provider SQLite | M1 |
| 4 | Catálogo interno | M2 |
| 5 | Mapeamento semântico | M2 |
| 6 | Motor de geração | M3 |
| 7 | Planejamento relacional e validação | M3 |
| 8 | Saída SQL + providers PostgreSQL e Firebird | M4 |
| 9 | Projeto, Application e Infraestrutura | M4 |
| 10 | Integração da UI (fluxo MVP) e **Gate MVP** | **MVP** |
| 11 | MySQL e segurança de conexões | M5 |
| 12 | Explorer real | M5 |
| 13 | Editor SQL | M6 |
| 14 | Projetos avançados e evolução de schema | M7 |
| 15 | Inserção direta | M8 |
| 16 | Relacional avançado e validação L4 | M8 |
| 17 | Catálogo V1 | M9 |
| 18 | Volume e formatos | M10 |
| 19 | Hardening, acessibilidade e release — **Gate V1** | **V1** |

---

## Sprint 0 — Fundação do repositório e alinhamento da UI existente

**Objetivo:** transformar o protótipo numa base versionada, com a estrutura de projetos, as regras de build e as verificações automáticas da especificação, **sem alterar o comportamento visual**.
**Referências:** docs/07, docs/05 §3, docs/26 §2–3, docs/28 (EPIC-00).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **0.1** Inicializar Git (`git init`), criar `.gitignore` para .NET/VS (bin/obj, `.vs`, `*.user`, `Thumbs.db`) e fazer o commit inicial do estado atual. · Concluído em: ____ · Evidência: ____
- [ ] **0.2** Inventariar a UI: produzir `docs/ui-inventory.md` relacionando cada view/controle ao S01–S19 de [docs/21](docs/21-user-interface-ux.md), com as diferenças (ex.: "SQL Editor" usa `ExplorerView`; logos têm MariaDB e não MySQL; navegação em inglês × UI pt-BR especificada) e a decisão para cada uma. Registrar as decisões como DA em docs/32. · Concluído em: ____ · Evidência: ____
- [ ] **0.3** Atualizar a documentação com o estado real: AvalonEdit **6.3.0.90** compila em net10.0-windows (atualizar docs/06 §3.3 e PV-008 em docs/32); decidir manter `.sln` ou migrar para `.slnx` e registrar. · Concluído em: ____ · Evidência: ____
- [ ] **0.4** Criar `global.json` (SDK 10.0.x), `Directory.Build.props` (Nullable, `TreatWarningsAsErrors`, LangVersion 14, Deterministic) e `Directory.Packages.props` (Central Package Management), migrando a referência do AvalonEdit para o CPM. O App continua compilando sem warnings. *(TASK-00.01)* · Concluído em: ____ · Evidência: ____
- [ ] **0.5** Criar os projetos de [docs/07 §2](docs/07-solution-structure.md) (Core, Providers.Abstractions, Providers.Sqlite/MySql/PostgreSql/Firebird, Catalog, Mapping, Generation, Output, SqlTooling, Projects, Application, Infrastructure) e os de teste, com as referências **exatamente** como especificado. *(TASK-00.01)* · Concluído em: ____ · Evidência: ____
- [ ] **0.6** Registrar pacotes com versão, licença e data de verificação em `docs/dependencies.md`. · Concluído em: ____ · Evidência: ____
- [ ] **0.7** Primitivas comuns: `Result<T>`, `MdsError`, `ErrorCodes`, `IClock`, `Strings.resx`. Teste T-ERR-001. *(TASK-00.04)* · Concluído em: ____ · Evidência: ____
- [ ] **0.8** Testes de arquitetura T-ARC-001..007, incluindo a demonstração de que uma referência proibida faz o teste falhar. *(TASK-00.02)* · Concluído em: ____ · Evidência: ____
- [ ] **0.9** CI (`build-unit` no Windows, `integration` no Ubuntu) e scripts locais equivalentes `build.ps1`/`test.ps1`. *(TASK-00.03; plataforma conforme DP-004)* · Concluído em: ____ · Evidência: ____
- [ ] **0.10** Fixtures de schema (`loja`, `cycles`, `selfref`, `composite-keys`) para os 4 SGBDs, `tools/docker/compose.test.yml` e um teste de fumaça Testcontainers. Pode correr em paralelo com 0.7–0.9. *(TASK-00.05)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] `dotnet build -c Release` sem warnings em toda a solução
- [ ] T-ARC-* e T-ERR-001 verdes
- [ ] App abre e todas as telas existentes continuam idênticas aos mockups (capturas antes/depois em `docs/progress/`)
- [ ] CI verde no último commit

**Relatório do sprint:** ____

---

## Sprint 1 — Base MVVM/DI da interface

**Objetivo:** preparar a UI existente para receber dados reais (Generic Host, DI, CommunityToolkit.Mvvm, navegação por ViewModel), mantendo os dados de exemplo **atrás de interfaces**.
**Referências:** docs/05 §5–6, docs/21 §1–3, docs/07 §4, ADR-001.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **1.1** Configurar `Microsoft.Extensions.Hosting` no `App.xaml.cs`: composition root em `App/Composition/ServiceRegistration.cs`, ciclo de vida e handler global de exceções ([docs/05 §9](docs/05-system-architecture.md)). · Concluído em: ____ · Evidência: ____
- [ ] **1.2** Adicionar CommunityToolkit.Mvvm e criar `ShellViewModel` com a navegação, hoje no code-behind de `MainWindow`, usando `DataTemplate` por ViewModel. Os botões da janela continuam funcionando. · Concluído em: ____ · Evidência: ____
- [ ] **1.3** Criar um ViewModel por tela existente (Home, Connections, Explorer, SchemaDigest, Templates, Mapping) e trocar os `x:Static SampleData.*` por bindings. Os dados vêm de `IDesignDataSource` (implementação `SampleDesignDataSource` sobre o `SampleData` atual). · Concluído em: ____ · Evidência: ____
- [ ] **1.4** Separar a tela "SQL Editor" da "Explorer" (`SqlEditorView` própria, com o AvalonEdit e o `sql.xshd`), conforme decisão da etapa 0.2. · Concluído em: ____ · Evidência: ____
- [ ] **1.5** Alinhar `Themes/Colors.xaml` aos tokens de [docs/21 §2.1](docs/21-user-interface-ux.md) (incluindo cores de ambiente, severidade e confiança), sem mudar o visual aprovado. Divergências entre mockup e doc são resolvidas a favor do mockup, com o doc atualizado. · Concluído em: ____ · Evidência: ____
- [ ] **1.6** Criar os componentes padronizados ainda ausentes: `EnvironmentChip`, `SeverityBadge`, `ConfidenceBadge`, `ProgressPanel`, `DiagnosticsList`, `ConfirmDialog`/`TypedConfirmDialog` ([docs/21 §2.2](docs/21-user-interface-ux.md)). · Concluído em: ____ · Evidência: ____
- [ ] **1.7** Externalizar os textos da UI para `Strings.resx` (pt-BR), incluindo os rótulos de navegação. · Concluído em: ____ · Evidência: ____
- [ ] **1.8** Projeto `MockDataStudio.App.UiTests` (FlaUI) com teste de fumaça: abre o app e navega por todas as telas sem exceção. · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] Nenhuma view referencia `SampleData` diretamente (só `SampleDesignDataSource` o faz)
- [ ] Teste de fumaça de UI verde
- [ ] Visual idêntico ao do Sprint 0 (capturas comparadas)
- [ ] T-ARC-003/005 verdes (WPF e Providers só no App/composition root)

**Relatório do sprint:** ____

---

## Sprint 2 — Modelo canônico e Schema Digest

**Objetivo:** núcleo de schema independente de SGBD, com serialização, validação, hash e diff do digest.
**Referências:** docs/08 §2, docs/12, docs/contracts/C01, schemas/digest-1.0, examples/digest-*.json.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **2.1** `ObjectName`/`ColumnRef`/`CanonicalId` com testes de round-trip. *(TASK-01.01)* · Concluído em: ____ · Evidência: ____
- [ ] **2.2** Records do `SchemaModel` com invariantes e `Core.Connections`/`Core.Bindings`/`Core.Profiles`/`Core.Templates`. *(TASK-01.02, TASK-02.01 parte Core)* · Concluído em: ____ · Evidência: ____
- [ ] **2.3** `DigestSerializer.Serialize`. *(TASK-01.03)* · Concluído em: ____ · Evidência: ____
- [ ] **2.4** `Deserialize` + `DigestValidator` com todos os códigos `DIG-*`. Os 4 exemplos de `docs/examples` fazem round-trip. *(TASK-01.04)* · Concluído em: ____ · Evidência: ____
- [ ] **2.5** `StructureHasher`, `ExpressionNormalizer` e `ColumnFingerprint`. *(TASK-01.05)* · Concluído em: ____ · Evidência: ____
- [ ] **2.6** `SchemaDiffer` com severidades e candidatos de renomeação. *(TASK-01.06)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-DIG-001..007 e T-DIG-010 verdes
- [ ] Hashes reais dos 4 exemplos calculados. O campo `hash` dos exemplos em `docs/examples` foi atualizado e a nota de "hash ilustrativo" removida de docs/12 §10
- [ ] Cobertura de Core ≥ 70%

**Relatório do sprint:** ____

---

## Sprint 3 — SqlTooling e provider SQLite

**Objetivo:** lexer/splitter por dialeto e o primeiro provider completo, validado pela suíte de contrato.
**Referências:** docs/20 §4–5, docs/10 §3, docs/11, docs/contracts/C02, ADR-007.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **3.1** Contratos de `Providers.Abstractions`, `ProviderRegistry` e `IMdsModule`. *(TASK-02.01)* · Concluído em: ____ · Evidência: ____
- [ ] **3.2** Lexer SQL para os 4 dialetos. *(TASK-03.01)* · Concluído em: ____ · Evidência: ____
- [ ] **3.3** Splitter (`SET TERM`, `DELIMITER`, BEGIN/END, `$$`) e classificador de comandos destrutivos. *(TASK-03.02)* · Concluído em: ____ · Evidência: ____
- [ ] **3.4** SQLite: conexão, `ServerInfo`/fingerprint, `TypeMapper`. *(TASK-02.02)* · Concluído em: ____ · Evidência: ____
- [ ] **3.5** SQLite: `SchemaExtractor`. O digest do schema `loja` bate com `examples/digest-sqlite.json`. *(TASK-02.03)* · Concluído em: ____ · Evidência: ____
- [ ] **3.6** SQLite: dialeto, literais, `TargetInspector`, `BulkInserter`, `ErrorTranslator`. *(TASK-02.04)* · Concluído em: ____ · Evidência: ____
- [ ] **3.7** Suíte abstrata `ProviderContractTests` passando para SQLite. *(TASK-02.05)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-SQL-SPLIT-* (4 dialetos) e T-SQL-CLASS-001 verdes
- [ ] T-PRV-SQLITE-001..011 e T-OUT-LIT-SQLITE verdes
- [ ] PV-013 e PV-015 resolvidas em docs/32

**Relatório do sprint:** ____

---

## Sprint 4 — Catálogo interno

**Objetivo:** `catalog.db` com migrações, repositórios, importação por staging isolado, backup e pack inicial pt-BR.
**Referências:** docs/09, docs/13, docs/contracts/C03, ADR-004, ADR-016, examples/catalog-import-example.sql.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **4.1** `CatalogDatabase`, pragmas, `CatalogMigrator` e `V0001__init.sql` idêntico a docs/09 §3. *(TASK-04.01)* · Concluído em: ____ · Evidência: ____
- [ ] **4.2** Repositórios (templates, campos, aliases, valores, registros, presets, regras, feedback, perfis sem segredo, histórico, execuções). *(TASK-04.02)* · Concluído em: ____ · Evidência: ____
- [ ] **4.3** Staging: análise do script com todos os códigos `CAT-STG-*`/`CAT-IMP-*`. *(TASK-04.03)* · Concluído em: ____ · Evidência: ____
- [ ] **4.4** Merge transacional com os modos `InsertOnly`/`Upsert`/`ReplaceTemplateData` e backup `premerge`. *(TASK-04.04)* · Concluído em: ____ · Evidência: ____
- [ ] **4.5** Integridade, backup e restauração. *(TASK-04.05)* · Concluído em: ____ · Evidência: ____
- [ ] **4.6** `CatalogTemplateDataSource` (`TemplateDataSet` ordenado + `ContentHash`). *(TASK-04.07)* · Concluído em: ____ · Evidência: ____
- [ ] **4.7** Pack `CORE_PTBR` (subconjunto MVP) instalado via staging, com `Seeds/SOURCES.md` de procedência e licença. *(TASK-04.06)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-CAT-001..007, 009, 010 e T-SEC-009 verdes
- [ ] `examples/catalog-import-example.sql` importa sem erros. Um script com `DROP` é rejeitado sem alterar o catálogo
- [ ] Teste de migração com falha injetada preserva o catálogo

**Relatório do sprint:** ____

---

## Sprint 5 — Mapeamento semântico

**Objetivo:** sugestões determinísticas e explicáveis de template/gerador por coluna.
**Referências:** docs/14, docs/contracts/C04.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **5.1** `NameNormalizer` (tabela de docs/14 §2 + 200 casos). *(TASK-05.01)* · Concluído em: ____ · Evidência: ____
- [ ] **5.2** `MappingKnowledge` e `StructuralBinder`. *(TASK-05.02)* · Concluído em: ____ · Evidência: ____
- [ ] **5.3** Pontuação, evidências, desempate, faixas de confiança, ambiguidade, fallback e slots. *(TASK-05.03)* · Concluído em: ____ · Evidência: ____
- [ ] **5.4** Corpus `mapping-corpus` (≥ 1.500 nomes) e medição das metas. *(TASK-05.04)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-MAP-001..006 e 008 verdes
- [ ] T-MAP-CORPUS: ≥ 70% High e precisão ≥ 95%, ou desvio registrado em docs/32 com plano de correção
- [ ] Caso `ZAP → TELEFONE` com confiança High comprovado

**Relatório do sprint:** ____

---

## Sprint 6 — Motor de geração

**Objetivo:** RNG determinístico, geradores, MDS-Expr, unicidade e engine em streaming.
**Referências:** docs/15, docs/contracts/C05, ADR-005, ADR-013, ADR-014.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **6.1** `StreamKey` + `DeterministicRandom`, reproduzindo os vetores de docs/15 §4.1. *(TASK-06.01)* · Concluído em: ____ · Evidência: ____
- [ ] **6.2** Infraestrutura de geradores: registry, descriptors, validação de parâmetros, `ValueCoercer`, `WeightedIndex`, distribuições, `IsoDuration`. *(TASK-06.02)* · Concluído em: ____ · Evidência: ____
- [ ] **6.3** Geradores `core.*` básicos com snapshots. *(TASK-06.03)* · Concluído em: ____ · Evidência: ____
- [ ] **6.4** `catalog.template`, `catalog.template.field` e slots de `RowContext`. *(TASK-06.04)* · Concluído em: ____ · Evidência: ____
- [ ] **6.5** MDS-Expr (lexer, parser, avaliador, funções), `core.format` e `core.expr`. *(TASK-06.05)* · Concluído em: ____ · Evidência: ____
- [ ] **6.6** Unicidade: Feistel, base mista, estratégias 1–5 e verificação de capacidade. *(TASK-06.06)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-GEN-RNG-001/002, T-GEN-LIB-*, T-GEN-DET-001, T-GEN-UQ-001..004, T-GEN-EXPR-001..003, T-GEN-COMP-001, T-GEN-NULL-001 e T-GEN-COERCE-001 verdes
- [ ] B-RNG-001 executado e registrado

**Relatório do sprint:** ____

---

## Sprint 7 — Planejamento relacional e validação

**Objetivo:** grafo de dependências, ciclos, chaves, cardinalidade, FKs compostas, plano completo, validador L2/L3 e engine ponta a ponta em memória.
**Referências:** docs/16, docs/17, docs/contracts/C06–C07, ADR-008.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **7.1** `DependencyGraph` + Tarjan iterativo. *(TASK-07.01)* · Concluído em: ____ · Evidência: ____
- [ ] **7.2** `KeyPlanner` (Explicit/DatabaseDefault) e `KeyOracle`. *(TASK-07.03)* · Concluído em: ____ · Evidência: ____
- [ ] **7.3** `CycleResolver` (NullThenUpdate + bloqueio) e autorreferência anulável. *(TASK-07.02)* · Concluído em: ____ · Evidência: ____
- [ ] **7.4** `rel.fk` ChildDriven, FKs compostas, sobreposição e `rel.child_seq`. *(TASK-07.04)* · Concluído em: ____ · Evidência: ____
- [ ] **7.5** `GenerationEngine` (chunks paralelos ordenados, `IRowSink`, preview). *(TASK-06.07)* · Concluído em: ____ · Evidência: ____
- [ ] **7.6** `GenerationPlanner` completo: diagnósticos `PLN-*`, estimativas e `PlanHash`. *(TASK-07.05)* · Concluído em: ____ · Evidência: ____
- [ ] **7.7** `PreflightValidator` L2/L3 e interpretador de CHECK. *(TASK-07.06)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-REL-001/002/005/006/007/012, T-REL-PLAN-001, T-GEN-DET-002/003, T-VAL-LOJA e T-VAL-CHECK-001 verdes
- [ ] Mesma saída com MaxDOP 1 e 8 (hash registrado)
- [ ] B-GEN-001 preliminar ≥ 100 k linhas/s (sem writer)

**Relatório do sprint:** ____

---

## Sprint 8 — Saída SQL + providers PostgreSQL e Firebird

**Objetivo:** scripts SQL executáveis nos três SGBDs do MVP.
**Referências:** docs/18, docs/10 §5–6, docs/11, examples/output-*.sql.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **8.1** `SqlScriptWriter` + `SegmentedTextSink` (cabeçalho, transações, resync). *(TASK-08.01)* · Concluído em: ____ · Evidência: ____
- [ ] **8.2** Provider PostgreSQL completo e suíte de contrato. *(TASK-08.02)* · Concluído em: ____ · Evidência: ____
- [ ] **8.3** Provider Firebird completo (heurística `tsq`, resync por versão) e suíte de contrato em FB 5, com resultados em FB 3/4 registrados. *(TASK-08.03)* · Concluído em: ____ · Evidência: ____
- [ ] **8.4** Corpus adversarial de literais e round-trip nos 3 SGBDs. · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-PRV-PG-*, T-PRV-FB-* (incl. FB-KEY-001..003), T-OUT-001/002/006, T-OUT-LIT-* e T-OUT-ROUNDTRIP-* (SQLite/PG/FB) verdes
- [ ] PV-004, PV-005, PV-009, PV-012 e PV-014 resolvidas em docs/32
- [ ] `examples/output-postgresql.sql` e `output-firebird.sql` atualizados com saídas reais

**Relatório do sprint:** ____

---

## Sprint 9 — Projeto, Application e Infraestrutura

**Objetivo:** persistência do projeto (`.mdsproj`), serviços de aplicação sem UI e infraestrutura (segredos, logs).
**Referências:** docs/22, docs/23 §3 e §6, docs/contracts/C09.

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **9.1** `MdsProject`, serializer e validator. `examples/project-loja.mdsproj` faz round-trip. *(TASK-09.01)* · Concluído em: ____ · Evidência: ____
- [ ] **9.2** Infraestrutura: `DpapiSecretStore`, settings e Serilog com redação. *(TASK-09.03)* · Concluído em: ____ · Evidência: ____
- [ ] **9.3** Serviços MVP: Connection, Digest, Mapping, Generation e `ReferenceDataSource` (Generated/Fixed). *(TASK-09.02)* · Concluído em: ____ · Evidência: ____
- [ ] **9.4** E2E sem UI: T-E2E-SQLITE/PG/FB-001 e T-E2E-003. *(TASK-08.04)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-PRJ-001/002/003/005/006/010, T-SEC-002/004/012 e T-APP-001..005 verdes
- [ ] E2E: scripts gerados executados via `sqlite3`/`psql`/`isql` sem erro, com FKs/UNIQUEs/CHECKs verificados

**Relatório do sprint:** ____

---

## Sprint 10 — Integração da UI (fluxo MVP) e Gate MVP

**Objetivo:** ligar as telas do protótipo aos serviços reais e entregar o fluxo completo: conexão → digest → mapeamento → plano → script.
**Referências:** docs/21 (S02, S06, S10, S11, S12, S14, S15, S16), docs/29 §2, docs/28 (TASK-09.04..09.07).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **10.1** **Connections** com dados reais (SQLite/PG/FB): CRUD, testar conexão, ambiente, somente leitura e senha protegida. Remover a fonte de exemplo desta tela. *(TASK-09.05)* · Concluído em: ____ · Evidência: ____
- [ ] **10.2** **Schema Digest** real: capturar (com progresso/cancelar), importar e exportar, árvore do digest. *(S06)* · Concluído em: ____ · Evidência: ____
- [ ] **10.3** **Templates** lendo o catálogo real (somente consulta neste sprint; edição completa no Sprint 17). · Concluído em: ____ · Evidência: ____
- [ ] **10.4** **Mapping** real: tabelas do projeto, sugestões com evidências e confiança, aceitar/alterar binding, editor de gerador gerado pelo descriptor com preview ao vivo, desfazer/refazer. *(TASK-09.06, S10–S12)* · Concluído em: ____ · Evidência: ____
- [ ] **10.5** Substituir o placeholder **Generation** pelas telas de plano/validação, preview e geração de script (S14, S15, S16 — só script). *(TASK-09.07)* · Concluído em: ____ · Evidência: ____
- [ ] **10.6** **Home** com projetos e conexões recentes reais. Novo/abrir/salvar projeto. · Concluído em: ____ · Evidência: ____
- [ ] **10.7** Remover `SampleData`/`SampleDesignDataSource` das telas do fluxo MVP. Telas ainda não integradas (Explorer, SQL Editor, History, Settings) ficam marcadas na UI como "Em desenvolvimento". · Concluído em: ____ · Evidência: ____
- [ ] **10.8** Teste T-UI-FLOW-001 (fluxo SQLite completo só por teclado) e T-UI-PERF-002. · Concluído em: ____ · Evidência: ____

**Verificação do sprint — Gate MVP ([docs/29 §2](docs/29-acceptance-criteria.md))**
- [ ] G-MVP-1 — schema `loja` em SQLite, PostgreSQL 17 e Firebird 5: ponta a ponta sem erros
- [ ] G-MVP-2 — contagens, FKs (compostas/autorreferência), UNIQUEs e CHECKs verificados
- [ ] G-MVP-3 — scripts idênticos com a mesma seed (MaxDOP 1 e 8)
- [ ] G-MVP-4 — ciclo anulável gerado; ciclo NOT NULL bloqueado com diagnóstico
- [ ] G-MVP-5 — importação de catálogo funcionando; script com `DROP` rejeitado
- [ ] G-MVP-6 — metas do corpus de mapeamento
- [ ] G-MVP-7 — 1 M linhas de `cliente` com CPF único: ≤ 300 MB e ≥ 100 k linhas/s
- [ ] G-MVP-8 — fluxo MVP na UI executável só por teclado
- [ ] G-MVP-9 — testes de arquitetura e segurança do MVP verdes
- [ ] G-MVP-10 — relatório do marco e riscos residuais atualizados em docs/30

**Relatório do sprint / MVP:** ____

---

## Sprint 11 — MySQL e segurança de conexões

**Objetivo:** quarto SGBD obrigatório e proteções completas por ambiente.
**Referências:** docs/10 §4, docs/23 §5, docs/28 (EPIC-10, EPIC-11).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **11.1** Provider MySQL: conexão, tipos e extractor. Adicionar logo/ícone MySQL na UI (hoje só existe o da MariaDB). *(TASK-10.01)* · Concluído em: ____ · Evidência: ____
- [ ] **11.2** Dialeto, literais e escapes (com e sem `NO_BACKSLASH_ESCAPES`). *(TASK-10.02)* · Concluído em: ____ · Evidência: ____
- [ ] **11.3** Inspector, bulk, erros e contrato em MySQL 8.4 e 9.x. *(TASK-10.03)* · Concluído em: ____ · Evidência: ____
- [ ] **11.4** E2E MySQL e teste de autorreferência NOT NULL (resultado em PV-007). *(TASK-10.04)* · Concluído em: ____ · Evidência: ____
- [ ] **11.5** `ExecutionGuard` com a matriz completa. *(TASK-11.01)* · Concluído em: ____ · Evidência: ____
- [ ] **11.6** Somente leitura imposto também no servidor. *(TASK-11.02)* · Concluído em: ____ · Evidência: ____
- [ ] **11.7** Configurações avançadas por provider e resiliência de conexão. *(TASK-11.03)* · Concluído em: ____ · Evidência: ____
- [ ] **11.8** Fingerprints e alerta de servidor divergente. *(TASK-11.04)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-PRV-MYSQL-*, T-E2E-MYSQL-001, T-SQL-GUARD-001 e T-SEC-003/005/006 verdes

**Relatório do sprint:** ____

---

## Sprint 12 — Explorer real

**Objetivo:** substituir os dados de exemplo da tela Explorer por navegação real nos 4 SGBDs.
**Referências:** docs/21 (S03, S05), docs/28 (EPIC-12).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **12.1** Árvore de objetos com carga lazy e virtualização. *(TASK-12.01)* · Concluído em: ____ · Evidência: ____
- [ ] **12.2** Inspetor de tabela e referências. *(TASK-12.02)* · Concluído em: ____ · Evidência: ____
- [ ] **12.3** Ações rápidas, refresh por nó e filtro. *(TASK-12.03)* · Concluído em: ____ · Evidência: ____
- [ ] **12.4** Remover `SampleData` da tela Explorer. · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-UI-PERF-001 (10 mil tabelas) e T-UI-FLOW-003 (parte do explorer) verdes

**Relatório do sprint:** ____

---

## Sprint 13 — Editor SQL

**Objetivo:** editor completo, com sessões, transação manual, autocomplete, resultados, histórico e proteções.
**Referências:** docs/20, docs/28 (EPIC-13).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **13.1** `QuerySession`/`QueryExecutionService` (Manual/AutoCommit, paginação, cancelamento). *(TASK-13.01)* · Concluído em: ____ · Evidência: ____
- [ ] **13.2** Motor de autocomplete com aliases. *(TASK-13.02)* · Concluído em: ____ · Evidência: ____
- [ ] **13.3** UI do editor: abas por perfil, chip de ambiente, `.xshd` por dialeto (evoluindo o `sql.xshd` atual) e recuperação de abas. *(TASK-13.03)* · Concluído em: ____ · Evidência: ____
- [ ] **13.4** Grade de resultados, exportação, histórico e erros com posição. *(TASK-13.04)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-SQL-EXEC-001/002, T-SQL-COMP-001, T-SQL-HIST-001, T-UI-PERF-003 e T-UI-FLOW-003 verdes
- [ ] B-SQL-001 (autocomplete p95 ≤ 50 ms)

**Relatório do sprint:** ____

---

## Sprint 14 — Projetos avançados e evolução de schema

**Objetivo:** snapshots, diff, migração de bindings, múltiplos perfis e portabilidade.
**Referências:** docs/12 §8–9, docs/22, docs/28 (EPIC-14).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **14.1** Snapshots e diff na tela Schema Digest (comparação com severidades, como no mockup). *(TASK-14.01)* · Concluído em: ____ · Evidência: ____
- [ ] **14.2** Migração de bindings (Active/NeedsReview/Orphaned + propostas de renomeação). *(TASK-14.02)* · Concluído em: ____ · Evidência: ____
- [ ] **14.3** Múltiplos perfis, migração de formato, autosave, exportação sanitizada e confiança em projetos de terceiros. *(TASK-14.03)* · Concluído em: ____ · Evidência: ____
- [ ] **14.4** Dependências de catálogo ao abrir o projeto. *(TASK-14.04)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-DIG-005/006/030, T-E2E-004, T-PRJ-004/007 e T-SEC-008 verdes

**Relatório do sprint:** ____

---

## Sprint 15 — Inserção direta

**Objetivo:** inserir os dados gerados no banco com autorização, lotes, captura de chaves e limpeza.
**Referências:** docs/19, docs/23 §5, docs/28 (EPIC-15).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **15.1** Autorização vinculada a fingerprint e diálogos de confirmação (incluindo confirmação digitada). *(TASK-15.01)* · Concluído em: ____ · Evidência: ____
- [ ] **15.2** Bulk inserters: PG COPY, MySQL multi-row, FB prepared/batch e SQLite. *(TASK-15.02)* · Concluído em: ____ · Evidência: ____
- [ ] **15.3** `DirectInsertWriter`: transações, falhas e cancelamento. *(TASK-15.03)* · Concluído em: ____ · Evidência: ____
- [ ] **15.4** Estratégia Captured + `KeyRegistry`. *(TASK-15.04)* · Concluído em: ____ · Evidência: ____
- [ ] **15.5** Script de limpeza, relatório, execuções interrompidas e substituição do placeholder **History** (S17). *(TASK-15.05)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-DIR-*, T-SEC-001/011, T-OUT-004 e T-E2E-<X>-002 nos 4 SGBDs verdes
- [ ] PV-002 e PV-006 resolvidas; B-DIR-* registrados

**Relatório do sprint:** ____

---

## Sprint 16 — Relacional avançado e validação L4

**Objetivo:** estratégias de ciclo restantes, cardinalidades avançadas, fontes de referência e inserção de teste.
**Referências:** docs/16, docs/17 §6, docs/28 (EPIC-16).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **16.1** DeferredConstraint, DisableChecks (opt-in) e autorreferência NOT NULL. *(TASK-16.01)* · Concluído em: ____ · Evidência: ____
- [ ] **16.2** ParentDriven, Zipf, Poisson e 1:1. *(TASK-16.02)* · Concluído em: ____ · Evidência: ____
- [ ] **16.3** Fontes Existing/Dataset/Query e verificação de chaves no destino. *(TASK-16.03)* · Concluído em: ____ · Evidência: ____
- [ ] **16.4** NaturalKeyLookup. *(TASK-16.04)* · Concluído em: ____ · Evidência: ____
- [ ] **16.5** Inserção de teste (L4) com rollback. *(TASK-16.05)* · Concluído em: ____ · Evidência: ____
- [ ] **16.6** Tela de Relacionamentos (S13) integrada ao fluxo de geração. · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-REL-003/004/008..011, T-REL-SELF-NN-*, T-REL-PLAN-002, T-VAL-001..040 (40/40) e T-VAL-TRIAL-001/002 verdes

**Relatório do sprint:** ____

---

## Sprint 17 — Catálogo V1

**Objetivo:** manutenção completa do catálogo na UI, packs, presets, aprendizado e geradores brasileiros.
**Referências:** docs/13, docs/14 §8–9, docs/15 §6, docs/28 (EPIC-17).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **17.1** Tela **Templates** com edição completa (S07/S08): escalares, compostos, aliases, valores e colagem em lote. Remover `SampleData` desta tela. *(TASK-17.01)* · Concluído em: ____ · Evidência: ____
- [ ] **17.2** Importação por script (S09) com "Copiar instruções para IA". *(TASK-17.02)* · Concluído em: ____ · Evidência: ____
- [ ] **17.3** Packs JSON (exportar/importar). *(TASK-17.03)* · Concluído em: ____ · Evidência: ____
- [ ] **17.4** Presets, regras aprendidas e diagnóstico de mapeamento. *(TASK-17.04)* · Concluído em: ____ · Evidência: ____
- [ ] **17.5** Geradores BR, `core.switch`, lorem e uuid v7. *(TASK-17.05)* · Concluído em: ____ · Evidência: ____
- [ ] **17.6** Pack `CORE_PTBR` completo e reavaliação do corpus. *(TASK-17.06)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-CAT-008, T-MAP-007, T-GEN-BR-001 e T-UI-FLOW-002 verdes; T-MAP-CORPUS reavaliado
- [ ] PV-011 resolvida (ou CNPJ alfanumérico adiado com registro)

**Relatório do sprint:** ____

---

## Sprint 18 — Volume e formatos

**Objetivo:** grandes volumes, manifesto, CSV/JSON Lines e metas de desempenho medidas.
**Referências:** docs/18 §6 e §9, docs/24, docs/28 (EPIC-18).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **18.1** Manifesto, segmentação completa e guardas no script. *(TASK-18.01)* · Concluído em: ____ · Evidência: ____
- [ ] **18.2** Saídas CSV e JSON Lines. *(TASK-18.02)* · Concluído em: ____ · Evidência: ____
- [ ] **18.3** Benchmarks e testes de volume; resultados em `docs/benchmarks/`. *(TASK-18.03)* · Concluído em: ____ · Evidência: ____

**Verificação do sprint**
- [ ] T-OUT-002/003/005 e T-GEN-VOL-001 verdes
- [ ] NFR-PERF-001..009 atendidos, ou desvios aprovados e registrados em docs/32

**Relatório do sprint:** ____

---

## Sprint 19 — Hardening, acessibilidade e release (Gate V1)

**Objetivo:** produto pronto para distribuição.
**Referências:** docs/21 §7, docs/25, docs/04, docs/29 §3, docs/28 (EPIC-19).

> 🤖 **IA: marque cada etapa com `[x]` ao concluir e preencha data e evidência.**

- [ ] **19.1** Acessibilidade e DPI 100–200%. *(TASK-19.01)* · Concluído em: ____ · Evidência: ____
- [ ] **19.2** Substituir o placeholder **Settings** (S18), além de paleta de comandos e atalhos. *(TASK-19.02)* · Concluído em: ____ · Evidência: ____
- [ ] **19.3** Logs e pacote de diagnóstico. *(TASK-19.03)* · Concluído em: ____ · Evidência: ____
- [ ] **19.4** Instalador, `docs/licenses.md` e verificação offline (DP-003, DP-008, PV-001). *(TASK-19.04)* · Concluído em: ____ · Evidência: ____
- [ ] **19.5** Manual do usuário local (F1). *(TASK-19.05)* · Concluído em: ____ · Evidência: ____
- [ ] **19.6** Confirmar que não resta nenhuma referência a `Samples/SampleData` no código de produção e remover a pasta `Samples`. · Concluído em: ____ · Evidência: ____
- [ ] **19.7** Auditoria final documentação × implementação, no formato de docs/36. · Concluído em: ____ · Evidência: ____

**Verificação do sprint — Gate V1 ([docs/29 §3](docs/29-acceptance-criteria.md))**
- [ ] G-V1-1 — todos os FR MVP/V1 (M) atendidos; matriz docs/35 100% verde
- [ ] G-V1-2 — matriz oficial de versões de SGBD verde
- [ ] G-V1-3 — inserção direta + limpeza restaura o banco nos 4 SGBDs
- [ ] G-V1-4 — editor SQL: guarda, transação manual e cancelamento nos 4 SGBDs
- [ ] G-V1-5 — evolução de schema preserva bindings
- [ ] G-V1-6 — NFRs medidos dentro das metas (ou desvio aprovado)
- [ ] G-V1-7 — acessibilidade e DPI
- [ ] G-V1-8 — instalação em Win10/Win11 limpas e execução offline
- [ ] G-V1-9 — licenças e procedência dos dados do pack registradas
- [ ] G-V1-10 — auditoria final sem divergências críticas/altas abertas

**Relatório do sprint / V1:** ____

---

## Depois da V1 (V2 — detalhar antes de iniciar)

CLI · Plugins · Grafo de relacionamentos · Designer de cenários · Regras condicionais declarativas · Perfil estatístico · Anonimização restrita · Perfis de carga e cobertura · Pacotes temáticos · MariaDB oficial · Coerência entre tabelas. Ver [docs/27 §4](docs/27-development-roadmap.md).

## Decisões de produto pendentes (não bloqueiam o início)

DP-001 nome comercial · DP-002 licença · DP-003 empacotamento · DP-004 plataforma de CI · DP-005 idiomas · DP-006 anonimização no V2 · DP-007 telemetria · DP-008 certificado · DP-009 testes de usabilidade. Ver [docs/32 §3](docs/32-decisions-and-assumptions.md).
