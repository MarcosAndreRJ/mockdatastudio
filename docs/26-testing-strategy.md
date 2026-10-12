# 26 — Estratégia de Testes

| Campo | Valor |
|---|---|
| Objetivo | Definir níveis de teste, catálogo de testes com IDs rastreáveis, ambientes reproduzíveis, matriz de versões e regras de CI. |
| Escopo | Todo o produto. |
| Relacionados | [29](29-acceptance-criteria.md), [35](35-traceability-matrix.md), [33 §5](33-executor-instructions.md) |

## 1. Pirâmide e ferramentas

| Nível | Ferramentas | Execução | Alvo |
|---|---|---|---|
| Unitário | xUnit v3, Shouldly, Verify | Todo build | Core, Mapping, Generation, SqlTooling, Output (formatadores), Projects, Catalog (SQLite em memória) |
| Contrato de provider | Suíte abstrata `ProviderContractTests<TFixture>` | PR (SQLite) / nightly + marcos (todos) | Cada provider |
| Integração | Testcontainers (MySQL, PostgreSQL, Firebird), SQLite arquivo temp | Nightly + marcos; PR quando tocar `Providers.*`/`Output` | Introspecção real, round-trip de literais, inserção direta, E2E |
| Arquitetura | Reflexão sobre assemblies | Todo build | Regras AR-* |
| UI | FlaUI (UIA3) | Marcos + nightly (Windows) | Fluxos críticos, acessibilidade |
| Desempenho | BenchmarkDotNet + cenários | Marcos | Metas NFR-PERF |
| Snapshot | Verify (`*.verified.json/.sql/.txt`) | Todo build | Digests, scripts, planos, saídas de geradores |

Categorias via `[Trait("Category", "Unit|Contract|Integration|UI|Benchmark|Statistical|Corpus")]` e `[Trait("Id","T-...")]`.

**Regra de honestidade dos testes (DA-055):** um teste que não pode executar no ambiente (sem Docker, sem sessão interativa, sem servidor) termina como **Skip com motivo** (xUnit v3: `Assert.Skip("motivo")`/`[Fact(SkipUnless = …)]`), **nunca** como aprovado por `return` antecipado. Relatórios de sprint informam aprovados, falhos e ignorados separadamente. Teste de integração "smoke" deve iniciar o container de fato (Testcontainers), conectar e executar `SELECT 1`.

**Encerramento do host de testes:** toda suíte deve terminar sozinha. Conexões, pools (`SqliteConnection.ClearAllPools()`, `NpgsqlConnection.ClearAllPools()` etc.) e containers são liberados em `IAsyncLifetime.DisposeAsync`/fixtures. Um host que não encerra é defeito (docs/37 A-06).

## 2. Ambientes reproduzíveis

- `tools/docker/compose.test.yml` (uso local) e Testcontainers (CI) com **imagens fixadas por digest/tag exata** registradas em `tests/containers.json`.
- Cada teste de integração cria **banco novo** (nome aleatório) e aplica DDL de `tests/fixtures/schemas/<schema>.<engine>.sql`; descartado ao final. Nenhum teste usa banco compartilhado ou real do usuário.
- Fixtures de schema obrigatórios: `loja` (referência), `cycles` (ciclo nullable, ciclo NOT NULL, ciclo deferrable PG), `selfref`, `composite-keys`, `constraints-*` (40 casos de violação), `types-*` (todos os tipos por SGBD), `big` (1.000 tabelas geradas por script para B-INT-001), `firebird-legacy` (generator+trigger `tsq` condicional e não condicional, domínios, charsets WIN1252/UTF8/OCTETS).
- Seeds fixas em todos os testes de geração.

## 3. Matriz de versões

| SGBD | Imagem/versão | Frequência |
|---|---|---|
| SQLite | Biblioteca empacotada | Todo build |
| PostgreSQL | 14, 15, 16, 17, 18 | 17: PR (quando aplicável) e nightly; demais: marcos |
| MySQL | 8.4 (LTS), 9.x (última) | 8.4 nightly; 9.x marcos |
| MySQL 8.0 / MariaDB 11.4 | melhor esforço | marcos (falhas não bloqueiam; registradas) |
| Firebird | 3.0, 4.0, 5.0 | 5.0 nightly; 3.0/4.0 marcos |

Imagens Firebird: preferir imagem oficial do projeto Firebird no Docker Hub [K — verificar nome/tag atuais na TASK-00.05, PV-009]; alternativa comunitária documentada.

## 4. Testes de contrato de provider

Suíte abstrata executada para cada provider (T-PRV-<X>-NNN, X ∈ SQLITE, MYSQL, PG, FB):

| Nº | Verificação |
|---|---|
| 001 | Conectar, `ServerInfo` (versão, fingerprint estável entre duas conexões) |
| 002 | Extrair schema `loja` → digest igual ao snapshot verificado (Verify) |
| 003 | Mapeamento de tipos: fixture `types-*` → `LogicalType`/parâmetros esperados |
| 004 | Identity/auto/serial/tsq detectados corretamente |
| 005 | CHECKs, triggers, rotinas, sequences listados (sem corpo) |
| 006 | `IBulkInserter` insere 10.000 linhas e `COUNT(*)` confere |
| 007 | `ITargetInspector`: MAX, próximo valor de sequence sem consumir (quando suportado), triggers |
| 008 | `IErrorTranslator`: provoca unique/FK/not null/check/too long/syntax/timeout e verifica categoria |
| 009 | Cancelamento de consulta longa em ≤ 6 s |
| 010 | Resync de sequence: após inserir chaves explícitas, um INSERT sem chave recebe `last+1` |
| 011 | Introspecção parcial sem permissão em rotinas → `PRV-INTROSPECT-PARTIAL` |

Específicos: T-PRV-FB-KEY-001 (tsq condicional vs não), T-PRV-FB-KEY-002 (`OVERRIDING SYSTEM VALUE` FB4/5), T-PRV-FB-KEY-003 (semântica de `RESTART WITH` por versão), T-PRV-PG-COPY-001 (COPY com identity ALWAYS), T-PRV-MYSQL-ESC-001 (escapes com/sem `NO_BACKSLASH_ESCAPES`), T-PRV-SQLITE-DATE-001 (`dateStorage`).

## 5. Catálogo de testes por área

### 5.1 Arquitetura (T-ARC)
001 Core sem dependências do produto · 002 Generation/Mapping/SqlTooling/Projects só Core · 003 WPF/AvalonEdit só em App · 004 drivers só em seus providers (+Catalog→Microsoft.Data.Sqlite) · 005 App referencia Providers só no composition root · 006 records públicos de Core imutáveis · 007 nenhum `switch`/`if` sobre `DatabaseEngine` fora de Providers/App.Composition.

### 5.2 Digest (T-DIG)
001 saída válida pelo schema · 002 round-trip modelo↔digest (4 SGBDs) · 003 hash invariante a nome do banco, nomes de constraints, ordem de colunas, comentários · 004 hash muda com cada mudança estrutural listada em 12 §6 · 005 classificação de severidade do diff · 006 candidatos de renomeação · 007 cada código `DIG-*` provocado por fixture inválida · 010 equivalência validador codificado × JSON Schema de referência · 020 leitura de fixtures de versões anteriores · 030 migração de bindings (tabela 12 §9).

### 5.3 Catálogo (T-CAT)
001 migração do zero + integridade · MIG-NNN uma por migração · 002 proteção de embutidos · 003 staging rejeita DDL/ATTACH/PRAGMA · 004 cada `CAT-IMP-*` · 005 modos de merge · 006 backup/restauração · 007 integrity checker desabilita regras inválidas · 008 export/import de pack round-trip · 009 `content_hash` independe da ordem de inserção · 010 pack embutido instalado via staging.

### 5.4 Mapeamento (T-MAP)
001 normalizador (tabela de 14 §2 + 200 casos) · 002 binder estrutural · 003 evidências e pontos · 004 desempate determinístico · 005 faixas de confiança e ambiguidade · 006 slots por resíduo · 007 aprendizado (feedback/regras) · 008 fallback por tipo · CORPUS métricas (≥70% High, ≥95% precisão).

### 5.5 Geração (T-GEN)
RNG-001 vetores congelados · RNG-002 uniformidade (χ², 10^6 amostras, p > 0,001) · DET-001 cada gerador: 2 execuções com MaxDOP 1 e 8 → valores idênticos · DET-002 script byte-idêntico (exceto linha de data) entre execuções · DET-003 adicionar coluna não altera outras · LIB-<id> snapshot de 1.000 valores por gerador/versão · UQ-001 Feistel bijetivo exaustivo N ≤ 65.536 · UQ-002 bloqueio por capacidade · UQ-003 unicidade composta · UQ-004 tentativas limitadas (falha determinística) · EXPR-001 gramática (aceita/rejeita corpus) · EXPR-002 funções · EXPR-003 erros com posição · COMP-001 coerência de slots · NULL-001 taxa de nulos ±1% em 10^5 linhas · COERCE-001 tabela de coerção · BR-001 CPF/CNPJ com DVs válidos (10^5 amostras) e formatados.

### 5.6 Relacional (T-REL)
001 SCC/ordem determinística · 002 ciclo NullThenUpdate (executado em PG/FB/MySQL/SQLite reais) · 003 DeferredConstraint PG · 004 DisableChecks MySQL/SQLite com reativação · 005 bloqueio `PLN-CYCLE-UNBREAKABLE` · 006 autorreferência anulável com profundidade · SELF-NN-<X> autorreferência NOT NULL por SGBD (resultado define suporte) · 007 FK composta consistente · 008 ParentDriven soma e cobertura · 009 1:1 sem repetição · 010 Existing + fingerprint · 011 origem≠destino com chaves ausentes → bloqueio · 012 reserva de faixa e overflow · 013 faixa ocupada antes da execução · PLAN-001 snapshot do plano `loja`.

### 5.7 Validação (T-VAL)
001–040 casos de `constraints-*` · LOJA sem falso positivo · TRIAL-001 rollback e aviso de sequences · TRIAL-002 bloqueio em Production · CHECK-001 padrões do interpretador.

### 5.8 Saída e execução (T-OUT, T-DIR)
OUT-LIT-<X> corpus adversarial · OUT-ROUNDTRIP-<X> executa script e relê valores · OUT-001 estrutura/cabeçalho (snapshot por dialeto) · OUT-002 segmentação nunca quebra comando · OUT-003 manifesto com hashes corretos · OUT-004 limpeza remove exatamente as linhas da execução · OUT-005 CSV/JSONL · OUT-006 resync efetivo · DIR-<X>-001 inserção completa · DIR-002 falha em batch → relatório e estado · DIR-003 cancelamento → rollback do batch · DIR-004 Captured · DIR-005 DisableChecks restaurado após erro.

### 5.9 Editor SQL (T-SQL)
SPLIT-<X> corpus por dialeto · CLASS-001 classificação e destrutivos · COMP-001 contextos de autocomplete · EXEC-001 estado de transação manual · EXEC-002 cancelamento · GUARD-001 matriz completa de 23 §5.2 · HIST-001 histórico/retenção.

### 5.10 Projeto (T-PRJ)
001 round-trip · 002 serialização estável (chaves ordenadas) · 003 sem segredos · 004 migração de formato · 005 códigos `PRJ-*` · 006 gravação atômica (simula falha) · 007 perfis não resolvidos · 010 equivalência com JSON Schema.

### 5.11 Segurança (T-SEC)
001 execução direta sem autorização falha · 002 projeto sem segredos · 003 fingerprint divergente invalida autorização · 004 redação de logs · 005 ReadOnly bloqueia DML/DDL no editor e na geração · 006 modo somente leitura no servidor aplicado · 007 query de referência não-SELECT rejeitada · 008 projeto não confiável desativa Query · 009 staging isolado (script tenta `ATTACH` arquivo do catálogo) · 010 sanitização de nomes de arquivo · 011 autorização expirada (> 10 min) · 012 DPAPI: segredo de outro usuário não decifra · NET-001 nenhuma conexão de rede além do banco.

### 5.12 UI (T-UI)
FLOW-001 SQLite ponta a ponta só teclado · FLOW-002 importação de catálogo · FLOW-003 editor com transação manual · FLOW-004 inserção direta com confirmação digitada · A11Y-001 contraste · A11Y-002 nomes UIA · A11Y-003 navegação por teclado · DPI-001 100–200% · PERF-001 explorer 10k tabelas · PERF-002 dispatcher ≤ 100 ms · PERF-003 grade 100k linhas.

### 5.13 Ponta a ponta (T-E2E)
`<X>-001` loja: introspecção → automap → plano → script → executar script em banco limpo → contagens/FKs/uniques conferidos por SQL · `<X>-002` mesmo via inserção direta + limpeza → banco volta a 0 linhas · 003 determinismo entre duas execuções (hash do script) · 004 alteração de schema → migração de bindings → nova geração válida.

### 5.14 Erros (T-ERR)
001 todos os códigos existem e têm mensagem.

### 5.14a Application (T-APP)
001 ConnectionService (CRUD/teste com provider fake) · 002 DigestService (captura/exportação/importação) · 003 MappingService (contexto, AutoBindAll, SetBinding com aprendizado) · 004 GenerationService (plano → validação → execução em sink de memória) · 005 ReferenceDataSource (Generated/Fixed) — todos com fakes, sem banco.

### 5.14c IDs criados durante a implementação (Sprints 0–6)
T-NAM-001/002 round-trip de `ObjectName`/`ColumnRef` (10.000 casos, seed fixa) · T-SQL-SPLIT-CORPUS corpus por dialeto · T-UI-SMOKE-001 abre o app e navega por todas as telas · T-PRV-NNN-xxx IDs da suíte abstrata de contrato (`NNN` = SGBD na classe concreta) · T-SMOKE-* smoke de infraestrutura (**a substituir na Sprint B1** por smoke real com Testcontainers; DA-055).

### 5.14b Adições relacionais
T-REL-PLAN-002 snapshot de plano com `NaturalKeyLookup` e execução do script resultante (PG e MySQL).

### 5.15 Benchmarks (B-*)
B-RNG-001, B-GEN-001, B-MEM-001, B-INT-001, B-START-001, B-MAP-001, B-SQL-001, B-OUT-001/002, B-DIR-<X>, B-DIG-SIZE ([24 §2](24-performance-scalability.md)).

## 6. Testes de grande volume
- B-MEM-001 e B-OUT-002 (10 M linhas) em marcos.
- Teste de regressão de memória: geração de 2 M linhas com `GC.GetTotalMemory` amostrado; falha se pico > 300 MB (T-GEN-VOL-001, nightly).

## 7. Regressão
- Snapshots Verify protegem saídas (digest/script/plano/geradores). Mudança de snapshot exige justificativa no commit e, se alterar saída de gerador existente, nova versão do gerador (regra de 15 §4.4).
- Bugs corrigidos ganham teste com ID `T-BUG-<n>`.

## 8. CI

| Job | Runner | Conteúdo |
|---|---|---|
| `build-unit` | windows-latest | Build Release completo (inclui WPF), unit + arquitetura + snapshots |
| `integration` | ubuntu-latest | Testes de contrato/integração com Testcontainers (assemblies `net10.0` rodam em Linux; Windows runners não suportam containers Linux [K]) |
| `ui` | windows-latest (sessão interativa) | FlaUI — **premissa PA-012**: runners hospedados permitem automação UIA; se falhar, executar em máquina dedicada |
| `nightly` | ambos | Matriz completa de versões |

Critério de merge: `build-unit` verde; `integration` verde se o PR toca Providers/Output/Application. O job `integration` **não** usa `continue-on-error` (falha de integração reprova o pipeline).

**Auditoria de dependências:** `NuGetAudit` ativo; avisos NU1901–NU1904 aparecem no resumo do build. Vulnerabilidade **alta ou crítica** em pacote usado por `src/` bloqueia o release (Gate V1); em pacote usado só por `tests/`, exige registro em docs/32 com mitigação ou atualização do pacote.

**Execução paralela por IAs (DA-054):** cada sprint roda em branch própria. Antes do merge em `main`, a IA executa no mínimo a suíte rápida completa (`Category=Unit|Architecture|Contract`) após incorporar a `main` mais recente. Se tocou Providers/Output/Application, executa também `Category=Integration` (ou declara que não pôde, por falta de Docker, com o job de CI como evidência).

## 9. Dados de teste
Nenhum dado real. Fixtures de mapeamento (`mapping-corpus`) contêm apenas **nomes de colunas**, coletados de schemas públicos/sintéticos.
