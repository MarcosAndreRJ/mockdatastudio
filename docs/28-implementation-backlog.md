# 28 — Backlog de Implementação (Plano Operacional)

| Campo | Valor |
|---|---|
| Objetivo | Sequência executável de épicos e tarefas para a IA Executora, ordenada por dependência, com critérios de aceite e evidências. |
| Escopo | MVP (M0–M4) detalhado tarefa a tarefa; V1 (M5–M10) detalhado; V2 em nível de épico. |
| Relacionados | [27](27-development-roadmap.md), [29](29-acceptance-criteria.md), [33](33-executor-instructions.md), [35](35-traceability-matrix.md) |

## 0. Como ler

Cada tarefa possui: **Objetivo · Contexto (docs) · Arquivos/módulos · Pré-requisitos · Contratos · Passos · Regras obrigatórias · Testes · Aceite · Evidências · Riscos**.
"Evidências" = o que a IA Executora deve anexar ao relatório da tarefa (saída de comandos, arquivos de snapshot, capturas) conforme [33 §10](33-executor-instructions.md).
Regras comuns a **todas** as tarefas (não repetidas): build Release sem warnings; testes rápidos verdes; nenhum `TODO` sem ID de tarefa; strings de UI em `Strings.resx`; códigos de erro registrados em `ErrorCodes`.

---

## M0 — Fundação

**Verificação obrigatória do marco**: `dotnet build -c Release` ok; `dotnet test --filter Category=Unit` ok; T-ARC-001..007 verdes; CI `build-unit` verde.

### EPIC-00 — Fundação do repositório

#### TASK-00.01 — Criar solução e projetos
- **Objetivo**: estrutura de [07](07-solution-structure.md) compilável.
- **Contexto**: 07 §1–4, ADR-002, ADR-015.
- **Arquivos**: `MockDataStudio.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, todos os `src/*/*.csproj` e `tests/*/*.csproj` (vazios), `README.md`.
- **Pré-requisitos**: nenhum.
- **Contratos**: —.
- **Passos**: 1) `global.json` com SDK 10.0.x (rollForward latestFeature). 2) Criar projetos com TFMs de 07 §2. 3) Referências de projeto exatamente como 07 §2. 4) CPM com versões verificadas no nuget.org (registrar versões e data em `docs/dependencies.md`). 5) `App` como WinExe WPF exibindo janela vazia.
- **Regras**: nenhuma dependência fora de 06 §8; `Nullable`, `TreatWarningsAsErrors`.
- **Testes**: build.
- **Aceite**: solução compila; app abre janela vazia.
- **Evidências**: saída do build; `docs/dependencies.md` com pacote/versão/licença/data.
- **Riscos**: versão de pacote sem suporte a net10 → registrar em 32 e escolher versão compatível.

#### TASK-00.02 — Testes de arquitetura
- **Objetivo**: impor AR-01..AR-07.
- **Contexto**: 05 §3.
- **Arquivos**: `tests/MockDataStudio.Architecture.Tests/*`.
- **Pré-requisitos**: 00.01.
- **Passos**: implementar verificações por reflexão (`Assembly.GetReferencedAssemblies`) e varredura de IL/sintaxe para T-ARC-007 (pode usar análise de código-fonte por Roslyn em teste, ou regex sobre `.cs` com lista de exceções).
- **Regras**: falhas devem listar tipo/assembly ofensivo.
- **Testes**: T-ARC-001..007.
- **Aceite**: todos verdes; introduzir referência proibida localmente faz o teste falhar (demonstrar e reverter).
- **Evidências**: saída do teste falhando e passando.
- **Riscos**: T-ARC-007 com falsos positivos → lista de exceções explícita.

#### TASK-00.03 — CI
- **Objetivo**: pipelines `build-unit` (windows) e `integration` (ubuntu, inicialmente vazio).
- **Contexto**: 26 §8.
- **Arquivos**: `.github/workflows/ci.yml` (ou equivalente do repositório do usuário — DP-004).
- **Pré-requisitos**: 00.01.
- **Passos**: restore com lock files; build Release; testes `Category=Unit|Architecture`; coverlet; artefato de cobertura.
- **Testes**: pipeline executa.
- **Aceite**: pipeline verde no commit da tarefa.
- **Evidências**: link/log da execução.
- **Riscos**: plataforma de CI indefinida (DP-004) → manter scripts `build.ps1`/`test.ps1` locais equivalentes.

#### TASK-00.04 — Primitivas comuns
- **Objetivo**: `Result<T>`, `MdsError`, `ErrorCodes`, `IClock`, `Guard`, `Strings.resx` base.
- **Contexto**: C01, 25 §2–3.
- **Arquivos**: `src/MockDataStudio.Core/Common/*`.
- **Pré-requisitos**: 00.01.
- **Contratos**: C01 (Common).
- **Passos**: implementar; T-ERR-001 (reflexão sobre `ErrorCodes` × recursos).
- **Testes**: unitários de Result; T-ERR-001.
- **Aceite**: 100% dos códigos com mensagem.
- **Evidências**: saída dos testes.
- **Riscos**: —.

#### TASK-00.05 — Infraestrutura de testes e fixtures
- **Objetivo**: DDL de referência e base Testcontainers.
- **Contexto**: 26 §2–3, exemplos de digest.
- **Arquivos**: `tests/fixtures/schemas/loja.{sqlite,mysql,postgresql,firebird}.sql`, `cycles.*`, `selfref.*`, `composite-keys.*`, `tests/containers.json`, `tools/docker/compose.test.yml`, `tests/MockDataStudio.Providers.IntegrationTests/Infrastructure/*`.
- **Pré-requisitos**: 00.01.
- **Passos**: 1) Escrever DDL `loja` coerente com os 4 digests de [examples](examples/). 2) Fixtures de banco por teste (cria/descarta). 3) Verificar imagens Docker atuais (Firebird oficial) e fixar tags.
- **Regras**: nenhum banco compartilhado entre testes.
- **Testes**: teste de fumaça que cria e descarta banco em cada SGBD.
- **Aceite**: fumaça verde no job `integration`.
- **Evidências**: log com versões dos servidores.
- **Riscos**: imagem Firebird indisponível → alternativa documentada em 26 §3.

## M1 — Núcleo de schema

**Verificação do marco**: T-DIG-001..007 verdes; T-PRV-SQLITE-001..011 verdes; round-trip do digest SQLite `loja`; T-SQL-SPLIT-SQLITE verde.

### EPIC-01 — Modelo canônico e digest

#### TASK-01.01 — Nomes canônicos
- **Objetivo**: `ObjectName`, `ColumnRef`, `CanonicalId` com quoting/parse.
- **Contexto**: 08 §2.1; C01.
- **Arquivos**: `Core/Naming/*`.
- **Pré-requisitos**: 00.04.
- **Passos**: implementar regras de quoting; `ParseCanonical` inverso exato; comparação ordinal.
- **Testes**: round-trip com nomes contendo `.`, `"`, espaços, Unicode (property-based com 10.000 casos gerados).
- **Aceite**: `Parse(Format(x)) == x` para todos os casos.
- **Evidências**: saída dos testes.
- **Riscos**: —.

#### TASK-01.02 — Records do SchemaModel
- **Objetivo**: tipos de C01 (Schema).
- **Contexto**: 08 §2.2–2.3.
- **Arquivos**: `Core/Schema/*`, `Core/Types/*`.
- **Pré-requisitos**: 01.01.
- **Passos**: records imutáveis; validações de invariantes em construtores estáticos `Create(...)` retornando `Result`; `FindTable`, `AllTables`.
- **Regras**: AR-07.
- **Testes**: invariantes (PK com coluna inexistente → Fail).
- **Aceite**: T-ARC-006 verde.
- **Evidências**: testes.
- **Riscos**: igualdade estrutural de records com listas — implementar `Equals` por sequência (usar `ImmutableArray` + comparador ou `EquatableArray<T>` próprio).

#### TASK-01.03 — Serialização do digest
- **Objetivo**: `DigestSerializer.Serialize` conforme 12 §3–4.
- **Contexto**: 12; schema JSON; exemplos.
- **Arquivos**: `Core/Digest/DigestSerializer.cs`, `Core/Digest/digest-1.0.schema.json` (EmbeddedResource, cópia de `docs/contracts/schemas`).
- **Pré-requisitos**: 01.02.
- **Passos**: `Utf8JsonWriter` manual (controle da ordem e tuplas); omissão de defaults; ordem canônica de flags.
- **Testes**: T-DIG-001 (validação pelo JSON Schema de referência em teste); snapshots.
- **Aceite**: os 4 exemplos de `docs/examples/digest-*.json`, desserializados e reserializados (após 01.04), são estruturalmente iguais (ignorando `hash`).
- **Evidências**: arquivos `.verified.json`.
- **Riscos**: divergência entre doc e código → corrigir ambos na mesma tarefa.

#### TASK-01.04 — Desserialização e validação
- **Objetivo**: `Deserialize` + `DigestValidator` (todos os códigos de 12 §7).
- **Arquivos**: `Core/Digest/*`.
- **Pré-requisitos**: 01.03.
- **Passos**: leitura com `Utf8JsonReader`/`JsonDocument`; acumular erros com JSON Path; regras de versão (12 §5).
- **Testes**: T-DIG-002 (round-trip), T-DIG-007 (fixtures inválidas por código), T-DIG-010.
- **Aceite**: cada código `DIG-*` provocado por ao menos uma fixture.
- **Evidências**: lista de fixtures × código.
- **Riscos**: —.

#### TASK-01.05 — Hash estrutural e fingerprint de coluna
- **Objetivo**: `StructureHasher` (12 §6) + `ExpressionNormalizer`.
- **Arquivos**: `Core/Hashing/*`.
- **Pré-requisitos**: 01.02.
- **Passos**: forma canônica; JSON canônico; SHA-256; `ComputeTable`; `ComputeColumnFingerprint`.
- **Testes**: T-DIG-003, T-DIG-004 (uma fixture por mudança listada).
- **Aceite**: invariâncias e sensibilidades listadas verificadas.
- **Evidências**: testes.
- **Riscos**: normalização de expressão insuficiente → hashes diferentes para defaults equivalentes entre bancos (aceitável; documentar casos em 32).

#### TASK-01.06 — Diff de schemas
- **Objetivo**: `SchemaDiffer` com severidade e candidatos de renomeação (12 §8).
- **Arquivos**: `Core/Diff/*`.
- **Pré-requisitos**: 01.05.
- **Testes**: T-DIG-005, T-DIG-006 com fixtures `loja-v1`/`loja-v2`.
- **Aceite**: diff esperado exato.
- **Evidências**: snapshot do diff.
- **Riscos**: Jaro-Winkler — implementar localmente (sem pacote).

### EPIC-02 — Abstração de providers e SQLite

#### TASK-02.01 — Providers.Abstractions
- **Objetivo**: contratos C02 + `ProviderRegistry` + `IMdsModule`.
- **Arquivos**: `Providers.Abstractions/*`, `Core/Connections/*`.
- **Pré-requisitos**: 01.02.
- **Contratos**: C02.
- **Passos**: interfaces; `ConnectionProfile`/`ConnectionSettings`/`ProviderCapabilities`/`EnvironmentTag` em Core.Connections.
- **Testes**: compila; T-ARC.
- **Aceite**: contratos idênticos a C02 (revisão por diff).
- **Evidências**: diff contrato × código.
- **Riscos**: —.

#### TASK-03.01 — Lexer SQL por dialeto (antecipada por dependência)
- **Objetivo**: `ISqlLexer` (20 §4) para os 4 dialetos.
- **Arquivos**: `SqlTooling/Lexing/*`.
- **Pré-requisitos**: 00.04.
- **Passos**: lexer manual (sem gerador); tokens com offsets; dollar-quoting PG; `#` comentário MySQL; `E'..'`, `N'..'`.
- **Testes**: corpus de tokens por dialeto.
- **Aceite**: corpus 100%.
- **Evidências**: testes.
- **Riscos**: casos MySQL `/*! ... */` → tratados como código (documentado).

#### TASK-03.02 — Splitter e classificador
- **Objetivo**: `ISqlScriptSplitter` + classificação (20 §4–5).
- **Pré-requisitos**: 03.01.
- **Testes**: T-SQL-SPLIT-<X> (4 dialetos), T-SQL-CLASS-001.
- **Aceite**: corpus 100%; destrutivos classificados.
- **Evidências**: testes.
- **Riscos**: blocos BEGIN/END aninhados com `CASE` → corpus específico.

#### TASK-02.02 — SQLite: conexão, servidor, tipos
- **Objetivo**: `SqliteProvider`, `SqliteTypeMapper` (10 §3, 11 §5).
- **Pré-requisitos**: 02.01.
- **Testes**: T-PRV-SQLITE-001, -003.
- **Aceite**: fixture `types-sqlite` mapeada conforme tabela.
- **Evidências**: snapshot.
- **Riscos**: —.

#### TASK-02.03 — SQLite: extractor
- **Objetivo**: `SqliteSchemaExtractor` (11 §2, §4.1).
- **Pré-requisitos**: 02.02, 03.01, 01.03.
- **Testes**: T-PRV-SQLITE-002/004/005/011.
- **Aceite**: digest `loja` igual ao exemplo (exceto hash/capturedAt/server).
- **Evidências**: `.verified.json`.
- **Riscos**: CHECK extraído do SQL original com formatação variável → normalização.

#### TASK-02.04 — SQLite: dialeto, literais, inspector, bulk, erros
- **Objetivo**: demais classes do provider.
- **Pré-requisitos**: 02.03.
- **Testes**: T-PRV-SQLITE-006..010, T-OUT-LIT-SQLITE.
- **Aceite**: suíte de contrato SQLite completa verde.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-02.05 — Suíte de contrato abstrata
- **Objetivo**: `ProviderContractTests<TFixture>` (26 §4) usada por todos os providers.
- **Pré-requisitos**: 02.04.
- **Aceite**: SQLite herda e passa; outros providers só precisam de fixture.
- **Evidências**: testes.
- **Riscos**: —.

## M2 — Catálogo e mapeamento

**Verificação do marco**: T-CAT-001..010 (exceto 008), T-MAP-001..008, T-MAP-CORPUS com metas; exemplo de importação passa.

### EPIC-04 — Catálogo interno

#### TASK-04.01 — Banco, pragmas e migrador
- **Objetivo**: `CatalogDatabase`, `CatalogMigrator`, `V0001__init.sql` (09 §3).
- **Pré-requisitos**: 00.04.
- **Testes**: T-CAT-001; migração com falha injetada mantém catálogo anterior.
- **Aceite**: DDL idêntico ao documento; `schema_version` com checksum.
- **Evidências**: dump `sqlite_schema` comparado ao doc.
- **Riscos**: JSON1 indisponível no bundle → verificar `json_valid` no teste.

#### TASK-04.02 — Repositórios
- **Objetivo**: repositórios de C03 (templates, campos, aliases, valores, registros, presets, regras, feedback, perfis de conexão sem segredos, histórico, execuções).
- **Pré-requisitos**: 04.01.
- **Testes**: CRUD; T-CAT-002 (embutidos).
- **Aceite**: invariantes de 09 §3 "Notas de integridade" aplicadas.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-04.03 — Staging: análise
- **Objetivo**: `StagingImporter.AnalyzeAsync` (13 §7, 09 §4).
- **Pré-requisitos**: 04.02, 03.02.
- **Testes**: T-CAT-003, T-CAT-004, T-SEC-009.
- **Aceite**: `examples/catalog-import-example.sql` analisado sem Errors; cada `CAT-IMP-*` coberto.
- **Evidências**: relatório de análise serializado.
- **Riscos**: script enorme → staging em arquivo temporário > 50 MB.

#### TASK-04.04 — Staging: merge e backup
- **Objetivo**: `MergeAsync` com modos (13 §7.3) + backup `premerge`.
- **Pré-requisitos**: 04.03.
- **Testes**: T-CAT-005, T-CAT-009.
- **Aceite**: merge atômico; content_hash recalculado.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-04.05 — Integridade, backup e restauração
- **Objetivo**: `ICatalogMaintenance`, `CatalogIntegrityChecker`.
- **Pré-requisitos**: 04.02.
- **Testes**: T-CAT-006, T-CAT-007.
- **Aceite**: restauração funcional; regra inválida desabilitada.
- **Evidências**: testes.
- **Riscos**: arquivos bloqueados por conexões abertas → fechar pool.

#### TASK-04.06 — Pack embutido (subconjunto MVP)
- **Objetivo**: `pack-core-ptbr.json` (MVP: NOME 200, SOBRENOME 150, EMAIL_DOMINIO 20, ENDERECO 200, UF 27, TELEFONE aliases + presets CPF/CNPJ quando geradores existirem) + `BuiltInPackInstaller` via staging.
- **Pré-requisitos**: 04.04.
- **Testes**: T-CAT-010.
- **Regras**: dados **autorais/sintéticos**; registrar fonte e licença de cada lista em `Seeds/SOURCES.md` (PA-009).
- **Aceite**: instalação idempotente.
- **Evidências**: `SOURCES.md`; contagens.
- **Riscos**: licença de dados → nunca copiar listas de terceiros sem licença compatível.

#### TASK-04.07 — Fonte de dados de templates
- **Objetivo**: `CatalogTemplateDataSource` → `TemplateDataSet` ordenado + `ContentHash` (C05 §2).
- **Pré-requisitos**: 04.02.
- **Testes**: T-CAT-009.
- **Aceite**: hash independente da ordem de inserção.
- **Evidências**: testes.
- **Riscos**: —.

### EPIC-05 — Mapeamento

#### TASK-05.01 — Normalizador de nomes
- **Objetivo**: `INameNormalizer` (14 §2).
- **Pré-requisitos**: 00.04.
- **Testes**: T-MAP-001 (tabela do doc + 200 casos).
- **Aceite**: 100% dos casos.
- **Evidências**: testes.
- **Riscos**: abreviações ambíguas → manter dicionário mínimo documentado.

#### TASK-05.02 — MappingKnowledge e binder estrutural
- **Objetivo**: índices de conhecimento + `IStructuralBinder` (14 §3).
- **Pré-requisitos**: 05.01, 01.02.
- **Testes**: T-MAP-002.
- **Aceite**: AC-060.1.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-05.03 — Pontuação, confiança, desempate, fallback, slots
- **Objetivo**: `IMappingEngine` completo (14 §4–7).
- **Pré-requisitos**: 05.02.
- **Testes**: T-MAP-003..006, T-MAP-008.
- **Aceite**: evidências e pontos idênticos ao documento.
- **Evidências**: snapshots de sugestões para a tabela `cliente` dos 4 digests.
- **Riscos**: calibração dos pontos — ajustes só com registro em 32 e atualização do 14.

#### TASK-05.04 — Corpus e métricas
- **Objetivo**: `mapping-corpus` (≥ 1.500 nomes) + T-MAP-CORPUS.
- **Pré-requisitos**: 05.03, 04.06.
- **Aceite**: ≥ 70% High; precisão ≥ 95%.
- **Evidências**: relatório de métricas.
- **Riscos**: metas não atingidas → ajustar aliases do pack (não os pesos) primeiro; registrar.

## M3 — Motor de geração e relacional

**Verificação do marco**: T-GEN-* (exceto BR), T-REL-001/002/005/006/007/012, T-REL-PLAN-001, T-VAL-LOJA, B-RNG-001, B-GEN-001 (meta preliminar ≥ 100 k linhas/s sem writer).

### EPIC-06 — Geração

#### TASK-06.01 — RNG determinístico
- **Objetivo**: `StreamKey`, `DeterministicRandom` (15 §4.1).
- **Pré-requisitos**: 00.04.
- **Testes**: T-GEN-RNG-001 (vetores do doc), T-GEN-RNG-002.
- **Aceite**: vetores idênticos.
- **Evidências**: testes.
- **Riscos**: overflow aritmético → usar `unchecked`.

#### TASK-06.02 — Infraestrutura de geradores
- **Objetivo**: registry, descriptors, validação de parâmetros, `ValueCoercer`, `WeightedIndex`, distribuições.
- **Pré-requisitos**: 06.01.
- **Contratos**: C05 §4.
- **Testes**: T-GEN-COERCE-001; distribuição normal/zipf.
- **Aceite**: tabela 15 §5 implementada.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-06.03 — Geradores básicos
- **Objetivo**: `core.*` (exceto expr/format/switch) da tabela 15 §6.
- **Pré-requisitos**: 06.02.
- **Testes**: T-GEN-LIB-<id>, T-GEN-DET-001, T-GEN-NULL-001.
- **Aceite**: snapshots aprovados.
- **Evidências**: `.verified.txt` por gerador.
- **Riscos**: —.

#### TASK-06.04 — Geradores de catálogo e slots
- **Objetivo**: `catalog.template`, `catalog.template.field`, `RowContext` slots.
- **Pré-requisitos**: 06.03, 04.07.
- **Testes**: T-GEN-COMP-001, AC-052.1.
- **Aceite**: coerência de slots.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-06.05 — MDS-Expr
- **Objetivo**: lexer/parser/avaliador + funções 15 §9.2 (exceto `parent`, que depende de 07.03) + `core.format`/`core.expr`.
- **Pré-requisitos**: 06.04.
- **Testes**: T-GEN-EXPR-001..003.
- **Aceite**: gramática EBNF coberta; erros com posição.
- **Evidências**: testes.
- **Riscos**: escopo crescer → somente funções listadas.

#### TASK-06.06 — Unicidade
- **Objetivo**: Feistel, base mista, estratégias 1–5, capacidade (15 §8).
- **Pré-requisitos**: 06.03.
- **Testes**: T-GEN-UQ-001..004.
- **Aceite**: AC-075.1..3.
- **Evidências**: testes + medição de memória.
- **Riscos**: —.

#### TASK-06.07 — Engine e preview
- **Objetivo**: `GenerationEngine` (15 §2–3) com `IRowSink`, chunks paralelos ordenados, preview.
- **Pré-requisitos**: 06.05, 06.06, 07.03.
- **Testes**: T-GEN-DET-002/003, B-GEN-001.
- **Aceite**: saída idêntica com MaxDOP 1 e 8.
- **Evidências**: hash das saídas.
- **Riscos**: ordenação de chunks → buffer por índice.

### EPIC-07 — Planejamento relacional e validação

#### TASK-07.01 — Grafo e SCC
- **Objetivo**: `DependencyGraphBuilder`, Tarjan iterativo (16 §1–2).
- **Pré-requisitos**: 01.02.
- **Testes**: T-REL-001.
- **Aceite**: ordem determinística.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-07.02 — Ciclos e autorreferência (MVP)
- **Objetivo**: `CycleResolver` com NullThenUpdate + bloqueio; autorreferência anulável (16 §3–4).
- **Pré-requisitos**: 07.01.
- **Testes**: T-REL-002 (unitário; integração no M4), T-REL-005, T-REL-006.
- **Aceite**: fixtures `cycles`/`selfref`.
- **Evidências**: snapshots de plano.
- **Riscos**: busca exaustiva — limite 12 arestas.

#### TASK-07.03 — Chaves e KeyOracle (Explicit)
- **Objetivo**: `KeyPlanner` (16 §5.1–5.2, Explicit/DatabaseDefault), `KeyOracle`.
- **Pré-requisitos**: 07.01.
- **Testes**: T-REL-012.
- **Aceite**: faixas, overflow, offline.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-07.04 — Cardinalidade ChildDriven e FKs compostas
- **Objetivo**: `rel.fk` ChildDriven Uniform, FK composta, sobreposição, `rel.child_seq` (16 §6.1, §7).
- **Pré-requisitos**: 07.03.
- **Testes**: T-REL-007.
- **Aceite**: AC-083.1.
- **Evidências**: testes.
- **Riscos**: sobreposição de FKs → `PLN-FK-OVERLAP` claro.

#### TASK-07.05 — Planner completo e PlanHash
- **Objetivo**: `GenerationPlanner.Plan` com diagnósticos (16 §10), passos, estimativas, `PlanHash` (15 §4.5).
- **Pré-requisitos**: 07.02, 07.04.
- **Testes**: T-REL-PLAN-001.
- **Aceite**: snapshot do plano `loja`.
- **Evidências**: `.verified.txt`.
- **Riscos**: —.

#### TASK-07.06 — Validador L2/L3
- **Objetivo**: `PreflightValidator` com regras de 17 §2 e interpretador de CHECK (17 §4).
- **Pré-requisitos**: 07.05, 06.07.
- **Testes**: T-VAL-001..040 (L1–L3), T-VAL-LOJA, T-VAL-CHECK-001.
- **Aceite**: AC-092.1 (parte sem L4).
- **Evidências**: matriz caso × código.
- **Riscos**: conversão de regex SIMILAR TO/GLOB → Unsupported quando incerto.

## M4 — Saída SQL, PostgreSQL, Firebird, UI mínima (MVP)

**Verificação do marco = Gate do MVP** ([29 §2](29-acceptance-criteria.md)).

### EPIC-08 — Saída e providers PG/FB

#### TASK-08.01 — SqlScriptWriter
- **Objetivo**: writer (18 §1–8) com `ISegmentedTextSink` (segmentação ativa), cabeçalho, transações, resync, limpeza (cleanup pode ficar para 15.04 no V1 — no MVP gerar apenas script principal).
- **Pré-requisitos**: 06.07, 02.04.
- **Testes**: T-OUT-001 (SQLite), T-OUT-002, T-OUT-006.
- **Aceite**: script SQLite `loja` executa em banco limpo.
- **Evidências**: script de exemplo versionado em `tests/fixtures/expected/`.
- **Riscos**: —.

#### TASK-08.02 — Provider PostgreSQL
- **Objetivo**: provider completo (10 §5, 11).
- **Pré-requisitos**: 02.05, 08.01.
- **Testes**: T-PRV-PG-001..011, T-PRV-PG-COPY-001 (bulk pode ser INSERT multi-linha no MVP; COPY no V1), T-OUT-LIT-PG, T-OUT-ROUNDTRIP-PG.
- **Aceite**: suíte de contrato PG 17 verde.
- **Evidências**: logs.
- **Riscos**: `format_type` vs tipo curto.

#### TASK-08.03 — Provider Firebird
- **Objetivo**: provider completo (10 §6, 11 §4.4).
- **Pré-requisitos**: 02.05, 08.01.
- **Testes**: T-PRV-FB-001..011, T-PRV-FB-KEY-001..003, T-OUT-LIT-FB, T-OUT-ROUNDTRIP-FB.
- **Aceite**: suíte de contrato FB 5 verde; FB 3 e 4 executados e resultados registrados.
- **Evidências**: logs por versão.
- **Riscos**: semântica de `RESTART WITH`; CHECK via `RDB$TRIGGERS`.

#### TASK-08.04 — E2E de motor (sem UI)
- **Objetivo**: T-E2E-<X>-001 para SQLite/PG/FB e T-E2E-003 via Application (testes de integração).
- **Pré-requisitos**: 08.02, 08.03, 09.02.
- **Aceite**: verde.
- **Evidências**: logs + hash dos scripts.
- **Riscos**: —.

### EPIC-09 — Projeto, Application, UI MVP

#### TASK-09.01 — Projects
- **Objetivo**: `MdsProject`, serializer, validator (22).
- **Pré-requisitos**: 01.04.
- **Testes**: T-PRJ-001/002/003/005/006/010.
- **Aceite**: `examples/project-loja.mdsproj` abre e reserializa igual.
- **Evidências**: diff vazio.
- **Riscos**: —.

#### TASK-09.02 — Serviços de Application (MVP)
- **Objetivo**: `ConnectionService`, `DigestService`, `MappingService`, `GenerationService` (sem DirectInsert multi-SGBD), `ReferenceDataSource` (Generated/Fixed).
- **Pré-requisitos**: 09.01, 05.03, 07.06, 08.01.
- **Testes**: T-APP-* unitários com fakes.
- **Aceite**: fluxo completo testável sem UI.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-09.03 — Infraestrutura
- **Objetivo**: `DpapiSecretStore`, settings, Serilog com redação.
- **Pré-requisitos**: 04.02.
- **Testes**: T-SEC-002/004/012.
- **Aceite**: segredos nunca em claro.
- **Evidências**: testes.
- **Riscos**: —.

#### TASK-09.04 — Shell, DI, tema
- **Objetivo**: Shell (21 §3), tokens (21 §2.1), DI root, navegação.
- **Pré-requisitos**: 09.02.
- **Testes**: T-UI-A11Y-001 (contraste dos tokens).
- **Aceite**: janela com painéis redimensionáveis e persistidos.
- **Evidências**: capturas claro/escuro.
- **Riscos**: —.

#### TASK-09.05 — Tela de conexões (básica)
- **Objetivo**: S02 com SQLite/PG/FB, teste, ambiente, ReadOnly.
- **Pré-requisitos**: 09.04, 09.03.
- **Testes**: parte de T-UI-FLOW-001.
- **Aceite**: AC-001.1, AC-002.1/2.
- **Evidências**: capturas.
- **Riscos**: —.

#### TASK-09.06 — Projeto, mapeamento e gerador (básico)
- **Objetivo**: S10, S11, S12 (sem compostos avançados; slots visíveis).
- **Pré-requisitos**: 09.05.
- **Testes**: T-UI-FLOW-001.
- **Aceite**: usuário mapeia `loja` por teclado.
- **Evidências**: gravação/capturas.
- **Riscos**: geração de formulário a partir de descriptors.

#### TASK-09.07 — Plano, validação, preview e geração de script
- **Objetivo**: S14, S15, S16 (somente script).
- **Pré-requisitos**: 09.06.
- **Testes**: T-UI-FLOW-001, T-UI-PERF-002.
- **Aceite**: fluxo MVP completo.
- **Evidências**: script gerado + capturas.
- **Riscos**: —.

---

## V1 — M5 a M10

**Verificação de cada marco V1**: testes listados das tarefas + regressão completa (unit, contrato nos SGBDs oficiais da versão principal) + checklist de [29 §3](29-acceptance-criteria.md) correspondente.

### EPIC-10 — MySQL (M5)
- **TASK-10.01 — Provider MySQL (conexão, tipos, extractor)** · Objetivo: 10 §4, 11 · Arquivos: `Providers.MySql/*` · Pré: 02.05 · Contratos: C02 · Passos: allow-list, `tinyint1IsBool`, `sql_mode` lido, enum parse · Regras: `AllowLoadLocalInfile=false` · Testes: T-PRV-MYSQL-001..005/011 · Aceite: digest `loja` igual ao exemplo · Evidências: `.verified.json` · Riscos: `COLUMN_DEFAULT` sem aspas.
- **TASK-10.02 — Dialeto/literais/escapes MySQL** · Pré: 10.01 · Testes: T-PRV-MYSQL-ESC-001, T-OUT-LIT-MYSQL, T-OUT-ROUNDTRIP-MYSQL · Aceite: round-trip com e sem `NO_BACKSLASH_ESCAPES` · Evidências: logs · Riscos: escapes de `\0`/`\Z`.
- **TASK-10.03 — Inspector, bulk, erros MySQL** · Pré: 10.02 · Testes: T-PRV-MYSQL-006..010 · Aceite: contrato verde em 8.4 e 9.x · Evidências: logs · Riscos: `max_allowed_packet`.
- **TASK-10.04 — E2E MySQL** · Pré: 10.03 · Testes: T-E2E-MYSQL-001, T-REL-SELF-NN-MYSQL · Aceite: verde; resultado de SELF-NN registrado em 32 · Evidências: logs.

### EPIC-11 — Conexões e segurança completas (M5)
- **TASK-11.01 — ExecutionGuard e matriz** · Objetivo: 23 §5 · Arquivos: `Application/Security/*` · Pré: 09.02 · Testes: T-SQL-GUARD-001, T-SEC-005 · Aceite: matriz completa · Evidências: tabela de casos.
- **TASK-11.02 — Somente leitura no servidor** · Objetivo: 20 §8 · Pré: 11.01 · Testes: T-SEC-006 nos 4 SGBDs · Aceite: DML falha no servidor mesmo contornando a guarda (teste chama o executor direto) · Riscos: SQLite `Mode=ReadOnly` exige reabrir conexão.
- **TASK-11.03 — Configurações avançadas e resiliência** · Objetivo: FR-005, FR-006 · Testes: T-PRV-<X>-009, T-SQL-EXEC-002 · Aceite: AC-005.*, AC-006.*.
- **TASK-11.04 — Fingerprints e alerta de divergência** · Objetivo: 10 §8, FR-007 · Testes: T-SEC-003 · Aceite: AC-007.2.

### EPIC-12 — Explorer (M5)
- **TASK-12.01 — Árvore e carga lazy** · S03 · Testes: T-UI-PERF-001 · Aceite: AC-010.1/2 · Riscos: virtualização de TreeView (usar `VirtualizingStackPanel` + dados planos se necessário).
- **TASK-12.02 — Inspetor de tabela e referências** · S05 · Testes: T-UI-FLOW-003 · Aceite: AC-011.1, AC-012.1.
- **TASK-12.03 — Ações rápidas e refresh** · FR-013/014/015 · Aceite: AC-013.1, AC-014.*.

### EPIC-13 — Editor SQL (M6)
- **TASK-13.01 — QuerySession e QueryExecutionService** · Objetivo: transações Manual/Auto, paginação, cancelamento (20 §7) · Testes: T-SQL-EXEC-001/002 · Aceite: AC-024.*, AC-026.1, AC-027.*.
- **TASK-13.02 — Motor de autocomplete** · 20 §6 · Testes: T-SQL-COMP-001, B-SQL-001 · Aceite: AC-021.1, AC-022.*.
- **TASK-13.03 — UI do editor (AvalonEdit, xshd, abas, recuperação)** · S04 · Testes: T-UI-FLOW-003 · Aceite: AC-020.* · Riscos: compatibilidade AvalonEdit/.NET 10 (PV).
- **TASK-13.04 — Resultados, exportação, histórico, erros com posição** · FR-025/029/030/031 · Testes: T-UI-PERF-003, T-SQL-HIST-001, T-OUT-005 · Aceite: AC-025.1, AC-029.1, AC-030.1, AC-031.1.

### EPIC-14 — Projetos avançados e evolução (M7)
- **TASK-14.01 — Snapshots, diff UI (S06)** · Testes: T-DIG-005/006 via UI · Aceite: AC-044.1, AC-045.1.
- **TASK-14.02 — Migração de bindings** · 12 §9 · Testes: T-DIG-030, T-E2E-004 · Aceite: AC-046.*.
- **TASK-14.03 — Múltiplos perfis, migração de formato, autosave, exportação sanitizada, confiança** · 22 · Testes: T-PRJ-004/007, T-SEC-008 · Aceite: AC-111.1, AC-112.2, AC-113.1, AC-115.1.
- **TASK-14.04 — Dependências de catálogo** · FR-116 · Testes: T-PRJ-007 · Aceite: AC-116.1.

### EPIC-15 — Inserção direta (M8)
- **TASK-15.01 — Autorização e fluxo de confirmação** · 19 §2, 23 §5.3 · Testes: T-SEC-001/003/011 · Aceite: AC-095.1.
- **TASK-15.02 — Bulk inserters (PG COPY, MySQL multi-row, FB prepared/batch, SQLite)** · 19 §4 · Testes: T-PRV-<X>-006, T-PRV-PG-COPY-001, B-DIR-<X> · Aceite: metas registradas (ajustadas se necessário em 32) · Riscos: PV-002.
- **TASK-15.03 — DirectInsertWriter, transações, falhas, cancelamento** · 19 §5–7 · Testes: T-DIR-<X>-001, T-DIR-002/003/005 · Aceite: AC-095.2, AC-096.1.
- **TASK-15.04 — Captured + KeyRegistry** · 16 §5 · Testes: T-DIR-004 · Aceite: FK de filhos usa chaves capturadas.
- **TASK-15.05 — Limpeza, relatório, histórico, interrompidas** · 18 §7, 19 §8, 25 §6 · Testes: T-OUT-004, T-E2E-<X>-002 · Aceite: AC-099.1, AC-114.*.

### EPIC-16 — Relacional avançado e L4 (M8)
- **TASK-16.01 — Deferred/DisableChecks/self NOT NULL** · 16 §3–4 · Testes: T-REL-003/004, T-REL-SELF-NN-<X> · Aceite: AC-084.*.
- **TASK-16.02 — ParentDriven, Zipf, Poisson, 1:1** · 16 §6 · Testes: T-REL-008/009 · Aceite: AC-082.*.
- **TASK-16.03 — Fontes Existing/Dataset/Query + verificação no destino** · 16 §8–9 · Testes: T-REL-010/011, T-SEC-007 · Aceite: AC-081.1, AC-087.1.
- **TASK-16.04 — NaturalKeyLookup** · 16 §5.1 · Testes: T-REL-PLAN-002 (novo) · Aceite: script com subconsultas executa.
- **TASK-16.05 — Inserção de teste (L4)** · 17 §6 · Testes: T-VAL-TRIAL-001/002, T-VAL-001..040 completo · Aceite: AC-092.1 (40/40).

### EPIC-17 — Catálogo V1 (M9)
- **TASK-17.01 — UI Catálogo e editor de template (S07/S08)** · Aceite: AC-050.1, AC-053.* via UI.
- **TASK-17.02 — UI de importação (S09) + instruções para IA** · Aceite: AC-054.* via UI.
- **TASK-17.03 — Packs JSON** · Schema `template-pack-1.0` · Testes: T-CAT-008 · Aceite: AC-055.1.
- **TASK-17.04 — Presets, aprendizado e diagnóstico de mapeamento** · FR-059/064/065 · Testes: T-MAP-007 · Aceite: AC-059.1, AC-064.1, AC-065.1.
- **TASK-17.05 — Geradores BR, switch, lorem, uuid v7** · 15 §6 · Testes: T-GEN-BR-001, T-GEN-LIB-* · Aceite: AC-076.1, AC-073.1.
- **TASK-17.06 — Pack CORE_PTBR completo** · 09 §6 · Testes: T-CAT-010, T-MAP-CORPUS (reavaliar) · Aceite: AC-056.*.

### EPIC-18 — Volume e formatos (M10)
- **TASK-18.01 — Manifesto, segmentação completa, guardas no script** · 18 §6, 16 §5.2 · Testes: T-OUT-002/003 · Aceite: AC-094.1.
- **TASK-18.02 — CSV e JSON Lines** · 18 §9 · Testes: T-OUT-005 · Aceite: AC-100.1.
- **TASK-18.03 — Benchmarks e testes de volume** · 24 · Testes: B-*, T-GEN-VOL-001 · Aceite: metas NFR-PERF atendidas ou desvio registrado com plano.

### EPIC-19 — Hardening e release (M10)
- **TASK-19.01 — Acessibilidade e DPI** · 21 §7 · Testes: T-UI-A11Y-*, T-UI-DPI-001.
- **TASK-19.02 — Paleta de comandos, atalhos, configurações** · FR-120/122.
- **TASK-19.03 — Pacote de diagnóstico e logs** · FR-121, 25 §5 · Testes: T-SEC-004.
- **TASK-19.04 — Instalador, licenças, verificação offline** · NFR-INST-001, NFR-LIC-001, T-SEC-NET-001 · Decisão DP-003.
- **TASK-19.05 — Manual do usuário local (F1)** · Markdown→HTML embutido.

## V2 — Épicos (detalhar antes de iniciar)
EPIC-20 CLI · EPIC-21 Plugins · EPIC-22 Grafo de relacionamentos · EPIC-23 Designer de cenários · EPIC-24 Regras condicionais declarativas · EPIC-25 Perfil estatístico · EPIC-26 Anonimização restrita · EPIC-27 Perfis de carga e cobertura · EPIC-28 Pacotes temáticos/.mdspkg · EPIC-29 MariaDB oficial · EPIC-30 Coerência entre tabelas.

## Ordem recomendada (caminho crítico)
`00.01 → 00.04 → 01.01 → 01.02 → 03.01 → 02.01 → 01.03 → 01.04 → 03.02 → 02.02 → 02.03 → 02.04 → 02.05 → 01.05 → 01.06 → 04.01 → 04.02 → 04.03 → 04.04 → 04.07 → 05.01 → 05.02 → 05.03 → 04.06 → 05.04 → 06.01 → 06.02 → 06.03 → 06.04 → 06.05 → 06.06 → 07.01 → 07.03 → 07.02 → 07.04 → 06.07 → 07.05 → 07.06 → 08.01 → 09.01 → 08.02 → 08.03 → 09.02 → 08.04 → 09.03 → 09.04 → 09.05 → 09.06 → 09.07` (00.02, 00.03, 00.05 em paralelo logo após 00.01; 04.05 após 04.02).
