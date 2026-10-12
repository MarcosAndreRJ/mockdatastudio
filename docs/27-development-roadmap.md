# 27 — Roadmap de Desenvolvimento e Controle de Escopo

| Campo | Valor |
|---|---|
| Objetivo | Definir MVP técnico, V1 profissional e V2 avançada; classificar funcionalidades premium; registrar dependências, contratos antecipados e abstrações prematuras. |
| Escopo | Produto inteiro. |
| Relacionados | [28](28-implementation-backlog.md), [29](29-acceptance-criteria.md), [02](02-requirements.md) |

## 1. Marcos

```mermaid
flowchart LR
    M0[M0 Fundação<br/>repo, CI, arquitetura] --> M1[M1 Núcleo de schema<br/>modelo, digest, SQLite]
    M1 --> M2[M2 Catálogo + Mapeamento]
    M2 --> M3[M3 Motor de geração + relacional]
    M3 --> M4[M4 Saída SQL + PG + FB]
    M4 --> MVP{{MVP técnico}}
    MVP --> M5[M5 MySQL + conexões seguras + explorer]
    M5 --> M6[M6 Editor SQL]
    M6 --> M7[M7 Projetos, snapshots, evolução]
    M7 --> M8[M8 Inserção direta + limpeza + validação L4]
    M8 --> M9[M9 Compostos, catálogo UI, packs, BR]
    M9 --> M10[M10 Volume, CSV/JSON, hardening, a11y, instalador]
    M10 --> V1{{V1 profissional}}
    V1 --> V2[V2 avançada]
```

## 2. MVP técnico — "provar o núcleo"

**Objetivo**: demonstrar que o modelo canônico, o mapeamento determinístico, o motor relacional determinístico e a geração de SQL funcionam em SGBDs com metadados muito diferentes.

| Inclui | Por quê |
|---|---|
| Providers **SQLite, PostgreSQL, Firebird** (introspecção, dialeto, target inspector; `IBulkInserter` implementado e coberto pela suíte de contrato, mas **sem** fluxo de inserção direta na UI — este entra no M8) | SQLite = testes rápidos; PG = metadados mais ricos (identity, deferrable, schemas); FB = o mais divergente (sem schemas, generators/triggers, sem multi-row). Se a abstração suporta os três, suporta MySQL |
| Modelo canônico, digest 1.0 (serialização, validação, hash) | Contrato central |
| Catálogo SQLite com migrações, staging import, pack mínimo | Requisito original (scripts SQL) e base do mapeamento |
| Mapeamento estrutural + semântico (sem aprendizado) | Prova do algoritmo |
| Motor: RNG, geradores core/catalog/rel, MDS-Expr (format), unicidade (construtiva/bijetiva/capacidade), nulos | Núcleo |
| Planner: SCC, NullThenUpdate, autorreferência anulável, Explicit keys, ChildDriven uniform, FK composta | Riscos B/C |
| Validação L1–L3 | Risco E (parcial) |
| Script SQL para os 3 SGBDs, resync, cabeçalho | Saída principal |
| Projeto `.mdsproj` (1 perfil, 1 snapshot) | Persistência mínima |
| UI mínima: conexões, captura, projeto (tabelas + mapeamento + gerador), plano/validação, geração de script | Demonstração utilizável |

**Exclui** (com justificativa): MySQL (abstração já provada; entra no M5), editor SQL (não é necessário para provar o motor), inserção direta PG/FB (riscos de bulk isolados no M8), compostos na UI (motor suporta; UI no M9), CSV/JSON, L4.

## 3. V1 profissional

Tudo do MVP mais: MySQL; conexões completas (ambientes, guarda, avançadas, resiliência); explorer completo; editor SQL completo; snapshots/diff/migração de bindings; múltiplos perfis; inserção direta nos 4 SGBDs com autorização, Captured, limpeza; validação L4; Deferred/DisableChecks; ParentDriven/Zipf/1:1; Existing/Fixed/Dataset/Query; templates compostos na UI; packs; geradores BR; segmentação; CSV/JSON Lines; histórico; diagnóstico; acessibilidade; instalador.

## 4. V2 avançada (visão preservada)

| Item | FR |
|---|---|
| Designer visual de cenários | FR-200 |
| Regras condicionais declarativas | FR-201 |
| Grafo de relacionamentos | FR-202 |
| Plugins externos | FR-203 |
| CLI | FR-204 |
| Perfil estatístico (agregados) | FR-205 |
| Anonimização/pseudonimização (restrita) | FR-206 |
| Perfis de carga | FR-207 |
| Cobertura de cenários | FR-208 |
| Pacotes temáticos + `.mdspkg` | FR-209 |
| Coerência de slot entre tabelas | FR-210 |
| MariaDB oficial; arrays/JSON estruturado; paralelismo entre tabelas; auditoria assinada | FR-211..214 |

## 5. Avaliação das funcionalidades premium (briefing §5) e adicionais

Escalas: Benefício/Complexidade/Risco = A (alto), M, B.

| # | Funcionalidade | Benefício | Complex. | Risco | Dependências | Prioridade | Fase |
|---|---|---|---|---|---|---|---|
| 1 | Designer visual de cenários | M | A | M | Perfis, plano | C | V2 |
| 2 | Registros relacionados por cardinalidade | A | M | B | Planner | M | MVP (Child) / V1 (Parent, 1:1) |
| 3 | Distribuições estatísticas | A | B | B | RNG | M | MVP (uniform/normal) / V1 (zipf, poisson) |
| 4 | Regras condicionais entre colunas | A | M | B | MDS-Expr | S | V1 (`core.switch`, `if`) / V2 (declarativas) |
| 5 | Dependências entre tabelas | A | M | M | Grafo | M | MVP |
| 6 | Detecção de mudanças estruturais | A | M | B | Hash, diff | M | V1 |
| 7 | Comparação de digests | A | M | B | Diff | M | V1 |
| 8 | Validação avançada de restrições | A | A | M | Validador, providers | M | MVP (L1–L3) / V1 (L4) |
| 9 | Diagnóstico de mapeamentos | A | B | B | Mapeamento | M | V1 |
| 10 | Estimativa de volume/desempenho | M | B | B | Plano | S | V1 |
| 11 | Catálogo de geradores reutilizáveis (presets) | A | B | B | Catálogo | M | V1 |
| 12 | Pacotes temáticos | M | B | M (licença de dados) | Packs | S | V1 (pt-BR) / V2 (temas) |
| 13 | Visualizador de relacionamentos | M | M | B | Grafo | C | V2 |
| 14 | Exportação CSV/JSON | M | B | B | Writers | S | V1 |
| 15 | Exportação segmentada | A | B | B | Writer | M | V1 |
| 16 | Histórico de execuções | A | B | B | Catálogo | M | V1 |
| 17 | Reprodução determinística | A | M | M | RNG, versões | M | MVP |
| 18 | Perfis de conexão por ambiente | A | B | B | Conexões | M | MVP (tag) / V1 (guarda) |
| 19 | Auditoria | M | M | B | Histórico | C | V2 |
| 20 | Plugins | M | A | A (segurança) | Contratos estáveis | C | V2 |
| 21 | CLI | A | B | B | Application sem UI | S | V2 (contratos prontos desde MVP) |
| 22 | Análise estatística de dados existentes | M | A | A (privacidade) | Providers | C | V2 |
| 23 | Anonimização/pseudonimização | M | A | A (LGPD, escopo) | 22 | C | V2 restrito: somente transformação determinística de colunas mapeadas em cópia; nunca sem confirmação |
| 24 | Perfis de carga | M | M | B | Perfis | C | V2 |
| 25 | Cobertura de cenários | M | M | B | Relatórios | C | V2 |
| A1 | Script de limpeza | A | B | B | Keys Explicit | M | V1 |
| A2 | Inserção de teste com rollback | A | M | M | Providers | S | V1 |
| A3 | Importação de digest offline | A | B | B | Digest | M | MVP |
| A4 | Instruções "copiar para IA" | M | B | B | Staging | S | V1 |
| A5 | Geradores BR algorítmicos | A | B | B | Motor | M | V1 |
| A6 | Paleta de comandos | M | B | B | UI | S | V1 |

## 6. Contratos que precisam existir desde o MVP (evitar retrabalho)

| Contrato | Motivo |
|---|---|
| `IDatabaseProvider` completo (inclui `IBulkInserter`, `ITargetInspector`, `ProviderCapabilities`) | MySQL e inserção direta entram depois sem quebrar |
| `GeneratorSpec` com `Version` e `Children` | Versionamento e composição |
| Digest 1.0 com `x` e chaves reservadas | Evolução MINOR |
| `.mdsproj` com `profiles[]` (lista) mesmo usando 1 | Multi-perfil no V1 sem migração MAJOR |
| `KeyStrategy` com Captured/NaturalKeyLookup no enum | Planner já decide/bloqueia corretamente |
| `IRowSink`/`IOutputWriter` | Inserção direta/CSV no V1 |
| `IReferenceDataSource` | Existing/Query no V1 |
| Application sem dependência de WPF | CLI V2 |
| `ReferenceInstant` no perfil | Determinismo |

## 7. Abstrações prematuras (deliberadamente **não** criadas)

| Abstração | Por que não |
|---|---|
| Carregamento dinâmico de plugins | Contratos ainda imaturos (ADR-009) |
| Motor de regras genérico (rule engine) | `core.switch` + MDS-Expr cobrem V1 |
| Parser SQL completo / AST | Lexer basta (ADR-007) |
| Abstração de UI multiplataforma | Windows-only; MVVM já isola |
| Camada de cache distribuído / servidor | Mono-usuário local |
| Repositório genérico `IRepository<T>` | Repositórios específicos e explícitos são mais claros com Dapper |
| Event sourcing de projetos | Undo/redo em memória basta |

## 8. Dependências que impedem antecipação

- Inserção direta (M8) depende de: autorização/guarda (M5), TargetInspector completo, KeyRegistry (Captured).
- Migração de bindings (M7) depende de: hash/diff (M1) e fingerprints de coluna (M2).
- L4 (M8) depende de: providers com savepoints e ErrorTranslator completos.
- Packs (M9) dependem de: staging (M2) — reutilizado.
- CLI (V2) depende de: Application estável e formatos versionados (V1).
