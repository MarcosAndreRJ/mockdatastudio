# 32 — Decisões, Premissas, Questões Abertas e Verificações Pendentes

| Campo | Valor |
|---|---|
| Objetivo | Registro central de ambiguidades e sua classificação; fonte para a IA Executora saber o que é firme, o que é hipótese e o que exige aprovação. |
| Escopo | Produto inteiro. |
| Regras | Toda nova decisão/premissa da IA Executora é registrada aqui (ou em ADR, se arquitetural) — [33 §8](33-executor-instructions.md). |

Classificação (briefing §17):
- **DA — Decisão autônoma**: resolvida por engenharia; pode ser revista com justificativa.
- **PA — Premissa documentada**: hipótese explícita; se falsa, impacto descrito.
- **DP — Decisão de produto**: requer aprovação do responsável; há um padrão provisório que não bloqueia o desenvolvimento.
- **BT — Bloqueio técnico**: impede especificação/implementação.
- **PV — Verificação pendente**: fato técnico a confirmar empiricamente ou em fonte oficial.

### Numeração na execução paralela (a partir de 2026-10-12)

Várias IAs trabalham em branches diferentes ([PLANO-DE-ACAO.md](../PLANO-DE-ACAO.md)). Para que dois registros nunca recebam o mesmo número, cada trilha usa uma faixa própria e acrescenta linhas **somente** na subseção da sua trilha (§1.1–1.4, §5.1–5.4, §6.1–6.4):

| Trilha | DA | PV | PA |
|---|---|---|---|
| Base (até a auditoria de 2026-10-12) | DA-001..099 | PV-001..099 | PA-001..099 |
| A — Motor, planejamento e saída | DA-100..199 | PV-100..199 | PA-100..199 |
| B — Providers, SQL e testes de integração | DA-200..299 | PV-200..299 | PA-200..299 |
| C — Aplicação, projeto e interface | DA-300..399 | PV-300..399 | PA-300..399 |
| Integração/gates (sprints de gate) | DA-400..499 | PV-400..499 | PA-400..499 |

Decisões de produto (DP) e bloqueios técnicos (BT) seguem numeração única. Uma IA que precise de um DP **não o numera**: registra a questão como PV da sua faixa, com a etiqueta "requer DP", e o responsável promove a DP.

## 1. Decisões autônomas (DA)

| ID | Decisão | Referência |
|---|---|---|
| DA-001 | .NET 10 LTS, WPF, MVVM Toolkit, monólito modular | ADR-001/002/015 |
| DA-002 | ADO.NET para bancos externos; SQLite+Dapper para catálogo | ADR-003/004 |
| DA-003 | Motor de geração próprio; RNG por contador; MDS-Expr próprio | ADR-005/013/014 |
| DA-004 | Chaves Explicit por padrão | ADR-008 |
| DA-005 | Importação de catálogo via staging isolado | ADR-016 |
| DA-006 | `Unknown` tratado como `Production` para proteções; ambiente padrão de perfil novo = `Unknown` | 23 §5.1 |
| DA-007 | Presets de gerador são mapeáveis como templates (unificação da experiência) | 13 §1 |
| DA-008 | Namespace virtual `PUBLIC` para Firebird 3–5 | 08 §2.1 |
| DA-009 | Hash estrutural ignora nomes de constraints/índices e ordem de colunas | 12 §6 |
| DA-010 | Colunas no digest como tuplas posicionais; chaves curtas apenas em objetos folha | 12 §2–3 |
| DA-011 | `tinyint(1)` = Boolean no MySQL (configurável) | 10 §4 |
| DA-012 | SQLite: `foreign_keys=ON` por padrão nas conexões do app | 10 §3 |
| DA-013 | Sem biblioteca de docking no V1 | 06 §3.4 |
| DA-014 | `NormalizedName` com `Compact` (literal) e `ExpandedCompact` (abreviações expandidas) | 14 §2, C04 |
| DA-015 | Tipos de dados puros (perfis de conexão, capacidades, bindings, perfis de geração, dados de template) residem em `Core` para respeitar AR-02 | C02, C05, C06 |
| DA-016 | `Providers.*` e `Catalog` podem referenciar `SqlTooling` | 05 AR-04 |
| DA-017 | Validação de JSON Schema em runtime por código; validador de referência só em testes | 07 §2 |
| DA-018 | Firebird: um registro por INSERT em script; `COMMIT` a cada 5.000 | 18 §4–5 |
| DA-019 | Paralelismo apenas dentro de tabela no V1 | 24 §4 |
| DA-020 | Unicidade composta bijetiva usa componentes uniformes (perde pesos) | 15 §8.4 |
| DA-021 | Template de e-mail usa somente domínios fictícios/reservados | 23 §10 |
| DA-022 | MVP com SQLite+PostgreSQL+Firebird; MySQL no M5 | 27 §2 |
| DA-023 | Migração da solução para `MockDataStudio.slnx` nativo do .NET 10 | 07 §1, Sprint 0.3 |
| DA-024 | Separação de `SqlEditorView` e `ExplorerView` no Sprint 1 | docs/ui-inventory.md, Sprint 0.2 |
| DA-025 | Adição de logotipo próprio para MySQL (`mysql.png`), mantendo `mariadb.png` | docs/ui-inventory.md, Sprint 0.2 |
| DA-026 | Externalização dos textos da interface para `Strings.resx` (pt-BR padrão) preservando visual dos mockups | docs/ui-inventory.md, Sprint 0.2 |
| DA-027 | Tokens de cor de docs/21 §2.1 (`Themes/Tokens.xaml`) com os valores dos mockups; divergências de ambiente (mockup usa Development/QA/Local/Production, doc usa Development/Test/Staging/Production) resolvidas a favor do mockup. Tema escuro pendente | docs/21 §2.1, Sprint 1.5 |
| DA-028 | "SQL Editor" é página própria (`SqlEditorPageView`) composta pelos mesmos controles que o Explorer usa (`SqlEditorView`, `SqlResultsView`); o Explorer mantém o layout do mockup | docs/ui-inventory.md (DA-024), Sprint 1.4 |
| DA-029 | Igualdade estrutural do modelo por forma canônica (serialização do digest) em vez de `EquatableArray<T>`: os records mantêm `IReadOnlyList`, e os testes comparam a forma serializada (TASK-01.02, risco documentado) | Sprint 2 |
| DA-030 | `DigestValidationError` ganha `Severity` (aviso × erro, 12 §7) e `TriggerModel.Table` é `ObjectName?` (08 §2.2), ajustando o C01 de forma compatível | Sprint 2, contratos C01 |
| DA-031 | Digest: validação codificada (DA-017) com `JsonDocument`; leitura e escrita via `JsonNode` (mesma saída de 12 §4); tipos nativos → `LogicalType` por inferência genérica (`NativeTypeParser`), que os providers podem substituir pelo `ITypeMapper` | Sprint 2, 12 §3–4 |
| DA-032 | Hash v1: normalização de expressões sem parser SQL (AR-01): espaços só são removidos junto de `(`, `)` e `,`; operadores e caixa de identificadores citados são preservados. Os hashes dos exemplos foram calculados com esta regra | Sprint 2, 12 §6 |
| DA-033 | `IMdsModule` segue docs/31 §2 (`Id`, `ContractVersion`, `Register(IServiceCollection)`), sem `CreateProviders`; contratos ficam em arquivos por tipo no namespace `MockDataStudio.Providers` (modelos em `.Models`); `IDatabaseProvider` e `IProviderRegistry` acrescentados; `ProviderRegistry` recebe a lista de providers e a composição fica na raiz de DI | Sprint 3, C02, docs/31 §2 |
| DA-034 | `ConnectionEnvironment` (existente em Core) é usado no lugar de `EnvironmentTag` do C02, mesma semântica | Sprint 3, C02 |
| DA-035 | `SqlTooling` não conhece SGBDs (T-ARC-007): comportamento léxico/divisão vem de `SqlDialectFeatures` (flags e regra de início de rotina), com instâncias para SQLite, MySQL, PostgreSQL e Firebird. Lexer, splitter e classificador recebem o dialeto; o provider informa o seu | Sprint 3, 20 §4, T-ARC-007 |
| DA-036 | Digest SQLite (extrator): namespace `main` com `virtual` omitido (padrão `false`, 12 §3); extras de tabela só `withoutRowid` e `strict` com valor `"1"` (12 §6); UNIQUEs sem nome antes dos nomeados; `options.indexes` omitido quando `true` (padrão). Correções em `docs/examples/digest-sqlite.json`: tabelas em ordem por nome (12 §138) e remoção de `x.strict="0"` de `produto`, valor fora da spec (12 §6, `strict` só como `"1"`). O hash não mudou | Sprint 3, 12 §3, §6, §138, 28 TASK-02.03 |
| DA-037 | `dateStorage` (perfil) é aplicado por `SqliteProvider.CreateDialect(dateStorage)` e, no bulk e no inspector, pela conexão aberta (`OpenConnectionAsync`); `GetDialect(versão)` usa `text`. Integração do dialeto de script com o perfil fica para o ScriptWriter (Sprint 10) | Sprint 3, 10 §3 |
| DA-038 | Novos códigos `PRV-CANCELLED`, `PRV-SQL-SYNTAX` e `PRV-SQL-ERROR`, com mensagens em `Strings.resx`, para as categorias de erro que não tinham código | Sprint 3, 10 §7 |
| DA-039 | T-PRV-SQLITE-011 (introspecção parcial sem permissão) e o caso DataTooLong de T-PRV-SQLITE-008 são N/A no SQLite (sem permissões de catálogo; VARCHAR(n) não é imposto pelo engine). Registrados como `Skip` com o motivo, não como verdes | Sprint 3, 26 §4 |
| DA-040 | Normalizador (14 §2 passo 6): prefixo de tipo (DS, NM, VL, DT, FL, ID, TX, ST, IN, CD, NR, NUM, CODIGO) é ruído só na primeira posição; sufixo ID/FK/PK só na última; DATA e NUM expandidos são ruído; tokens da tabela (e singular) são ruído. Ruído nunca remove todos os tokens (mantém-se o nome). `E`+`MAIL` é fundido no nível bruto. Números viram `Ordinal` | Sprint 5, 14 §2 |
| DA-041 | Evidências `HistoryMatch` (14 §5), `CheckConstraintHint` (CHECK `IN` com valores do template) e `UniqueCapacity` (ValueCount vs linhas do perfil) não implementadas: dependem de feedback (Sprint 17), de conhecimento de valores no C04 e do perfil de geração. Escore atual omite esses ajustes | Sprint 5, 14 §5, TASK-17.04 |
| DA-042 | Faixa de confiança: o melhor candidato usa margem para o 2º (14 §7); os demais usam a diferença para o melhor (`score − score[0]`, sempre ≤ 0), o que os mantém fora de High. Fallback por tipo recebe escore 40 (piso da faixa Low) | Sprint 5, 14 §7 |
| DA-043 | Corpus de mapeamento: `corpus-core-ptbr.csv` é sintético, gerado de `generate-corpus.js` a partir dos aliases do pack CORE_PTBR (estilos de nome, prefixos de ruído, tabelas e negativos). Não é corpus real anonimizado (14 §11). Rótulo de `UF`/`ENDERECO.uf` aceita os dois alvos (docs/14 §7, ambiguidade real). Métricas medidas: cobertura High 48,1%, precisão High 100%, 0 violações de tipo | Sprint 5, 14 §11, TASK-05.04 |
| DA-044 | Meta de cobertura High do corpus revisada (decisão do responsável, PV-018 opção a): 70% passa a valer para nomes **sem prefixo de tipo** (14 §2 passo 6). Nomes com prefixo são medidos e reportados em `TestResults/mapping-corpus-metrics.json` (`coberturaHighSemPrefixo` e `coberturaHigh`), sem meta. Calibração de pesos (14 §5) inalterada. Resultado: cobertura sem prefixo 86,2% (1.044 positivos), cobertura total 48,1%, precisão High 100% | Sprint 5, 14 §11, PV-018 |
| DA-045 | `StreamKey.Child` usa `hex16(parent)` em maiúsculas (`X16`): a especificação não fixa a caixa; os vetores de §4.1 estão em maiúsculas | Sprint 6, 15 §4.1 |
| DA-046 | `GenerationCell.Rng` é campo público (CA1051 suprimido): struct `ref` não pode devolver referência ao próprio campo, e uma propriedade copiaria o estado do RNG a cada uso. Detectado por teste de avanço do estado | Sprint 6, 15 §4.1 |
| DA-047 | Unicidade de UUID v4 usa tentativas (estratégia 5), não construtiva (estratégia 1): o domínio é ilimitado e não indexável, então não há permutação. Colisão de 122 bits é desprezível, mas a garantia é probabilística com teto de 16 tentativas | Sprint 6, 15 §8.2 |
| DA-048 | Funções de MDS-Expr `template()`, `field()` e `pick()` usam o fluxo de valor da coluna, não o `expr:<posição>` de §4.2. Ambos são determinísticos; o fluxo dedicado fica para a revisão de versão do gerador (15 §4.4) | Sprint 6, 15 §4.2 |
| DA-049 | Pendências declaradas da Sprint 6: distribuições `normal` e `zipf`; `core.lorem.*`; `core.weighted` e `core.switch`; geradores `br.*`; `rel.*` (Sprint 7); estratégia 4 (sufixo, T-GEN-UQ-003 em `Skip`); `weekdays`/`hours` em `core.date.range`; passo de data com anos/meses; funções `cpf`, `cnpj` e `parent`. T-GEN-UQ-001 varre N ≤ 4.096 e N ∈ {65.535, 65.536, 1.000.003}, não todo N ≤ 65.536 (custo) | Sprint 6, 15 §6, §8 |
| DA-050 | Snapshots de `T-GEN-LIB` (`tests/fixtures/generation-snapshots/*.v1.txt`) são gravados pela própria implementação e revisados como arquivos versionados: detectam regressão, não provam a especificação. O teste de semântica do UUID v4 foi acrescentado porque o snapshot inicial registrou um bug de versão | Sprint 6, 26 §5.5 |
| DA-051 | Parâmetros numéricos aceitam `long`, `double` e `decimal` no JSON do binding (representação não é fixada pelo contrato); a leitura normaliza para o tipo pedido | Sprint 6, C05 §4 |
| DA-052 | A API de `IRowSink` e `RowBatch` foi convergida com C06 na Sprint A1 (`FirstRowIndex`, `BeginTableAsync`, `EndTableAsync`, `ExecuteSessionStepAsync`, `WriteDeferredUpdatesAsync` e tipos `PlanStep` implementados). A exposição de `IGenerationEngine.RunAsync(GenerationPlan, …)` ocorre na Sprint A2, mantendo a execução por tabela interna | Sprint A1 (SAN-A.2), C06 |
| DA-053 | As fixtures `tests/fixtures/schemas/loja.<engine>.sql` passam a ser **a mesma estrutura** de `docs/examples/digest-<engine>.json` (nomes no singular, tabela `entrega` com FK composta). A fixture SQLite deriva de `docs/examples/schema-loja-sqlite.sql`. A divergência atual (plural, sem `entrega`) é defeito a corrigir na Sprint B1 | Auditoria 2026-10-12 (docs/37 A-02) |
| DA-054 | Execução paralela: três trilhas (A, B, C) com posse de diretórios, uma branch por sprint, merge em `main` ao fim da sprint, faixas de numeração por trilha (tabela acima) | PLANO-DE-ACAO.md, docs/33 §18 |
| DA-055 | Teste que não pode executar no ambiente (sem Docker, sem sessão interativa) deve terminar como **Skip** com motivo, nunca como aprovado. Afeta `ContainerSmokeTests` e as suítes de integração e de UI | Auditoria 2026-10-12 (docs/37 A-03), docs/26 §1 |

### 1.1 Trilha A — Motor, planejamento e saída (DA-100..199)

| ID | Decisão | Referência |
|---|---|---|
| DA-100 | `DependencyGraphBuilder.Build` recebe `TableSettings` (origem de cada FK) em vez de `bindings`, como descrito em C06. Os bindings não alteram o grafo; a origem da FK é que decide se gera aresta (16 §1) | C06; 16 §1; `DependencyGraph.cs` |
| DA-101 | Tarjan iterativo (sem recursão) com nós e arestas ordenados por `CanonicalId`. Os componentes saem com pais antes dos filhos e cada componente sai ordenado | 16 §2; `DependencyGraph.cs` |
| DA-102 | Faixa de chave Explicit: `start = max(MAX, sequence, 0) + 1 + KeyOffset` (ou `KeyStartOverride`). Overflow usa a largura inteira do tipo (`IntegerBits`); tipo sem largura conhecida não tem checagem de overflow | 16 §5.2; `KeyPlanner.cs` |
| DA-103 | Resolução de ciclos: busca exaustiva (menor conjunto de arestas, desempate por custo da estratégia) até 12 arestas candidatas; acima disso, guloso que só remove arestas dentro de um componente cíclico restante. A heurística gulosa pode não ser mínima | 16 §3; `CycleResolver.cs` |
| DA-104 | `GenerationPlan` não inclui `SessionSetupStep`/`SessionTeardownStep`: as instruções dependem do dialeto e entram na A3. `PlanHash` é SHA-256 do texto canônico do plano. Autorreferência só gera o diagnóstico `PLN-SELF-REF`; a geração é responsabilidade do gerador | C06; 16 §4; `GenerationPlanner.cs` |
| DA-105 | A3 iniciada com A2 parcial e sem B1 concluída, por decisão do responsável em 2026-10-12. O writer implementa `IRowSink` (Generation) e é testado com linhas montadas à mão sobre o plano do `GenerationPlanner`; a integração com `IGenerationEngine.RunAsync(GenerationPlan)` (A2.5) fica para depois | PLANO A3 |
| DA-106 | Constantes por SGBD saíram do writer para `ISqlDialect` (T-ARC-007 proíbe ramificar por `DatabaseEngine` fora dos providers): `RecommendedCommitRows` (50.000), `DeferConstraintStatements()`, `MaxTuplesPerPredicate` (1.000) e `BuildTuplePredicate`, além de `BuildKeyRangeGuard` (C02). Todos têm implementação padrão; MySQL (10.000 linhas/commit) e Firebird (5.000, 200 tuplas, predicado com OR) sobrescrevem ao serem implementados (B3/B4). PostgreSQL implementa a guarda `DO $$ … RAISE EXCEPTION`; SQLite devolve nulo e o writer emite só o comentário `VERIFICAÇÃO` | docs/18 §5 e §7, C02 |
| DA-107 | Com constraints adiadas (`DeferredConstraint`) o script inteiro fica num só segmento e sem commits periódicos (o componente de ciclo não pode ser dividido, 18 §6); se exceder o limite, o aviso é `OUT-SEGMENT-OVERSIZE`. O total de segmentos do cabeçalho tem largura fixa de 3 dígitos (`Segmento 001 de 003`) e é gravado no fim, no lugar de um marcador, sem reescrever o arquivo | docs/18 §6 |
| DA-108 | Script de limpeza: faixa inteira Explicit vira `BETWEEN`; chaves compostas, UUID e demais usam tuplas guardadas pelo writer em memória (limite de 2 milhões por tabela; acima, comentário de limpeza manual). `ICleanupScriptBuilder` ganhou o schema no construtor e a convenção de que `KeyRangeResult.Column` traz as colunas da PK separadas por vírgula quando `CapturedKeys` está preenchido. `OutputWriterOptions` ganhou `CommitEveryRows` (testes) e `ScriptHeader` ganhou `Reproducibility`, ambos com padrão | C08 |
| DA-109 | `LineEnding` `\r\n` converte só as quebras estruturais do script (`EolConverter` ignora o interior de `'…'` e `"…"`), para que valores com quebra de linha não sejam alterados | docs/18 §6 |

### 1.2 Trilha B — Providers, SQL e testes de integração (DA-200..299)

| ID | Decisão | Referência |
|---|---|---|
| DA-200 | `TypeMapper` do PostgreSQL normaliza os nomes do catálogo (`format_type` e `pg_type`: `bpchar`, `int4`, `character varying(n)`, `timestamp without time zone`) antes do `NativeTypeParser` do Core. Tipos fora do parser (`time with time zone`, arrays, `money`, enum de usuário) não têm mapeamento canônico e mantêm o nome nativo | docs/11 §5; `PostgreSqlTypeMapper` |
| DA-201 | Dialeto PostgreSQL sem desligamento de FK por sessão: `session_replication_role` exige superusuário. `DisableFkChecks`/`EnableFkChecks` retornam `null` e `SupportsDisableFkChecksPerSession` continua `false` | C02; `PostgreSqlDialect` |
| DA-202 | Extrator lê `pg_catalog` em lote (uma consulta por tipo de objeto) com filtro opcional por namespace e tabela. `ExtractTableAsync` reusa as mesmas consultas, atendendo ao refresh por nó do Explorer | docs/11 §2 e §6; `PostgreSqlSchemaExtractor` |
| DA-203 | UNIQUE sem constraint em colunas simples vira `UniqueConstraintModel` com `UniqueSource.UniqueIndex`. Índice de expressão guarda a definição completa (`pg_get_indexdef`) em `IndexColumn.Expression`, sem análise da expressão | docs/11 §2; digest de referência |
| DA-204 | B2 iniciada antes de B1 ter status ✅ (regra §1.3 do plano), por decisão do responsável em 2026-10-12. B1 depende de Docker, indisponível nesta máquina. O contrato de B2 contra servidor real fica como Skip com motivo | PLANO §1.3; PV-202 |
| DA-205 | Servidores de desenvolvimento definidos pelo responsável em 2026-10-12: MySQL e Firebird num servidor da rede local, SQLite local, PostgreSQL adiado. As credenciais ficam fora do repositório: testes e sondas as leem de variáveis de ambiente | Pedido do responsável; docs/23 |
| DA-206 | O servidor "MySQL" de desenvolvimento é MariaDB 10.5. O provider MySQL atende os dois: `@@server_uuid` (inexistente no MariaDB) é lido à parte e `Product` vira "MariaDB" quando a versão o indica; defaults do MariaDB vêm citados (`'x'`) ou como função (`current_timestamp()`) e são normalizados | docs/10; `MySqlProvider`, `MySqlSchemaExtractor` |
| DA-207 | Firebird 2.5 é suportado na introspecção (o servidor de desenvolvimento é 2.5.9): sem `RDB$IDENTITY_TYPE`, `RDB$FUNCTIONS` nem BOOLEAN, o extrator escolhe as consultas pela versão (`ENGINE_VERSION`). O dialeto recebe a versão maior: generators usam `SET GENERATOR` antes do 3, `RESTART WITH n-1` no 3 e `RESTART WITH n` do 4 em diante | docs/10, docs/11; `FirebirdSchemaExtractor`, `FirebirdDialect` |
| DA-208 | `ISchemaExtractor` ganhou `ListRoutinesAsync` e `ListSequencesAsync`, e `ISqlDialect` ganhou `BuildLimitedSelect` (LIMIT por padrão; `SELECT FIRST n` no Firebird). São métodos com implementação padrão, sem quebra de contrato para providers existentes | C02; Explorer |
| DA-209 | Classificador SQL: `SHOW`, `DESCRIBE`/`DESC`, `VALUES`, `TABLE` e `EXPLAIN` (sem `ANALYZE`) são leitura (Select). Antes caíam em Other, que exige confirmação, e "show tables" não executava | docs/20 §4; `SqlClassifier` |
| DA-210 | `QueryService` fecha o cursor do comando anterior antes de executar o próximo do mesmo script: MySQL e PostgreSQL aceitam um único leitor ativo por conexão. Só o último comando mantém o cursor para paginação; os anteriores ficam com a primeira página | docs/20 §6; `QueryService` |

### 1.3 Trilha C — Aplicação, projeto e interface (DA-300..399)

| ID | Decisão | Referência |
|---|---|---|
| DA-300 | `MdsProject` ganha `ProjectRules` e `ToolVersion` além dos campos de C09 §1, porque docs/22 §1 e o exemplo `project-loja.mdsproj` os trazem. `SnapshotEntry.SourceFingerprintHash` passa a anulável (importação de digest não tem fingerprint de servidor) | C09 §1, docs/22 §1 |
| DA-301 | Serializador rejeita propriedade desconhecida na raiz (`additionalProperties: false` do schema, PRJ-SCHEMA), mas tolera MINOR mais novo do mesmo MAJOR (1.7 abre; 2.0 falha com PRJ-MAJOR-UNSUPPORTED). Valores padrão de relações e de opções avançadas de saída são omitidos; `keyOffset`, `validation` e as opções básicas de `output` sempre são gravados, como no exemplo | docs/22 §2, §4 |
| DA-302 | `IMappingService.BuildContextAsync` devolve `Result<MappingContext>` (falha PRJ-SCHEMA sem snapshot ativo) e `SetBinding` é `SetBindingAsync`, porque aprender regra grava no catálogo. `MigrateBindingsAsync` fica para a C7 | C09 §2 |
| DA-303 | `IDigestService.Compare` devolve `Result<SchemaDiff>` (digest corrompido vira erro DIG-*). `CaptureAsync` devolve snapshot com Id provisório `snap-0000`; `DigestService.NextSnapshotId(project)` atribui o definitivo | C09 §2 |
| DA-304 | T-PRJ-010 valida contra `project-1.0.schema.json` com o `$ref` do digest trocado por `{type: object}`: o digest embutido é validado por `DigestValidator` (PRJ-DIGEST) e pelos testes T-DIG. Divergência de hash do digest é só aviso (DA do Core), então PRJ-DIGEST usa erros estruturais | docs/22 §6 |
| DA-305 | O `MappingEngine` emitia `core.template` e `core.template.field`, ids que o registro do motor não tem (o registro usa `catalog.template` e `catalog.template.field`, como no exemplo `project-loja.mdsproj`). Corrigido em `Mapping/MappingEngine.cs` (arquivo da Trilha A, mudança mínima e registrada) e protegido por `MappingGenerationContractTests`, que exige que todo gerador emitido pelo Mapping exista no registro | C04, C05 §4 |
| DA-306 | `MappingService` resolve vínculos `core.preset` para o gerador e os parâmetros do preset do catálogo antes de gravar no projeto (o motor não conhece presets). `rel.key` e `rel.fk` não estão no registro por serem vínculos estruturais resolvidos pelo planejador; o editor de gerador não os lista | docs/14, C06 |
| DA-307 | `Infrastructure/AppPaths`: a pasta de dados é `%LOCALAPPDATA%\MockDataStudio` ou o caminho de `MDS_DATA_DIR`. Os testes de UI usam uma pasta descartável e não tocam nos dados do usuário | docs/23 §3 |
| DA-308 | `ProjectWorkspace` guarda o histórico de desfazer/refazer em memória (100 entradas, descartado ao abrir ou criar projeto), com granularidade de alteração de projeto; `IAppSettings` ganhou `RecentProjects` (10 itens). Projeto novo adota o nome do arquivo no primeiro Salvar como | docs/21 S10 |
| DA-309 | Telas Home, Digest, Mapping e Templates usam ViewModels singleton (mantêm seleção ao navegar). `IDesignDataSource` e os dados de exemplo foram removidos; `Samples/ViewItems.cs` guarda só os itens de apresentação. A área "Schema Relationships" do Digest lista as chaves estrangeiras em tabela, no lugar do diagrama estático do mockup | docs/21 S02 |
| DA-310 | Connections mostra a senha salva ao selecionar um perfil (botão de exibir/ocultar), por pedido do responsável em 2026-10-12. `IConnectionService.GetStoredPasswordAsync` decifra o segredo DPAPI do usuário atual; segredo de outro usuário/máquina falha com SEC-NO-AUTHORIZATION e o formulário mantém a senha salva se o campo ficar vazio | docs/23 §3; pedido do responsável |
| DA-311 | `IConnectionService.OpenAsync` ganhou sobrecarga com `database`: abre o perfil em outro database do mesmo servidor sem alterar o perfil salvo. Base para o seletor de database do Explorer e das abas do editor | C09; docs/20 |
| DA-312 | SQL Editor em abas: cada aba tem a própria conexão (perfil + database), o próprio documento (texto e desfazer) e os próprios resultados, e a mesma lista de abas aparece no Explorer e na página SQL Editor. F9 e Ctrl+Enter executam a seleção ou o script inteiro; Ctrl+T/Ctrl+W abrem e fecham abas; Esc cancela. A conexão ativa (`ActiveConnectionSession`) passa a ser só a do Explorer | docs/20 §2; pedido do responsável |
| DA-313 | Explorer: selecionar um nó só preenche o inspetor (Columns, Indexes, Foreign Keys, Checks, Triggers, References, Preview); o texto do editor não é mais trocado. Ações do menu de contexto abrem o SQL numa aba do mesmo perfil e database, reaproveitando só aba vazia ou com texto gerado e não editado. A árvore lista cada tipo de objeto uma vez (`DatabaseNavigator.LoadCatalogAsync`), com grupos Triggers, Routines e Sequences quando existem | docs/20, docs/21 S04 |

### 1.4 Integração e gates (DA-400..499)

| ID | Decisão | Referência |
|---|---|---|

## 2. Premissas (PA)

| ID | Premissa | Impacto se falsa |
|---|---|---|
| PA-001 | Uso mono-usuário local; sem compartilhamento simultâneo do catálogo | Exigiria servidor/concorrência multiusuário |
| PA-002 | Público principal usa pt-BR | Priorizar tradução en-US (strings já externalizadas) |
| PA-003 | Volumes típicos ≤ 10 M linhas por execução; máximo suportado 100 M/tabela | Reavaliar paralelismo entre tabelas |
| PA-004 | Destinos são majoritariamente bancos de dev/test | Proteções já cobrem produção |
| PA-005 | Firebird somente dialeto 3 | Dialeto 1 exigiria regras de quoting/tipos diferentes |
| PA-006 | Somente x64 no V1 | ARM64 no V2 (drivers nativos FB) |
| PA-007 | Firebird 6 manterá objetos existentes no schema `PUBLIC` | Ajuste de mapeamento de namespace no provider FB6 (sem impacto em projetos existentes graças ao `virtual`) |
| PA-008 | Firebird embedded não distribuído no V1 | Usuário instala fbclient; documentar |
| PA-009 | Nomes de municípios/UFs (fatos públicos) podem ser usados; demais listas são autorais | Revisão jurídica do pack |
| PA-010 | Valores de sequence > 2^53 serializados como string no digest | — |
| PA-011 | `numeric` sem precisão no PG: geração assume escala 2 e faixa até 10^9 | Usuário ajusta parâmetros |
| PA-012 | Runners Windows hospedados executam FlaUI | Máquina dedicada |
| PA-013 | `ReferenceInstant` fixado na criação do perfil é o comportamento desejado | Alternar padrão para `Now` |
| PA-014 | Fuso horário do perfil = fuso do Windows na criação | — |

## 3. Decisões de produto (DP) — requerem aprovação

| ID | Questão | Padrão provisório (não bloqueia) | Impacto |
|---|---|---|---|
| DP-001 | Nome comercial definitivo | "Mock Data Studio" | Namespaces/instalador (renomear antes do V1) |
| DP-002 | Modelo de licenciamento do produto (proprietário, open-source, freemium) | Dependências todas permissivas — compatível com qualquer escolha | Licença do repositório, EULA |
| DP-003 | Empacotamento: MSIX, instalador (WiX/Inno) ou portátil | Instalador self-contained + zip portátil | TASK-19.04 |
| DP-004 | Plataforma de repositório/CI | **RESOLVIDA (2026-10-12):** GitHub (`origin` = github.com/MarcosAndreRJ/mockdatastudio) + GitHub Actions (`.github/workflows/ci.yml`), com scripts locais `build.ps1`/`test.ps1` | TASK-00.03 |
| DP-005 | Idiomas da UI no V1 | Somente pt-BR | Esforço de tradução |
| DP-006 | Incluir anonimização no V2 e com que escopo | Escopo restrito (27 §5 item 23) | Riscos LGPD |
| DP-007 | Telemetria opt-in no futuro | Nenhuma | NFR-PLAT-002 |
| DP-008 | Certificado de assinatura de código | Necessário para V1 | SmartScreen |
| DP-009 | Metas de usabilidade (NFR-USA-001) e quem participa dos testes | 3 usuários internos | Validação de UX |

## 4. Bloqueios técnicos (BT)

Nenhum bloqueio técnico identificado que impeça iniciar a implementação. Itens com incerteza estão como PV com fallback definido.

## 5. Verificações pendentes (PV)

| ID | Verificação | Como verificar | Fallback se negativo | Tarefa |
|---|---|---|---|---|
| PV-001 | Licença IDPL do FirebirdClient permite redistribuição no instalador | Ler licença no repositório oficial | Download sob demanda / instrução de instalação | TASK-19.04 |
| PV-002 | Existência/nome da API de batch do FirebirdClient para FB4+ | Documentação/código-fonte do NETProvider | Prepared por linha | TASK-15.02 |
| PV-003 | Licença e versão atual do Bogus | nuget.org/GitHub | Não usar | V2 |
| PV-004 | FB4/5 aceita `OVERRIDING SYSTEM VALUE` em identity ALWAYS | T-PRV-FB-KEY-002 | Captured | TASK-08.03 |
| PV-005 | Semântica de `ALTER SEQUENCE ... RESTART WITH` por versão FB | T-PRV-FB-KEY-003 | Ajustar valor calculado por versão | TASK-08.03 |
| PV-006 | COPY binário PG com identity ALWAYS | T-PRV-PG-COPY-001 | INSERT multi-linha com OVERRIDING | TASK-15.02 |
| PV-007 | Autorreferência NOT NULL na mesma linha por SGBD | T-REL-SELF-NN-<X> | Bloqueio `PLN-SELF-NOTNULL` | TASK-10.04/16.01 |
| PV-008 | AvalonEdit funciona em net10.0-windows sem problemas (RESOLVIDO: 6.3.0.90 compila e roda sem erros) | Validado no protótipo de UI em 2026-10-12 | — | Sprint 0.3 |
| PV-009 | Imagem Docker oficial do Firebird (RESOLVIDO: fixado `firebird:5.0` com fallback `jacobalberty/firebird:v5.0` em tests/containers.json) | Docker Hub / tests/containers.json | Imagem comunitária | Sprint 0.10 |
| PV-010 | Formato de `COLUMN_DEFAULT` no MySQL 8.4/9.x | T-PRV-MYSQL-002 | Normalização ajustada | TASK-10.01 |
| PV-011 | Norma vigente do CNPJ alfanumérico (algoritmo de DV) | Publicação oficial da Receita Federal | Adiar `br.cnpj` v2 | TASK-17.05 |
| PV-012 | Disponibilidade de `MON$DATABASE.MON$GUID` (FB4+) para fingerprint | Teste | Usar caminho/alias + servidor | TASK-08.03 |
| PV-013 | Palavra-chave `Foreign Keys` na connection string do Microsoft.Data.Sqlite (RESOLVIDO em 2026-10-12: `Foreign Keys=True` faz a FK ser imposta; T-PRV-SQLITE-008 verde, sem `PRAGMA`) | Teste T-PRV-SQLITE-008 | `PRAGMA foreign_keys=ON` após abrir | TASK-02.02 |
| PV-014 | `DbCommand.Cancel` efetivo em MySqlConnector e FirebirdClient | T-PRV-<X>-009 | Fechar conexão | TASK-08.03/10.03 |
| PV-015 | Pacote JSON1 presente no `SQLitePCLRaw.bundle_e_sqlite3` atual (RESOLVIDO em 2026-10-12: `CHECK json_valid(...)` da V0001 aplica-se sem erro; T-CAT-001 verde) | Teste em 04.01 | Validação de JSON em C# | TASK-04.01 |
| PV-017 | `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, dependência transitiva do Microsoft.Data.Sqlite 10, tem alerta NU1903 (GHSA-2m69-gcr7-jv3q) | Build de Catalog e Providers.Sqlite | Atualizar Microsoft.Data.Sqlite quando houver versão com a biblioteca corrigida; decidir antes do release | Sprint 3 |
| PV-018 | Cobertura High do corpus sintético (48,1%) abaixo da meta de 70%. Causa: variantes com prefixo de tipo (`DS_`, `TX_`, `NM_`, `VL_`) e aliases de vários tokens com sobreposição total recebem TokenOverlap 70 (14 §5); com tipo (+5) e comprimento (+3) o escore é 78, abaixo de 80, logo Medium. Decisão necessária: (a) aceitar a regra e revisar a meta para o caso sem prefixo (~86%); (b) acrescentar evidência para igualdade do núcleo com pontos definidos (alteraria a calibração de 14 §5: exige registro e atualização do 14); (c) medir com corpus real anonimizado (14 §11) antes de escolher | Sprint 5, T-MAP-CORPUS | RESOLVIDO em 2026-10-12: opção (a), ver DA-044 | TASK-05.04 |
| PV-019 | `DbCommand.Cancel()` do Microsoft.Data.Sqlite não interrompe consulta que agrega numa única etapa (ex.: `count(*)` sobre CTE recursiva infinita): a chamada não retorna. O cancelamento é efetivo entre linhas (verificado com CTE que produz linhas). Mitigação: `CommandTimeoutSec`. Verificar se a versão seguinte do driver ou `sqlite3_interrupt` via SQLitePCLRaw resolvem | Explorer (Fase A), PV-014 | Pendência explícita (2026-10-12): limitação aceita com timeout; investigar interrupção nativa antes do release | TASK-Explorer |
| PV-020 | Suíte `ProviderContractTests` da branch `feature/connections` (base) é uma versão anterior (7 testes); a versão com 14 testes está na árvore principal, não commitada | Explorer, release | RESOLVIDO no merge Connections+Explorer (versão completa commitada) | Sprint 3 |
| PV-016 | Idioma dos rótulos de navegação e da UI: DA-026 e DP-005 preveem pt-BR, mas os mockups usam inglês ("Connections", "Schema Digest" etc.). `Strings.resx` mantém os textos dos mockups até decisão do responsável | Revisão do responsável (DP-005) — decisão **D-01** do Plano de Ação | Manter textos do mockup | TASK-00.04, Sprint C1 |

> PV-017 (vulnerabilidades NuGet) foi ampliada pela auditoria de 2026-10-12: há também `SSH.NET` 2024.2.0 (alta, via Testcontainers) e `System.Drawing.Common` 5.0.2 (**crítica**, via FlaUI). Tratamento na Sprint B1.

### 5.1 Trilha A (PV-100..199)

| ID | Verificação | Como verificar | Fallback se negativo | Sprint |
|---|---|---|---|---|
| PV-100 | `IGenerationEngine.RunAsync(GenerationPlan, …)` e `IRowSink`/`RowBatch` (C06) ainda não existem: o motor continua com a API por tabela (DA-052). Output (A3) precisa consumir o plano, então exige coordenação com a trilha A na própria A3 | Implementar A2.5 e revisar com A3 | Manter a API por tabela até A3 e registrar a divergência no relatório | A2 / A3 |
| PV-101 | Snapshots de plano para os 4 digests de `docs/examples` (T-REL-PLAN-001) não foram feitos: falta importar os digests para `SchemaModel` na suíte de testes | Importar `digest-*.json` (Core.Digest) e gerar o plano de `loja` por SGBD | Registrar snapshot parcial só para PostgreSQL e manter o restante pendente | A2 |
| PV-102 | Smoke de UI (`App.UiTests`) falhou em rodadas completas da solução (rodadas de 2026-10-12, com a A2): `Saved Connections (` não foi encontrado na tela Connections após `Thread.Sleep(1200)`. Passa em execuções isoladas. Não é causado por código da A2 (a sprint só altera Generation) | Repetir a suíte completa e capturar a mensagem; trocar o sleep fixo por espera pelo marcador | Não marcar o smoke como verde sem captura; a correção é da Trilha C | Trilha C (C1) |
| PV-103 | A saída em script foi verificada só em SQLite real (execução do script e limpeza). PostgreSQL, MySQL e Firebird: nenhum script foi executado contra servidor (sem Docker); a guarda `DO $$` e o resync de PostgreSQL só têm teste de texto | T-OUT-ROUNDTRIP-* e T-OUT-LIT-* em containers (B1/CI) | Manter marcado como não verificado até a B1 | A3/B1 |
| PV-104 | `SqliteValues.ToStorage` (Trilha B) devolvia números como texto (`'1'` no script). Corrigido nesta sprint (inteiros, floats e decimais) com os testes de contrato verdes; requer revisão da Trilha B | Contratos do SQLite + T-OUT-001 | Reverter e tratar no writer | A3 |

### 5.2 Trilha B (PV-200..299)

| ID | Verificação | Como verificar | Fallback se negativo | Sprint |
|---|---|---|---|---|
| PV-200 | Extrator PostgreSQL contra servidor 17: o digest de `loja` deve ser igual a `docs/examples/digest-postgresql.json` (B2.2). Nada foi executado contra servidor nesta etapa | Docker com PostgreSQL 17; capturar `loja` e comparar o digest | Documentar as diferenças e ajustar o extrator; não aceitar divergência de tipo ou de chave sem registro | B2 |
| PV-201 | Expressão de CHECK: `pg_get_constraintdef` devolve forma diferente da do digest de referência (parênteses e `::text`). A equivalência de digest não foi verificada | Comparar o digest capturado (PV-200) com o de referência | Normalizar a expressão na captura, com regra registrada | B2 |
| PV-202 | Contrato PostgreSQL real (T-PRV-PG-001..011) e T-OUT-ROUNDTRIP-PG não executados: Docker indisponível. Hoje há só testes puros e um Skip com motivo | Executar `Category=Contract` com Docker e PostgreSQL 17 | Manter o Skip com motivo; B2.5 não é marcada como concluída | B2 |
| PV-203 | MySQL (MariaDB 10.5) e Firebird 2.5 verificados contra os servidores de desenvolvimento só por sonda fora do repositório: listagem, catálogo, inspeção, preview, DDL e execução de script. Não há teste automatizado de contrato para MySQL e Firebird no repositório, nem verificação com MySQL 8 ou Firebird 3+ | Testes `Category=Contract` lendo host e senha de variáveis de ambiente; servidores MySQL 8 e Firebird 4 | Manter a sonda manual registrada; não marcar B3/B4 como concluídas | B3/B4 |

### 5.3 Trilha C (PV-300..399)

| ID | Verificação | Como verificar | Fallback se negativo | Sprint |
|---|---|---|---|---|
| PV-300 | T-PRJ-007 (perfis não resolvidos ao abrir em outra máquina) e as validações PRJ-GENERATOR, PRJ-PARAMS e PRJ-QUERY não implementadas: dependem do registro de geradores (A) e do SqlTooling | Teste com projeto cujo `profileId` não existe; gerador desconhecido; query não-SELECT | Aberto no ProjectValidator como TODO; a abertura não bloqueia | C2/C3 |
| PV-301 | Autosave e arquivo de recuperação (docs/22 §7) e `ProjectMigrator` com backup `.vX.Y.bak` não implementados (só há formato 1.0) | Teste de recuperação ao reabrir; migração 1.0→1.1 quando existir | Implementar com a UI de projeto (C3) e a primeira mudança de formato | C3 |
| PV-302 | Fluxo "só por teclado" (G-MVP-8) e salvar pelo diálogo de arquivo não foram automatizados na UI: T-UI-FLOW-001 cobre projeto novo → digest → mapeamento automático por automação de interface | Teste FlaUI com foco e Tab/Enter; diálogo de arquivo | Cobertura de Salvar/Abrir fica em Application.Tests; validar a navegação por teclado manualmente | C4 |
| PV-303 | Telas reescritas (Home, Digest, Templates, Mapping) verificadas por testes de texto e de fluxo, sem comparação visual com os mockups; nada foi conferido a 125% e 150% de DPI | Captura e comparação visual; T-UI-DPI-001 | Ajustes de layout depois de revisão do responsável | C3/C9 |
| PV-304 | Explorer e SQL Editor em abas (DA-312, DA-313) verificados por build, teste de fumaça de UI e sonda dos serviços; a interação real (troca de database por aba, F9, menu de contexto, preview) não foi exercitada por automação de interface | Teste FlaUI com perfil SQLite: nova aba, trocar database, F9, menu de contexto | Validação manual pelo responsável | Módulo Explorer |

### 5.4 Integração e gates (PV-400..499)

| ID | Verificação | Como verificar | Fallback se negativo | Sprint |
|---|---|---|---|---|

## 6. Histórico de alterações de decisões

| Data | Item | Alteração | Motivo |
|---|---|---|---|
| 2026-10-11 | — | Criação do registro | Especificação inicial |
| 2026-10-11 | DA-015/016 | Movidos tipos puros para Core; Providers/Catalog → SqlTooling | Auditoria (36, achados A-01, A-02) |
| 2026-10-12 | DA-023 | Migração da solução para `MockDataStudio.slnx` | Modernização .NET 10 (Sprint 0.3) |
| 2026-10-12 | DA-024..026 | Decisões de UI decorrentes do inventário | Inventário de UI (docs/ui-inventory.md, Sprint 0.2) |
| 2026-10-12 | DA-027..028, PV-016 | Execução do Sprint 1 (MVVM/DI, tokens, separação SQL Editor, idioma pendente) | Sprint 1 |
| 2026-10-12 | DA-033..039, PV-013/015 resolvidas, PV-017 | Execução da Sprint 3 (SqlTooling sem engine, provider SQLite completo, suíte de contrato) | Sprint 3 |
| 2026-10-12 | DA-040..043, PV-018 | Execução da Sprint 5 (normalizador, binder, pontuação, slots, corpus sintético; meta de cobertura não atingida) | Sprint 5 |
| 2026-10-12 | DA-045..051 | Execução da Sprint 6 (RNG, geradores, MDS-Expr, unicidade, motor; pendências declaradas) | Sprint 6 |
| 2026-10-12 | DA-029..032 | Execução da Sprint 2 (Core: nomes, schema, digest, hash, diff) | Sprint 2 |
| 2026-10-12 | PV-008 | Resolvido: AvalonEdit 6.3.0.90 compila e funciona em net10.0-windows | Verificação empírica no build Release do protótipo |
| 2026-10-12 | PV-009 | Resolvido: Imagem Firebird fixada `firebird:5.0` com fallback | TASK-00.05 / Sprint 0.10 |
| 2026-10-12 | DA-052..055, DP-004 resolvida, PV-017 ampliada | Auditoria de implementação (docs/37) e passagem para execução paralela em trilhas | docs/37, PLANO-DE-ACAO.md |

### 6.1 Trilha A

| Data | Item | Alteração | Motivo |
|---|---|---|---|
| 2026-10-12 | DA-105..109, PV-103..104 | Sprint A3 (saída em script SQL) | Writer, sink segmentado, manifesto, limpeza e guardas; constantes por SGBD movidas para ISqlDialect |
| 2026-10-12 | DA-100..104, PV-100..102 | Sprint A2 parcial: grafo com Tarjan, resolução de ciclos, faixa de chaves e planejador com diagnósticos e PlanHash | Pedido do responsável em 2026-10-12 |

### 6.2 Trilha B

| Data | Item | Alteração | Motivo |
|---|---|---|---|
| 2026-10-12 | DA-200..204, PV-200..202 | Sprint B2 parcial (TypeMapper, dialeto, extrator e testes puros do PostgreSQL) e reconciliação do plano (C1, C2, C5, B2, B3, B4) | Pedido do responsável em 2026-10-12 |
| 2026-10-12 | DA-205..210, PV-203 | Providers MySQL e Firebird (introspecção, dialeto, MariaDB, Firebird 2.5), classificador e cursor do QueryService | Módulo Conexões/Explorer, pedido do responsável |

### 6.3 Trilha C

| Data | Item | Alteração | Motivo |
|---|---|---|---|
| 2026-10-12 | DA-310..313, PV-304 | Módulo Conexões/Explorer: senha visível no formulário, database por conexão, editor em abas, inspetor e árvore completos | Pedido do responsável |
| 2026-10-12 | DA-305..309, PV-302..303 | Sprint C3 (telas do fluxo MVP com dados reais) | Ligar Home, Digest, Templates e Mapping aos serviços da C2; correção dos ids de geradores do Mapping |
| 2026-10-12 | DA-300..304, PV-300..301 | Sprint C2 (Projects, DigestService, MappingService) | Implementação de C09 §1 e §2 com ajustes de assinatura registrados |

### 6.4 Integração e gates

| Data | Item | Alteração | Motivo |
|---|---|---|---|
