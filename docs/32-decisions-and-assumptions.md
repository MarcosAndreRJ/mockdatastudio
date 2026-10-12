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

### 1.2 Trilha B — Providers, SQL e testes de integração (DA-200..299)

| ID | Decisão | Referência |
|---|---|---|
| DA-200 | `TypeMapper` do PostgreSQL normaliza os nomes do catálogo (`format_type` e `pg_type`: `bpchar`, `int4`, `character varying(n)`, `timestamp without time zone`) antes do `NativeTypeParser` do Core. Tipos fora do parser (`time with time zone`, arrays, `money`, enum de usuário) não têm mapeamento canônico e mantêm o nome nativo | docs/11 §5; `PostgreSqlTypeMapper` |
| DA-201 | Dialeto PostgreSQL sem desligamento de FK por sessão: `session_replication_role` exige superusuário. `DisableFkChecks`/`EnableFkChecks` retornam `null` e `SupportsDisableFkChecksPerSession` continua `false` | C02; `PostgreSqlDialect` |
| DA-202 | Extrator lê `pg_catalog` em lote (uma consulta por tipo de objeto) com filtro opcional por namespace e tabela. `ExtractTableAsync` reusa as mesmas consultas, atendendo ao refresh por nó do Explorer | docs/11 §2 e §6; `PostgreSqlSchemaExtractor` |
| DA-203 | UNIQUE sem constraint em colunas simples vira `UniqueConstraintModel` com `UniqueSource.UniqueIndex`. Índice de expressão guarda a definição completa (`pg_get_indexdef`) em `IndexColumn.Expression`, sem análise da expressão | docs/11 §2; digest de referência |
| DA-204 | B2 iniciada antes de B1 ter status ✅ (regra §1.3 do plano), por decisão do responsável em 2026-10-12. B1 depende de Docker, indisponível nesta máquina. O contrato de B2 contra servidor real fica como Skip com motivo | PLANO §1.3; PV-202 |

### 1.3 Trilha C — Aplicação, projeto e interface (DA-300..399)

| ID | Decisão | Referência |
|---|---|---|

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

### 5.2 Trilha B (PV-200..299)

| ID | Verificação | Como verificar | Fallback se negativo | Sprint |
|---|---|---|---|---|
| PV-200 | Extrator PostgreSQL contra servidor 17: o digest de `loja` deve ser igual a `docs/examples/digest-postgresql.json` (B2.2). Nada foi executado contra servidor nesta etapa | Docker com PostgreSQL 17; capturar `loja` e comparar o digest | Documentar as diferenças e ajustar o extrator; não aceitar divergência de tipo ou de chave sem registro | B2 |
| PV-201 | Expressão de CHECK: `pg_get_constraintdef` devolve forma diferente da do digest de referência (parênteses e `::text`). A equivalência de digest não foi verificada | Comparar o digest capturado (PV-200) com o de referência | Normalizar a expressão na captura, com regra registrada | B2 |
| PV-202 | Contrato PostgreSQL real (T-PRV-PG-001..011) e T-OUT-ROUNDTRIP-PG não executados: Docker indisponível. Hoje há só testes puros e um Skip com motivo | Executar `Category=Contract` com Docker e PostgreSQL 17 | Manter o Skip com motivo; B2.5 não é marcada como concluída | B2 |

### 5.3 Trilha C (PV-300..399)

| ID | Verificação | Como verificar | Fallback se negativo | Sprint |
|---|---|---|---|---|

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

### 6.2 Trilha B

| Data | Item | Alteração | Motivo |
|---|---|---|---|
| 2026-10-12 | DA-200..204, PV-200..202 | Sprint B2 parcial (TypeMapper, dialeto, extrator e testes puros do PostgreSQL) e reconciliação do plano (C1, C2, C5, B2, B3, B4) | Pedido do responsável em 2026-10-12 |

### 6.3 Trilha C

| Data | Item | Alteração | Motivo |
|---|---|---|---|

### 6.4 Integração e gates

| Data | Item | Alteração | Motivo |
|---|---|---|---|
