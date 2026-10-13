# Plano de Ação v3 — Mock Data Studio (continuação em etapas)

| Campo | Valor |
|---|---|
| Objetivo | Continuar o desenvolvimento do ponto atual até o **MVP** e a **V1**, em **etapas sequenciais**. Dentro de cada etapa, até 3 sprints rodam em paralelo, cada uma numa branch, executada por uma IA. |
| Ponto de partida | `main` em `01f5a98` (verificado em 2026-10-12, ver §2) |
| Histórico | [Plano sequencial (Sprints 0–6)](docs/progress/plano-sequencial-sprints-0-6.md) · [Plano em trilhas v2](docs/progress/plano-paralelo-trilhas-v2.md) — ambos arquivados, com as evidências originais |
| Conteúdo das tarefas | [docs/28-implementation-backlog.md](docs/28-implementation-backlog.md) (`TASK-xx.yy`) |
| Regras da IA | [docs/33-executor-instructions.md](docs/33-executor-instructions.md) (§18) · Definition of Done: [docs/29 §1](docs/29-acceptance-criteria.md) |
| Revisão | 2026-10-12 (v3) |

---

## 1. Como usar este plano (obrigatório para toda IA)

1. **Etapas são sequenciais.** Uma etapa só começa quando o **Marco de saída** da etapa anterior estiver marcado ✅ (exceção indicada na própria etapa).
2. **Sprints dentro de uma etapa são paralelas.** O mapa do §4 diz qual IA executa cada sprint. A posse de diretórios (§5) segue a **trilha da sprint** (A, B ou C), e não a IA.
3. **Uma sprint = uma branch = uma IA.** Siga o protocolo Git do §6 no início e no fim de cada sprint.
4. **Ao concluir cada etapa de trabalho, marque o checkbox**: troque `- [ ]` por `- [x]` e preencha na linha `Concluído em: AAAA-MM-DD · Evidência: <arquivo/teste/resumo>`.
5. **Só marque `[x]` com a etapa realmente concluída**: build Release sem erros e sem avisos, testes executados e passando, documentação atualizada. Parcial → `[ ]` com `⏳ PARCIAL: <o que falta>`. Impedido → `[ ]` com `⚠ BLOQUEADO: <motivo>`.
6. **Atualize a linha Status** da sprint: `⬜ Não iniciada` → `🔄 Em andamento (branch, IA, data)` → `✅ Mesclada na main (data, hash do merge)`.
7. **Edite neste arquivo somente a seção da sua sprint.** O Marco de saída de cada etapa é marcado pela IA que fizer o **último merge** da etapa.
8. **Honestidade:** o que não foi verificado é declarado como não verificado. Teste que não pôde executar fica como **Skip com motivo**, nunca como aprovado (DA-055). Sem Docker, nenhum item que dependa de servidor real pode ser marcado `[x]` sem o CI como evidência.
9. **Registros:** decisões e verificações vão para [docs/32](docs/32-decisions-and-assumptions.md), na faixa e na subseção da trilha (A: 100–199, B: 200–299, C: 300–399, marcos: 400–499). Continue a numeração a partir do último ID existente da faixa.
10. **Relatório da sprint (obrigatório):** feito, testes por projeto (aprovados/falhos/ignorados), decisões (IDs), pendências, riscos e hash do merge.

**Legenda:** `- [ ]` pendente · `- [x]` concluído · `⏳ PARCIAL` · `⚠ BLOQUEADO` · Status: `⬜` `🔄` `✅`

---

## 2. Situação em 2026-10-12 (análise completa do repositório)

### 2.1 Repositório

| Item | Situação |
|---|---|
| Branch principal | `main` (não existe `master`). Local **atualizada** com `origin/main` (0 commits atrás), mas **4 commits à frente, não publicados** (merge da A3) |
| Branches de sprint | `sprint/A2-…`, `sprint/A3-…`, `sprint/B2-…`, `sprint/EXP-…` — **todas já mescladas na `main`**, ainda com worktrees em `../MockDataStudio-{A2,A3,b2,explorer2}`. `origin/sprint/A2-…` também mesclada |
| Build Release | ✅ 0 erros, **0 avisos** (vulnerabilidades NuGet da auditoria anterior resolvidas) |
| Testes (Release, por projeto) | ver §2.3 |
| Docker nesta máquina | ❌ indisponível: nenhum teste contra PostgreSQL/MySQL/Firebird real foi executado até hoje |
| CI | Job de integração ainda com `continue-on-error: true` |

### 2.2 Implementação por área

| Área | Estado | Observações |
|---|---|---|
| Core (schema, digest, hash, diff, profiles, bindings) | ✅ | `Core.Profiles` concluído (A1) |
| SqlTooling | ✅ | Corpus de split ainda pequeno (1 arquivo por dialeto) |
| Catálogo | ✅ | Pack `CORE_PTBR` com volumes MVP |
| Mapeamento | ✅ | Faltam evidências `HistoryMatch`/`CheckConstraintHint`/`UniqueCapacity` (A5) |
| Geradores, RNG, MDS-Expr, unicidade | ✅ | br.cpf/cnpj, weighted/switch/lorem, sufixo e base mista prontos; faltam br.phone/placa, uuid v7 (A5) |
| **Planejador relacional** | ⏳ | Grafo, ciclos, faixa de chaves, `ExplicitKeyOracle`, `GenerationPlanner` e `PlanHash` prontos. **Faltam** `rel.key`/`rel.fk`/`rel.child_seq`, FKs compostas, autorreferência, **`IGenerationEngine.RunAsync(GenerationPlan)` multi-tabela**, `PreflightValidator`, snapshots de plano |
| **Motor** | ⏳ | Só `RunAsync(TablePlan)` por tabela; `IRowSink` já no formato C06 |
| Saída SQL (Output) | ✅ | Writer, segmentação, manifesto, limpeza, guardas — verificado só em SQLite (PV-103) |
| Provider SQLite | ✅ | Contrato verde |
| Provider PostgreSQL | ⏳ | TypeMapper, dialeto, extrator (não verificado em servidor). `TargetInspector` e `BulkInserter` = `NotImplementedException` |
| Providers Firebird e MySQL | ❌ | Só conexão/listagem de bancos; introspecção, dialeto, inspector e bulk = `NotImplementedException` |
| Projects | ⏳ | Serializer/store/validator prontos; faltam T-PRJ-007, validações PRJ-GENERATOR/PARAMS/QUERY, autosave, migrator (PV-300/301) |
| Application | ⏳ | Connection, Digest, Mapping, Workspace, Explorer, preview prontos. **Falta `GenerationService`** |
| Infrastructure | ✅ | DPAPI, settings, Serilog com redação |
| UI | ⏳ | Home, Connections, Digest, Templates, Mapping, Explorer e SQL Editor com dados reais. **Generation, History e Settings = placeholder.** Textos ainda em inglês, apesar de **D-01 = pt-BR**; sem logo MySQL (D-04); sem tema escuro; smoke de UI intermitente (PV-102) |

### 2.3 Testes executados nesta análise

**489 aprovados, 2 falhos (UI), 4 ignorados** — detalhes por projeto no §10.

### 2.4 Desvios de processo observados (corrigidos neste plano)

- Sprints iniciadas sem dependências concluídas (A3 sem A2/B1, B2 sem B1), com autorização do responsável (DA-105, DA-204). O resultado são entregas parciais mescladas.
- Uma sprint fora do plano (`sprint/EXP-explorer-completo`) e uma branch intermediária `feature/connections`.
- Status desatualizados no plano v2: A2 e B2 diziam "não mescladas", e o bloqueio da C4 ainda citava a A3.

**Correções:** sprints parciais continuam em branches novas com sufixo `-r2`, cujo escopo lista **só o que falta**. Branches mescladas são removidas na Etapa 0. Nenhuma sprint nova pode começar fora deste plano.

---

## 3. Decisões do responsável

| ID | Decisão | Situação |
|---|---|---|
| D-01 | Idioma da interface | ✅ **pt-BR** — ainda não aplicado na UI (Sprint C1-r2) |
| D-02 | SQL Editor como tela separada | ✅ Sim |
| D-03 | Rótulos de ambiente | ✅ Domínio (Development/Test/Staging/Production/Unknown), cores do mockup |
| D-04 | Logo MySQL | ✅ Usar o logo MySQL — ⚠ confirmar a licença de uso da marca antes de publicar |
| D-05 | Licença do pack `CORE_PTBR` e do produto (DP-002) | ⬜ Pendente — necessária até o Gate V1 |
| **D-06** | **Ambiente de integração**: (a) instalar Docker Desktop (WSL2, containers Linux) na máquina de desenvolvimento; ou (b) usar só o GitHub Actions como evidência de integração | ⬜ **Pendente — bloqueia o Gate MVP.** Recomendado: (a) + (b) |
| **D-07** | **Servidores de desenvolvimento** (2026-10-12): MySQL e Firebird num servidor da rede local; SQLite local; PostgreSQL adiado. Credenciais **fora do repositório** (variáveis de ambiente) | ✅ Respondida — o servidor "MySQL" é MariaDB 10.5 e o Firebird é 2.5.9 (DA-205..207) |
| **D-08** | **Ritmo de desenvolvimento** (2026-10-12): fechar **um módulo por vez**, na ordem pedida pelo responsável | ✅ Respondida — ver §3.1 |

Marcação pelo responsável:
- [ ] **D-05** respondida · Resposta: ____
- [ ] **D-06** respondida · Resposta: ____ (D-07 cobre MySQL e Firebird manualmente; PostgreSQL e o CI continuam dependendo de D-06)

### 3.1 Módulo em andamento: Conexões e Explorer (branch `modulo/conexoes-explorer`)

Status: 🔄 Em andamento (2026-10-12). Os itens abaixo antecipam partes de B3, B4, C5r.4 e C6r.3 a pedido do responsável.

- [x] Conexões: ao selecionar um perfil salvo, mostrar todos os dados (inclusive a senha, com exibir/ocultar), editar e salvar (DA-310) · Concluído em: 2026-10-12 · Evidência: build sem avisos; Application.Tests
- [x] Explorer lista objetos de MySQL/MariaDB e Firebird 2.5 (extrator, type mapper e dialeto dos dois providers) (DA-206..208) · Concluído em: 2026-10-12 · Evidência: sonda contra os servidores de desenvolvimento (PV-203)
- [x] SQL do editor executa `SHOW`/`DESCRIBE`/`EXPLAIN` sem confirmação e scripts com vários SELECT no MySQL (DA-209, DA-210) · Concluído em: 2026-10-12 · Evidência: SqlClassifierTests; `QueryServiceTests.Script_FechaCursorDoComandoAnteriorAntesDoProximo`; sonda
- [x] Botão "+" abre nova aba; cada aba escolhe perfil e database próprios; F9 e Ctrl+Enter executam (seleção ou script); Ctrl+T, Ctrl+W, Ctrl+S e Esc (DA-311, DA-312) · Concluído em: 2026-10-12 · Evidência: build; smoke de UI
- [x] Explorer: seletor de conexão e database; árvore com ícones e grupos Triggers/Routines/Sequences; inspetor com Columns, Indexes, Foreign Keys, Checks, Triggers, References e Preview; menu de contexto abre o SQL numa aba sem apagar texto do usuário (DA-313) · Concluído em: 2026-10-12 · Evidência: `NavigatorAndInspectorTests.Catalogo_*`; sonda
- [x] Resultados: abas Results/Messages/Query Plan funcionais, filtro de linhas e Export (CSV/JSON) · Concluído em: 2026-10-12 · Evidência: build
- [ ] Verificação manual pelo responsável (PV-304) e merge na `main` · Concluído em: ____
- [ ] Pendências do módulo para a próxima rodada: carregar mais linhas além da primeira página; testes de contrato MySQL/Firebird automatizados lendo variáveis de ambiente (PV-203); `.xshd` por dialeto; recuperar abas ao reabrir o app

---

## 4. Mapa de etapas

| Etapa | Objetivo | IA-1 | IA-2 | IA-3 | Marco de saída |
|---|---|---|---|---|---|
| **E0** | Consolidar repositório e ambiente | Sprint **E0** (1 IA) | — | — | M-E0 |
| **E1** | Fechar o núcleo do MVP | **A2-r2** Motor multi-tabela | **B1** Integração real | **C1-r2** UI pt-BR e saneamento | M-E1 |
| **E2** | Providers do MVP e geração na aplicação | **B3** Firebird | **B2-r2** PostgreSQL | **C4a** Geração na aplicação/UI | M-E2 |
| **E3** | **Gate MVP** + início da V1 | **A4** Relacional avançado | **B4** MySQL | **C4b** Gate MVP | **M-MVP** |
| **E4** | V1 — bloco 1 | **A5** BR, mapeamento, formatos | **B5** Bulk e somente leitura | **C5-r2** Segurança e Explorer | M-E4 |
| **E5** | V1 — bloco 2 | **A6** Inserção direta (motor) | **B6** Matriz de versões | **C6-r2** Editor SQL | M-E5 |
| **E6** | V1 — bloco 3 | **C7a** Evolução de schema | **C7b** Fontes reais e L4 | **C8a** Catálogo na UI | M-E6 |
| **E7** | V1 — bloco 4 | **C9a** Acessibilidade e Settings | **C9b** Instalador e licenças | **C8b** Inserção direta na UI | M-E7 |
| **E8** | **Gate V1** | apoio (correções) | apoio (correções) | **C9c** Gate V1 | **M-V1** |

**Com 2 IAs:** IA-1 executa, em cada etapa, as sprints das colunas IA-1 e IA-2, em sequência (ex.: E1 = A2-r2 e depois B1). IA-2 executa a coluna IA-3. O Marco de saída só é marcado quando todas as sprints da etapa estão ✅.

**Por que essa ordem:**
- O caminho crítico do MVP é **motor multi-tabela (A2-r2) → geração na aplicação (C4a) → gate (C4b)**, com B1 → B2-r2/B3 em paralelo.
- A trilha C é a mais carregada. Nas etapas E6 e E7, IA-1 e IA-2 assumem sprints de aplicação/UI, sempre respeitando a posse de diretórios da trilha C.

---

## 5. Posse de diretórios por trilha

| Trilha | Diretórios |
|---|---|
| **A** | `src/MockDataStudio.{Generation,Output,Mapping}`, `src/MockDataStudio.Core/{Profiles,Bindings,Templates,Schema,Naming,Digest,Hashing,Diff,Types}`, testes correspondentes, `tests/fixtures/{generation-snapshots,mapping-corpus,plans}`, docs 12, 14–18, contratos C01, C04–C08 |
| **B** | `src/MockDataStudio.Providers.*`, `src/MockDataStudio.SqlTooling` (exceto `Completion/`), `tests/MockDataStudio.Providers.*`, `tests/MockDataStudio.SqlTooling.Tests`, `tests/fixtures/{schemas,sql-split}`, `tests/containers.json`, `tools/docker`, `.github/workflows`, versões em `Directory.Packages.props`, docs 06 §7, 10, 11, 20 §4–5, `docs/dependencies.md`, contrato C02 |
| **C** | `src/MockDataStudio.{Application,Projects,Infrastructure,Catalog,App}`, `src/MockDataStudio.Core/{Connections,Common}`, `src/MockDataStudio.SqlTooling/Completion`, testes correspondentes, `images/`, `README.md`, docs 09, 13, 19, 20 §6–11, 21, 22, 23, `docs/ui-inventory.md`, contratos C03, C09 |

- `ErrorCodes.cs` e `Strings.resx` aceitam **acréscimos** de qualquer trilha, no bloco do prefixo da trilha.
- Precisa mudar código de outra trilha? Registre um PV "requer coordenação" e descreva no relatório. Se for indispensável e pequeno, faça e marque o item com `⚠ CROSS-TRILHA` (justificativa obrigatória; precedente: PV-104).
- Testes de arquitetura (`T-ARC-*`) só mudam com registro DA.

---

## 6. Protocolo Git

### 6.1 Início da sprint
```bash
git checkout main
```
```bash
git pull origin main
```
Confirme na `main` que o marco da etapa anterior está ✅. Depois:
```bash
git checkout -b sprint/<ID>-<slug>
```
Pode usar worktree (`git worktree add ../MockDataStudio-<ID> -b sprint/<ID>-<slug>`). Ao final, remova-o (§6.3). Status → 🔄; commit `<ID>: início da sprint`.

### 6.2 Durante a sprint
- Commits pequenos, mensagem iniciando pelo ID (`A2-r2: motor multi-tabela (TASK-06.07)`).
- `git fetch origin` + `git merge origin/main` na branch pelo menos uma vez por dia e sempre que outra sprint da etapa for mesclada.
- Nunca commitar direto na `main` durante a sprint.

### 6.3 Fim da sprint (commit, merge e push)
1. Marque os checkboxes e preencha Status e Relatório. Commit `<ID>: conclusão da sprint`.
2. Incorpore a `main` mais recente na branch:
   ```bash
   git fetch origin
   ```
   ```bash
   git merge origin/main
   ```
   Resolva conflitos conforme §6.4.
3. Na branch: build Release (0 erros, 0 avisos) + suíte rápida `Category=Unit|Architecture|Contract`, e `Category=Integration` se a sprint tocou Providers, Output ou Application (ou CI como evidência).
4. Mescle:
   ```bash
   git checkout main
   ```
   ```bash
   git pull origin main
   ```
   ```bash
   git merge --no-ff sprint/<ID>-<slug> -m "Merge sprint <ID>: <título>"
   ```
5. Build e suíte rápida **na `main`**. Se falhar, corrija na branch e repita. Nunca deixe a `main` quebrada.
6. Publique: `git push origin main`. Push rejeitado → `git pull --no-rebase origin main`, repita o passo 5 e o push. **Nunca** `--force`.
7. Registre o hash do merge (commit `<ID>: registra merge` direto na `main`, única exceção permitida) e faça a limpeza:
   ```bash
   git branch -d sprint/<ID>-<slug>
   ```
   Se houver worktree: `git worktree remove ../MockDataStudio-<ID>`. Se a branch foi publicada: `git push origin --delete sprint/<ID>-<slug>`.

### 6.4 Conflitos

| Arquivo | Regra |
|---|---|
| `PLANO-DE-ACAO.md` | Cada IA editou só a sua sprint: mantenha ambas as seções |
| `docs/32` | Acréscimos por subseção de trilha: mantenha todas as linhas; IDs repetidos → renumere o **seu** e registre |
| `ErrorCodes.cs`, `Strings.resx` | União; mesmo código com significados diferentes → `⚠ BLOQUEADO` |
| `Directory.Packages.props` | União de pacotes novos; versões existentes só a trilha B altera |
| `docs/26`, `docs/35`, `docs/dependencies.md` | Acréscimos: mantenha ambos |
| Código de outra trilha | `git merge --abort`, `⚠ BLOQUEADO` e descrição no relatório |

---

## 7. Etapas e sprints

### ETAPA E0 — Consolidação do repositório e do ambiente

**Executor:** uma IA, ou o responsável quando o item exigir ação humana. **Sem branch de sprint** (operações de manutenção na `main`).

> 🤖 **IA: marque cada item com `[x]` ao concluir e preencha data e evidência.**

- [ ] **E0.1** Publicar os 4 commits locais da `main` (`git push origin main`); confirmar que `git rev-list --count origin/main..main` = 0. · Concluído em: ____ · Evidência: ____
- [ ] **E0.2** Remover os worktrees `../MockDataStudio-{A2,A3,b2,explorer2}` e as branches já mescladas (`sprint/A2-planejamento-relacional`, `sprint/A3-saida-sql`, `sprint/B2-postgresql`, `sprint/EXP-explorer-completo`), locais e remotas (`git push origin --delete sprint/A2-planejamento-relacional`). As continuações usam branches `-r2`. · Concluído em: ____ · Evidência: ____
- [ ] **E0.3** Resolver **D-06** (ambiente de integração) e, se for a opção (a), instalar e validar o Docker com `docker run --rm hello-world`. **Ação do responsável.** · Concluído em: ____ · Evidência: ____
- [ ] **E0.4** Confirmar o CI do GitHub verde no último commit da `main` (link da execução). Se falhar, abrir correção antes de E1. · Concluído em: ____ · Evidência: ____
- [ ] **E0.5** Atualizar o README (status da §2.2) e o `docs/00-index.md` (link deste plano e dos planos arquivados). · Concluído em: ____ · Evidência: ____

**Marco M-E0**
- [ ] `main` publicada, sem branches/worktrees mesclados pendentes, CI verde e D-06 decidido · Concluído em: ____

---

### ETAPA E1 — Fechar o núcleo do MVP

Pode iniciar com M-E0 marcado. **Exceção:** se D-06 ainda não estiver decidido, A2-r2 e C1-r2 podem começar, mas **B1** não.

#### Sprint A2-r2 — Motor multi-tabela e planejamento restante · Trilha A · IA-1

| Campo | Valor |
|---|---|
| Branch | `sprint/A2-r2-motor-multitabela` |
| Base já entregue (A2) | Grafo + Tarjan, `CycleResolver`, `KeyPlanner` (faixa/overflow/offline), `ExplicitKeyOracle`, `GenerationPlanner` + `PlanHash` |
| Referências | docs/16, docs/17, C06, C07, TASK-06.07, 07.02–07.06, PV-100, PV-101 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **A2r.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **A2r.1** `rel.key`, `rel.fk` ChildDriven (Uniform), FKs **compostas** e **sobrepostas** (`PLN-FK-OVERLAP`), `rel.child_seq`; `KeyOracle` para chaves compostas e UUID. *(TASK-07.04, resto da TASK-07.03)* · Concluído em: ____ · Evidência: ____
- [ ] **A2r.2** Autorreferência anulável por índice anterior (`rootRatio`, `maxDepth`); bloqueios `PLN-SELF-NOTNULL`/`PLN-SELF-CAPTURED`. *(resto da TASK-07.02)* · Concluído em: ____ · Evidência: ____
- [ ] **A2r.3** `IGenerationEngine.RunAsync(GenerationPlan, GenerationRunContext, IRowSink, …)` multi-tabela: passos em ordem, `DeferredUpdateStep` (NullThenUpdate), `ResyncSequenceStep`, função `parent()`; a execução por tabela vira interna (DA-052, PV-100). *(TASK-06.07)* · Concluído em: ____ · Evidência: ____
- [ ] **A2r.4** Integração motor → `SqlScriptWriter` (Output) sem adaptação manual de linhas; teste E2E em SQLite com `docs/examples/schema-loja-sqlite.sql`: script gerado, executado, FKs/UNIQUEs/CHECKs verificados e limpeza conferida (T-REL-002 SQLite). *(A2.8)* · Concluído em: ____ · Evidência: ____
- [ ] **A2r.5** `PreflightValidator` L2/L3 e interpretador de CHECK (T-VAL-LOJA, T-VAL-CHECK-001). *(TASK-07.06)* · Concluído em: ____ · Evidência: ____
- [ ] **A2r.6** Snapshots de plano dos **4 digests** de `docs/examples` (T-REL-PLAN-001, PV-101). · Concluído em: ____ · Evidência: ____
- [ ] **A2r.7** Determinismo e desempenho: mesma saída com MaxDOP 1 e 8 (T-GEN-DET-002/003, hash registrado); B-GEN-001 ≥ 100 mil linhas/s (sem writer). · Concluído em: ____ · Evidência: ____

**Verificação:** T-REL-001/002/005/006/007/012, T-REL-PLAN-001, T-GEN-DET-002/003, T-VAL-LOJA, T-VAL-CHECK-001 verdes; PV-100 e PV-101 resolvidas.
- [ ] Verificação concluída e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint B1 — Integração real e saneamento de testes · Trilha B · IA-2

| Campo | Valor |
|---|---|
| Branch | `sprint/B1-integracao-real` |
| Pré-condição | **D-06 resolvido** (Docker local e/ou CI) |
| Referências | docs/26 §1–3 e §8, DA-053, DA-055, docs/37 A-01..A-06, PV-102, PV-104 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **B1.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **B1.1** Fixtures `tests/fixtures/schemas/loja.{sqlite,postgresql,mysql,firebird}.sql` iguais à estrutura de `docs/examples` (singular, tabela `entrega` com FK composta); o digest da fixture SQLite deve ser igual a `docs/examples/digest-sqlite.json` (exceto hash, data e versão). *(SAN-B.1)* · Concluído em: ____ · Evidência: ____
- [ ] **B1.2** Testcontainers reais (PostgreSQL 17, MySQL 8.4, Firebird 5), banco novo por teste, `SELECT 1`; sem Docker → Skip com motivo; remover `ContainerSmokeTests` falso. *(SAN-B.2)* · Concluído em: ____ · Evidência: ____
- [ ] **B1.3** Host de `Providers.ContractTests` encerra sozinho em menos de 60 s (pools/conexões liberados). *(SAN-B.4)* · Concluído em: ____ · Evidência: ____
- [ ] **B1.4** CI: remover `continue-on-error` do job de integração; executar `Category=Contract|Integration`; `NuGetAudit` visível. *(SAN-B.2)* · Concluído em: ____ · Evidência: ____
- [ ] **B1.5** Revisar a correção cross-trilha `SqliteValues.ToStorage` (PV-104) e confirmar que não há aviso NU190x (build atual: 0 avisos); registrar em `docs/dependencies.md`. · Concluído em: ____ · Evidência: ____
- [ ] **B1.6** Corpus de split ampliado (MySQL `/*! */`, dollar-quoting com tag, `CASE … END` em PSQL, terminadores em strings). *(SAN-B.5)* · Concluído em: ____ · Evidência: ____

**Verificação:** smoke real verde no CI (link) e Skip local justificado; ContractTests encerra sozinho; T-SQL-SPLIT-* verdes.
- [ ] Verificação concluída e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C1-r2 — Interface em pt-BR e saneamento da UI · Trilha C · IA-3

| Campo | Valor |
|---|---|
| Branch | `sprint/C1-r2-ui-ptbr` |
| Base já entregue (C1/C2/C3) | Infraestrutura (DPAPI, settings, Serilog), `CAT-IMP-SIZE`, Projects, serviços de conexão/digest/mapeamento, telas reais |
| Referências | D-01, D-03, D-04, docs/21, docs/22 §7, PV-102, PV-300..303 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C1r.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C1r.1** **D-01:** todos os textos das views, diálogos e mensagens em pt-BR via `Strings.resx` (navegação incluída); remover o usuário fictício "JD / John Doe" do cabeçalho. Teste que falha se houver `Text="…"` literal em inglês nas views (lista de exceções explícita). *(SAN-C.2)* · Concluído em: ____ · Evidência: ____
- [ ] **C1r.2** **D-04:** logo MySQL em `Resources/Logos/mysql.png` (licença de uso registrada em `docs/dependencies.md`; sem licença confirmada, ícone genérico com o texto "MySQL"). · Concluído em: ____ · Evidência: ____
- [ ] **C1r.3** Tema escuro a partir de `Themes/Tokens.xaml`; docs/21 §2.1 atualizado com DA-027 e D-03. *(SAN-C.1)* · Concluído em: ____ · Evidência: ____
- [ ] **C1r.4** Componentes padronizados (`EnvironmentChip`, badges, `ProgressPanel`, `DiagnosticsList`) aplicados nas telas existentes. *(SAN-C.3)* · Concluído em: ____ · Evidência: ____
- [ ] **C1r.5** Smoke de UI estável (PV-102): 10 execuções seguidas da solução inteira sem falha intermitente. · Concluído em: ____ · Evidência: ____
- [ ] **C1r.6** Projetos: T-PRJ-007 e validações PRJ-GENERATOR/PRJ-PARAMS/PRJ-QUERY (PV-300); autosave e arquivo de recuperação (docs/22 §7, PV-301). · Concluído em: ____ · Evidência: ____
- [ ] **C1r.7** Fluxo só por teclado e salvar pelo diálogo automatizados (PV-302); capturas a 100%, 125% e 150% em `docs/progress/C1-r2/` (PV-303). · Concluído em: ____ · Evidência: ____

**Verificação:** App.UiTests estável (10/10), Projects/Application verdes, T-SEC-002/004/012 verdes.
- [ ] Verificação concluída e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

**Marco M-E1** (marcado por quem fizer o último merge da etapa)
- [ ] A2-r2, B1 e C1-r2 ✅ na `main`; build sem avisos; suíte rápida verde na `main` · Concluído em: ____

---

### ETAPA E2 — Providers do MVP e geração na aplicação

#### Sprint B3 — Provider Firebird · Trilha B · IA-1

| Campo | Valor |
|---|---|
| Branch | `sprint/B3-firebird` |
| Referências | docs/10 §6, docs/11 §4.4, TASK-08.03, PV-004/005/012/014 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **B3.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **B3.1** `TypeMapper` (domínios, charsets, OCTETS), versão, fingerprint (`MON$GUID`, PV-012). · Concluído em: ____ · Evidência: ____
- [ ] **B3.2** `SchemaExtractor` com heurística `tsq` e CHECK via `RDB$TRIGGERS`; digest de `loja` igual a `docs/examples/digest-firebird.json`. · Concluído em: ____ · Evidência: ____
- [ ] **B3.3** Dialeto e literais (T-OUT-LIT-FB), um INSERT por linha, `OVERRIDING SYSTEM VALUE` (PV-004), resync por versão (PV-005), constantes de `ISqlDialect` (DA-106). · Concluído em: ____ · Evidência: ____
- [ ] **B3.4** `TargetInspector`, `ErrorTranslator`, cancelamento (PV-014), `IBulkInserter` com prepared statement. · Concluído em: ____ · Evidência: ____
- [ ] **B3.5** Contrato T-PRV-FB-001..011 e T-PRV-FB-KEY-001..003 em FB 5; resultados FB 3/4 registrados; T-OUT-ROUNDTRIP-FB. · Concluído em: ____ · Evidência: ____

- [ ] Verificação (contrato FB 5 verde no CI; PV-004/005/012/014 resolvidas) e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint B2-r2 — Provider PostgreSQL contra servidor · Trilha B · IA-2

| Campo | Valor |
|---|---|
| Branch | `sprint/B2-r2-postgresql` |
| Base já entregue (B2/A3) | TypeMapper, dialeto, literais, extrator (não verificado), `BuildKeyRangeGuard` com `DO $$` (feito na A3) |
| Referências | docs/10 §5, TASK-08.02, PV-200..202 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **B2r.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **B2r.1** Extrator verificado contra PostgreSQL 17: digest de `loja` = `docs/examples/digest-postgresql.json` (normalização de CHECK, PV-200/201). · Concluído em: ____ · Evidência: ____
- [ ] **B2r.2** `TargetInspector` e `IBulkInserter` (INSERT multi-linha parametrizado; COPY fica na B5), cancelamento. · Concluído em: ____ · Evidência: ____
- [ ] **B2r.3** Contrato T-PRV-PG-001..011 e T-OUT-ROUNDTRIP-PG contra servidor (PV-202); EXPLAIN do PostgreSQL verificado (relatório do Explorer §13.3). · Concluído em: ____ · Evidência: ____

- [ ] Verificação (contrato PG 17 verde no CI; PV-200..202 resolvidas) e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C4a — Geração na aplicação e na interface · Trilha C · IA-3

| Campo | Valor |
|---|---|
| Branch | `sprint/C4a-geracao-app` |
| Depende de | A2-r2 (motor multi-tabela) |
| Referências | docs/21 S14–S16, C09 §2, TASK-09.02 (resto), TASK-09.07 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C4a.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C4a.1** `GenerationService` (plano, validação, preview, execução em script), `ReferenceDataSource` Generated/Fixed, coleta de `TargetState` (com destino conectado e offline). · Concluído em: ____ · Evidência: ____
- [ ] **C4a.2** Telas S14 (plano e diagnósticos), S15 (preview) e S16 (geração de script com progresso/cancelamento) no lugar do placeholder Generation; textos em pt-BR. · Concluído em: ____ · Evidência: ____
- [ ] **C4a.3** Fluxo completo em SQLite pela UI: projeto → digest → mapeamento → plano → script → abrir no editor (T-UI-FLOW-001 completo, T-UI-PERF-002). · Concluído em: ____ · Evidência: ____
- [ ] **C4a.4** History e Settings: placeholders marcados "Em desenvolvimento" em pt-BR (implementação em C8b e C9a). · Concluído em: ____ · Evidência: ____

- [ ] Verificação (Application/UiTests verdes) e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

**Marco M-E2**
- [ ] B3, B2-r2 e C4a ✅ na `main` · Concluído em: ____

---

### ETAPA E3 — Gate MVP e início da V1

A4 e B4 podem ser mescladas durante o gate. **O gate é avaliado sobre a `main` no momento do merge da C4b.**

#### Sprint C4b — GATE MVP · Trilha C · IA-3

| Campo | Valor |
|---|---|
| Branch | `sprint/C4b-gate-mvp` |
| Referências | docs/29 §2, TASK-08.04 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C4b.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C4b.1** E2E T-E2E-SQLITE/PG/FB-001 e T-E2E-003; scripts executados pelos clientes oficiais (`sqlite3`, `psql`, `isql`) nos containers. *(TASK-08.04)* · Concluído em: ____ · Evidência: ____
- [ ] **C4b.2** `docs/examples/output-postgresql.sql` e `output-firebird.sql` substituídos por saídas reais. · Concluído em: ____ · Evidência: ____
- [ ] **C4b.3** G-MVP-7: 1 milhão de linhas de `cliente` com CPF único, ≤ 300 MB e ≥ 100 mil linhas/s (pode pedir apoio da IA-1). · Concluído em: ____ · Evidência: ____
- [ ] **C4b.4** README e docs/30 atualizados; tag `v0.1.0-mvp` na `main` após o merge (`git tag -a v0.1.0-mvp -m "MVP"` e `git push origin v0.1.0-mvp`). · Concluído em: ____ · Evidência: ____

**Checklist do Gate MVP ([docs/29 §2](docs/29-acceptance-criteria.md))**
- [ ] G-MVP-1 — `loja` em SQLite, PostgreSQL 17 e Firebird 5 ponta a ponta sem erros
- [ ] G-MVP-2 — contagens, FKs (compostas e autorreferência), UNIQUEs e CHECKs verificados
- [ ] G-MVP-3 — scripts idênticos com a mesma seed (MaxDOP 1 e 8)
- [ ] G-MVP-4 — ciclo anulável gerado; ciclo NOT NULL bloqueado com diagnóstico
- [ ] G-MVP-5 — importação de catálogo funcionando; script com `DROP` rejeitado
- [ ] G-MVP-6 — metas do corpus de mapeamento
- [ ] G-MVP-7 — desempenho e memória (C4b.3)
- [ ] G-MVP-8 — fluxo MVP na UI só por teclado
- [ ] G-MVP-9 — testes de arquitetura e segurança do MVP verdes
- [ ] G-MVP-10 — relatório do marco e riscos residuais atualizados
- [ ] Merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint / MVP:** ____

#### Sprint A4 — Relacional avançado (motor) · Trilha A · IA-1

| Campo | Valor |
|---|---|
| Branch | `sprint/A4-relacional-avancado` |
| Referências | docs/16 §3–9, TASK-16.01..16.04 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **A4.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **A4.1** `DeferredConstraint` e `DisableChecks` (opt-in) no motor e no writer; autorreferência NOT NULL conforme PV-007 (B4); SGBD não verificado mantém `PLN-SELF-NOTNULL`. *(TASK-16.01 parte motor)* · Concluído em: ____ · Evidência: ____
- [ ] **A4.2** ParentDriven, Zipf, Poisson, 1:1 e `PLN-PARENTDRIVEN-MULTI`. *(TASK-16.02)* · Concluído em: ____ · Evidência: ____
- [ ] **A4.3** Fontes Existing/Fixed/Dataset/Query no motor via `IReferenceDataSource`/`KeyPool` (fonte fake nos testes; leitura real na C7b). *(TASK-16.03 parte motor)* · Concluído em: ____ · Evidência: ____
- [ ] **A4.4** `NaturalKeyLookup` (T-REL-PLAN-002 com execução em SQLite). *(TASK-16.04)* · Concluído em: ____ · Evidência: ____

- [ ] Verificação (T-REL-003/004/008/009/010, T-REL-PLAN-002) e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint B4 — Provider MySQL · Trilha B · IA-2

| Campo | Valor |
|---|---|
| Branch | `sprint/B4-mysql` |
| Referências | docs/10 §4, TASK-10.01..10.03, PV-007, PV-010 |

**Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **B4.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **B4.1** `TypeMapper` (`tinyint(1)`, unsigned, enum), leitura de `sql_mode`. *(TASK-10.01)* · Concluído em: ____ · Evidência: ____
- [ ] **B4.2** `SchemaExtractor` (PV-010): digest de `loja` = `docs/examples/digest-mysql.json`. · Concluído em: ____ · Evidência: ____
- [ ] **B4.3** Dialeto, literais e escapes com e sem `NO_BACKSLASH_ESCAPES` (T-PRV-MYSQL-ESC-001); EXPLAIN do MySQL verificado. *(TASK-10.02)* · Concluído em: ____ · Evidência: ____
- [ ] **B4.4** `TargetInspector`, `ErrorTranslator`, bulk multi-linha, contrato MySQL 8.4. *(TASK-10.03)* · Concluído em: ____ · Evidência: ____
- [ ] **B4.5** PV-007: autorreferência NOT NULL na mesma linha testada nos 4 SGBDs (T-REL-SELF-NN-<X>), com o resultado registrado para a trilha A. · Concluído em: ____ · Evidência: ____

- [ ] Verificação (contrato MySQL 8.4 verde no CI; PV-007/010 resolvidas) e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

**Marco M-MVP**
- [ ] C4b ✅ com Gate MVP aprovado; A4 e B4 ✅ na `main` · Concluído em: ____

---

### ETAPA E4 — V1, bloco 1

#### Sprint A5 — Geradores BR, mapeamento avançado e formatos · Trilha A · IA-1

**Branch:** `sprint/A5-br-mapeamento-formatos` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **A5.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **A5.1** `br.phone`, `br.placa`, `core.uuid` v7; T-GEN-BR-001 completo; CNPJ alfanumérico conforme PV-011 (ou adiado com registro). *(TASK-17.05)* · Concluído em: ____ · Evidência: ____
- [ ] **A5.2** Evidências `HistoryMatch`, `CheckConstraintHint` e `UniqueCapacity`; o lado do motor do aprendizado (DA-041). *(TASK-17.04 parte A)* · Concluído em: ____ · Evidência: ____
- [ ] **A5.3** Writers CSV e JSON Lines com manifesto (T-OUT-005). *(TASK-18.02)* · Concluído em: ____ · Evidência: ____
- [ ] **A5.4** Benchmarks B-GEN-001, B-MEM-001, B-OUT-001/002, T-GEN-VOL-001 em `docs/benchmarks/`. *(TASK-18.03)* · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint B5 — Bulk, somente leitura e consultas · Trilha B · IA-2

**Branch:** `sprint/B5-bulk-readonly` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **B5.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **B5.1** COPY binário no PostgreSQL (PV-006), multi-linha ajustado ao pacote no MySQL, batch do Firebird 4+ (PV-002) ou prepared; B-DIR-<X>. *(TASK-15.02)* · Concluído em: ____ · Evidência: ____
- [ ] **B5.2** `ApplySessionReadOnlyAsync` nos 4 providers; T-SEC-006 no nível de provider. *(TASK-11.02)* · Concluído em: ____ · Evidência: ____
- [ ] **B5.3** Configurações avançadas (allow-list), nova tentativa na abertura (2×), timeouts. *(TASK-11.03)* · Concluído em: ____ · Evidência: ____
- [ ] **B5.4** Alinhar `IQueryExecutor` (C02) com o `QueryService`/`ResultCursor` já existente em `Application/Explorer` (sem duplicar; registrar DA); savepoints para L4; `AllKeysExistAsync`/`ReadKeysAsync` nos 4 SGBDs. · Concluído em: ____ · Evidência: ____
- [ ] Verificação (T-PRV-<X>-006..010 nos 4 SGBDs; PV-002/006) e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C5-r2 — Segurança de execução e Explorer · Trilha C · IA-3

**Branch:** `sprint/C5-r2-seguranca-explorer` · **Base já entregue (EXP):** `ExecutionGuard` do editor, diálogos de confirmação, busca, referências, menu de contexto, scripts de objeto · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C5r.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C5r.1** `ExecutionGuard` com a matriz completa de docs/23 §5.2 (inserção direta, limpeza, L4). *(TASK-11.01)* · Concluído em: ____ · Evidência: ____
- [ ] **C5r.2** `ExecutionAuthorization` vinculada ao fingerprint e ao PlanHash, com validade de 10 min (T-SEC-001/003/011). *(TASK-15.01)* · Concluído em: ____ · Evidência: ____
- [ ] **C5r.3** Alerta de servidor divergente do registrado no projeto. *(TASK-11.04)* · Concluído em: ____ · Evidência: ____
- [ ] **C5r.4** Explorer: carga lazy por nível e virtualização (T-UI-PERF-001), refresh por nó, triggers/sequences/rotinas na árvore, abas do inspetor no XAML, seletor de conexão e reconexão. *(TASK-12.01..12.03)* · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

**Marco M-E4**
- [ ] A5, B5 e C5-r2 ✅ na `main` · Concluído em: ____

---

### ETAPA E5 — V1, bloco 2

#### Sprint A6 — Inserção direta (motor e saída) · Trilha A · IA-1

**Branch:** `sprint/A6-insercao-direta` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **A6.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **A6.1** `DirectInsertWriter`: PerBatch/Single, falhas, cancelamento, nova tentativa em deadlock. *(TASK-15.03)* · Concluído em: ____ · Evidência: ____
- [ ] **A6.2** Captured + `KeyRegistry` (arquivo temporário acima de 10 milhões de chaves). *(TASK-15.04)* · Concluído em: ____ · Evidência: ____
- [ ] **A6.3** Limpeza por faixas confirmadas e por chaves capturadas; `RunOutcome` completo. *(TASK-15.05 parte motor)* · Concluído em: ____ · Evidência: ____
- [ ] **A6.4** T-DIR-<X>-001 e T-DIR-002..005 nos 4 SGBDs (containers). · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint B6 — Matriz de versões e robustez · Trilha B · IA-2

**Branch:** `sprint/B6-matriz-versoes` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **B6.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **B6.1** Workflow nightly: PostgreSQL 14–18, MySQL 8.4/9.x, Firebird 3/4/5; MySQL 8.0 e MariaDB em melhor esforço. · Concluído em: ____ · Evidência: ____
- [ ] **B6.2** B-INT-001 (1.000 tabelas em ≤ 30 s, PostgreSQL e Firebird). · Concluído em: ____ · Evidência: ____
- [ ] **B6.3** Correções da matriz e registro em docs/06 §7. · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C6-r2 — Editor SQL · Trilha C · IA-3

**Branch:** `sprint/C6-r2-editor-sql` · **Base já entregue (EXP):** Run, Stop, Explain (SQLite), Format, salvar/abrir script, exportação da página, barra de estado · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C6r.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C6r.1** `QuerySession` com conexão dedicada, modos Manual/AutoCommit, Run da seleção/comando atual/script, somente leitura no servidor (B5). *(TASK-13.01)* · Concluído em: ____ · ⏳ PARCIAL: `QuerySession` na aba (conexão emprestada; BEGIN/COMMIT/ROLLBACK; troca de modo bloqueada com transação aberta; 5 testes SQLite). Modo Manual e botões Commit/Rollback na barra da aba. Run da seleção já existia. Falta conexão dedicada separada da navegação, somente leitura por sessão (B5) e Firebird (sintaxe de transação diferente)
- [ ] **C6r.2** Autocomplete em `SqlTooling/Completion` (T-SQL-COMP-001, B-SQL-001). *(TASK-13.02)* · Concluído em: ____ · ⏳ PARCIAL: `SqlCompletionEngine` (10 testes), `CompletionCatalogService` (2 testes SQLite) e popup no editor (catálogo carregado ao conectar; disparo por letra, ponto ou Ctrl+Espaço; a verificação visual não foi feita por UI automatizada). Falta T-SQL-COMP-001/B-SQL-001 com esses IDs e teste FlaUI
- [ ] **C6r.3** Abas por perfil, chip de ambiente, `.xshd` por dialeto, recuperação de abas. *(TASK-13.03)* · Concluído em: ____ · ⏳ PARCIAL: abas por perfil (existentes); barra de estado por aba; recuperação de abas (`EditorTabStore`, gravação atômica e com atraso, 3 testes; restaurada ao abrir o app); modo de transação na barra da aba. Falta `.xshd` por dialeto (há só o SQL genérico) e teste da recuperação na UI
- [ ] **C6r.4** "Carregar mais", exportação completa por cursor, histórico (T-SQL-HIST-001), posição do erro no editor. *(TASK-13.04)* · Concluído em: ____ · ⏳ PARCIAL: `ExecutionHistory` (1 teste, sem ligação à tela). Falta "carregar mais", exportação por cursor, histórico no editor e posição do erro
- [ ] Verificação (T-SQL-EXEC-001/002, T-UI-PERF-003, T-UI-FLOW-003) e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

**Marco M-E5**
- [ ] A6, B6 e C6-r2 ✅ na `main` · Concluído em: ____

---

### ETAPA E6 — V1, bloco 3 (sprints de aplicação distribuídas entre as 3 IAs; posse da trilha C)

#### Sprint C7a — Evolução de schema e projetos · IA-1

**Branch:** `sprint/C7a-evolucao-schema` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C7a.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C7a.1** Snapshots e diff na tela Schema Digest. *(TASK-14.01)* · Concluído em: ____ · ⏳ PARCIAL: captura, lista de snapshots, comparação com contagem por tipo e tabela de mudanças já na tela; comparação testada (`SchemaDifferTests`, `ProjectServicesTests`). Falta teste de UI da tela
- [ ] **C7a.2** Migração de bindings (Active/NeedsReview/Orphaned + renomeações; T-DIG-030, T-E2E-004). *(TASK-14.02)* · Concluído em: ____ · ⏳ PARCIAL: `BindingMigrator` (mantido, renomeado por fingerprint único, revisão, órfão; 6 testes) aplicado ao capturar um snapshot, com a contagem na mensagem de status. Falta exibir os estados na tela de Mapping e os IDs T-DIG-030/T-E2E-004
- [ ] **C7a.3** Múltiplos perfis, migração de formato, exportação sanitizada, confiança em projetos de terceiros, dependências de catálogo (T-PRJ-004, T-SEC-008). *(TASK-14.03, 14.04)* · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C7b — Fontes de referência reais, validação L4 e relacionamentos · IA-2

**Branch:** `sprint/C7b-fontes-l4` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C7b.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C7b.1** `ReferenceDataSource` Existing/Query/Dataset com leitura real e verificação das chaves no destino (T-REL-010/011, T-SEC-007). *(TASK-16.03 parte C)* · Concluído em: ____ · Evidência: ____
- [ ] **C7b.2** `TrialInsertRunner` (L4) com rollback e bloqueio em Production (T-VAL-TRIAL-001/002, T-VAL-001..040). *(TASK-16.05)* · Concluído em: ____ · Evidência: ____
- [ ] **C7b.3** Tela de Relacionamentos (S13) integrada à geração; T-E2E-MYSQL-001. · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C8a — Catálogo completo na interface · IA-3

**Branch:** `sprint/C8a-catalogo-ui` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C8a.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C8a.1** Templates com edição completa (S07/S08). *(TASK-17.01)* · Concluído em: ____ · ⏳ PARCIAL: edição de templates escalares e compostos na tela (novo, carregar, salvar, copiar, excluir; embutidos bloqueados; campos e registros em texto no composto), sobre `TemplateEditor` e `CompositeTemplateEditor`; 17 testes contra o catálogo real. Falta teste de UI do fluxo e a ressalva de que o carregador devolve NULL como texto vazio (registrada no teste)
- [ ] **C8a.2** Importação por script (S09) com "Copiar instruções para IA" (T-UI-FLOW-002). *(TASK-17.02)* · Concluído em: ____ · ⏳ PARCIAL: análise e importação de script SQL pelo importador de staging, instruções para IA copiadas para a área de transferência (testes de análise, mescla e instruções). Falta teste de fluxo de UI (T-UI-FLOW-002)
- [ ] **C8a.3** Packs JSON (T-CAT-008). *(TASK-17.03)* · Concluído em: ____ · ⏳ PARCIAL: importação de pack JSON (convertido em script pelo instalador; pack malformado vira erro) e exportação dos templates do usuário no formato `template-pack-1.0`, com botão na tela; 4 testes de ida e volta (exporta, reimporta num catálogo limpo e compara o conteúdo; embutidos ficam de fora). Falta o ID T-CAT-008 nos testes e a exportação de pack com ajuste de metadados (autor e licença)
- [ ] **C8a.4** Presets, regras aprendidas e painel de diagnóstico de mapeamento (T-MAP-007). *(TASK-17.04 parte C)* · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

**Marco M-E6**
- [ ] C7a, C7b e C8a ✅ na `main` · Concluído em: ____

---

### ETAPA E7 — V1, bloco 4

#### Sprint C9a — Acessibilidade, Settings e diagnóstico · IA-1

**Branch:** `sprint/C9a-a11y-settings` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C9a.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C9a.1** Acessibilidade e DPI de 100% a 200% (T-UI-A11Y-*, T-UI-DPI-001). *(TASK-19.01)* · Concluído em: ____ · Evidência: ____
- [ ] **C9a.2** Tela Settings (S18), paleta de comandos, atalhos. *(TASK-19.02)* · Concluído em: ____ · Evidência: ____
- [ ] **C9a.3** Pacote de diagnóstico e logs. *(TASK-19.03)* · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C9b — Instalador, licenças e manual · IA-2

**Branch:** `sprint/C9b-instalador-licencas` · **Pré-condição:** D-05 respondida · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C9b.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C9b.1** Instalador (DP-003, DP-008), verificação offline (T-SEC-NET-001). *(TASK-19.04)* · Concluído em: ____ · Evidência: ____
- [ ] **C9b.2** `docs/licenses.md` (inclui logo MySQL e PV-001) e licença do pack/produto (D-05). · Concluído em: ____ · Evidência: ____
- [ ] **C9b.3** Pack `CORE_PTBR` completo (volumes de docs/09 §6) com `SOURCES.md`. *(TASK-17.06)* · Concluído em: ____ · Evidência: ____
- [ ] **C9b.4** Manual do usuário local (F1). *(TASK-19.05)* · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

#### Sprint C8b — Inserção direta e histórico na interface · IA-3

**Branch:** `sprint/C8b-insercao-direta-ui` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C8b.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C8b.1** Fluxo de inserção direta na UI com autorização (C5-r2) e limpeza (T-UI-FLOW-004). *(TASK-15.05 parte C)* · Concluído em: ____ · Evidência: ____
- [ ] **C8b.2** Tela History (S17): reexecutar com a mesma seed, abrir artefatos, executar limpeza; execuções interrompidas. · Concluído em: ____ · Evidência: ____
- [ ] **C8b.3** T-E2E-<X>-002 nos 4 SGBDs (inserção + limpeza devolvem o estado original). · Concluído em: ____ · Evidência: ____
- [ ] Verificação e merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint:** ____

**Marco M-E7**
- [ ] C9a, C9b e C8b ✅ na `main` · Concluído em: ____

---

### ETAPA E8 — Gate V1

#### Sprint C9c — GATE V1 · IA-3 (IA-1 e IA-2 corrigem falhas em branches `fix/<ID>-<slug>`)

**Branch:** `sprint/C9c-gate-v1` · **Status:** ⬜ Não iniciada

> 🤖 **IA: ao concluir cada item, marque `[x]` e preencha data e evidência. Ao final, faça commit, merge e push (§6.3).**

- [ ] **C9c.0** Início conforme §6.1. · Concluído em: ____ · Evidência: ____
- [ ] **C9c.1** Nenhuma referência a dados de exemplo no código de produção; placeholders removidos. · Concluído em: ____ · Evidência: ____
- [ ] **C9c.2** Auditoria final documentação × implementação (formato de docs/37), sem divergência crítica/alta aberta. · Concluído em: ____ · Evidência: ____
- [ ] **C9c.3** Tag `v1.0.0` na `main` após o merge. · Concluído em: ____ · Evidência: ____

**Checklist do Gate V1 ([docs/29 §3](docs/29-acceptance-criteria.md))**
- [ ] G-V1-1 — todos os FR MVP/V1 (M) atendidos; matriz docs/35 100% verde
- [ ] G-V1-2 — matriz oficial de versões de SGBD verde
- [ ] G-V1-3 — inserção direta + limpeza restaura o banco nos 4 SGBDs
- [ ] G-V1-4 — editor SQL: guarda, transação manual e cancelamento nos 4 SGBDs
- [ ] G-V1-5 — evolução de schema preserva bindings
- [ ] G-V1-6 — NFRs medidos e dentro das metas (ou desvio aprovado)
- [ ] G-V1-7 — acessibilidade e DPI
- [ ] G-V1-8 — instalação em Windows 10/11 limpos e execução offline
- [ ] G-V1-9 — licenças e procedência dos dados registradas
- [ ] G-V1-10 — auditoria final sem divergências críticas/altas
- [ ] Merge/push conforme §6.3 · Concluído em: ____

**Relatório da sprint / V1:** ____

**Marco M-V1**
- [ ] Gate V1 aprovado e `v1.0.0` publicada · Concluído em: ____

---

## 8. Pendências rastreadas (onde cada uma é resolvida)

| Pendência | Origem | Sprint |
|---|---|---|
| PV-100, PV-101 (motor por plano; snapshots de plano) | A2 | A2-r2 |
| PV-102 (smoke de UI intermitente) | A2/EXP | C1-r2 |
| PV-103 (saída só verificada em SQLite) | A3 | B2-r2, B3, B4 (round-trip) e C4b (E2E) |
| PV-104 (correção cross-trilha em `SqliteValues`) | A3 | B1 |
| PV-200..202 (PostgreSQL sem servidor) | B2 | B2-r2 |
| PV-300..303 (projetos, teclado, visual/DPI) | C2/C3 | C1-r2 |
| SAN-B.1..B.5 (fixtures, Testcontainers, CI, host, corpus) | Auditoria | B1 |
| SAN-C.1..C.4 (tema, textos, componentes, logo) | Sprint 1 | C1-r2 |
| SAN-A.7 (evidências de mapeamento) | Sprint 5 | A5 |
| Pendências do Explorer/Editor (relatório §13–14) | EXP | C5-r2, C6-r2 |
| D-05, D-06 | Responsável | E0 / C9b |

---

## 9. Depois da V1 (V2 — detalhar antes de iniciar)

CLI · Plugins · Grafo de relacionamentos · Designer de cenários · Regras condicionais declarativas · Perfil estatístico · Anonimização restrita · Perfis de carga e cobertura · Pacotes temáticos · MariaDB oficial · Coerência entre tabelas ([docs/27 §4](docs/27-development-roadmap.md)).

---

## 10. Relatório da análise de 2026-10-12

Execução completa na `main` (`01f5a98`), Release, nesta máquina (Windows, sem Docker):

| Projeto de teste | Aprovados | Falhos | Ignorados | Observação |
|---|---|---|---|---|
| Core.Tests | 109 | 0 | 0 | |
| Generation.Tests | 104 | 0 | 0 | |
| Mapping.Tests | 22 | 0 | 0 | |
| Catalog.Tests | 22 | 0 | 0 | inclui `CAT-IMP-SIZE` (Slow) |
| SqlTooling.Tests | 32 | 0 | 0 | |
| Output.Tests | 28 | 0 | 0 | saída verificada só em SQLite |
| Projects.Tests | 8 | 0 | 0 | |
| Application.Tests | 110 | 0 | 0 | |
| Architecture.Tests | 10 | 0 | 0 | |
| Providers.ContractTests | 41 | 0 | 4 | 3 N/A no SQLite + 1 PostgreSQL real sem Docker; o host encerrou normalmente nesta execução |
| Providers.IntegrationTests | 3 | 0 | 0 | ⚠ ainda é o smoke **falso** (não sobe container; DA-055, sprint B1) |
| App.UiTests | 0 | **2** | 0 | ⚠ smoke e T-UI-FLOW-001 falharam (texto esperado não encontrado; fluxo parou na tela Mapping). Intermitente segundo PV-102 — sprint C1-r2 |
| **Total** | **489** | **2** | **4** | Build: 0 erros, 0 avisos |

Outras constatações:
- Nenhum teste contra servidor PostgreSQL, MySQL ou Firebird foi executado até hoje (sem Docker). O Gate MVP depende de D-06.
- Shell da UI ainda exibe o usuário fictício "JD / John Doe" dos mockups (incluído em C1r.1).
- `NotImplementedException` em produção: `FirebirdProvider` (5 membros), `MySqlProvider` (5), `PostgreSqlProvider` (2: bulk e inspector). Os serviços de Application tratam o caso (Explorer, Digest).
- CI: job de integração ainda com `continue-on-error: true` (B1.4).
- Testes de UI rodam com o desktop interativo; nesta execução falharam os dois, ambos ao procurar textos na tela, o que reforça PV-102.
