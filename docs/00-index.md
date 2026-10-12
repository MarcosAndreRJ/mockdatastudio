# Mock Data Studio — Documentação de Engenharia

| Campo | Valor |
|---|---|
| Objetivo | Índice navegável da especificação completa do Mock Data Studio (MDS). |
| Escopo | Visão, requisitos, arquitetura, especificação técnica, UX, plano de implementação, qualidade e governança. |
| Revisão | 1.0 — 2026-10-11 |
| Público | Responsável pelo produto e IA Executora (implementação) |
| Status | Especificação concluída. Implementação em andamento: Sprints 0–6 executados (ver [37](37-implementation-audit.md)); restante em trilhas paralelas |

> **IA Executora**: comece por [33-executor-instructions](33-executor-instructions.md) (§18: execução paralela em branches) e execute as sprints da sua trilha no [Plano de Ação](../PLANO-DE-ACAO.md). O conteúdo de cada tarefa está no [28-implementation-backlog](28-implementation-backlog.md).

## Resumo em uma página

- **O que é**: ferramenta desktop Windows (WPF/.NET 10), offline, que lê a estrutura de bancos SQLite/MySQL/PostgreSQL/Firebird, produz um *Schema Digest* compacto, mapeia colunas para templates/geradores de forma determinística e explicável, e gera dados sintéticos coerentes e reproduzíveis como script SQL ou inserção direta.
- **Decisões centrais**: monólito modular; modelo canônico com digest como projeção; ADO.NET; catálogo SQLite com importação por *staging* isolado; motor próprio com RNG por contador; chaves explícitas pré-atribuídas; planejamento relacional com SCC e quebra de ciclos; autorização de escrita vinculada ao fingerprint do servidor.
- **Fases**: MVP técnico (SQLite+PostgreSQL+Firebird, motor completo, script SQL) → V1 profissional (MySQL, editor SQL, inserção direta, evolução de schema, compostos, segurança completa) → V2 (CLI, plugins, designer de cenários, estatística, etc.).

## 1. Produto e requisitos

| Doc | Conteúdo |
|---|---|
| [01-product-vision](01-product-vision.md) | Visão, personas, diferenciação, **diagnóstico da proposta original**, decisões principais |
| [02-requirements](02-requirements.md) | Requisitos funcionais FR-001…FR-122 (+ V2 FR-200…214) com regras, fluxos, erros, aceite e testes |
| [03-functional-specification](03-functional-specification.md) | Casos de uso UC-001…UC-013 |
| [04-non-functional-requirements](04-non-functional-requirements.md) | NFRs mensuráveis |

## 2. Arquitetura e decisões

| Doc | Conteúdo |
|---|---|
| [05-system-architecture](05-system-architecture.md) | Componentes, dependências permitidas/proibidas, fluxos, ciclo de vida, concorrência, cancelamento, transações, resiliência |
| [06-technology-decisions](06-technology-decisions.md) | Pesquisa de mercado, comparações, pilha, matriz de versões, referências verificadas |
| [07-solution-structure](07-solution-structure.md) | Repositório, projetos, build, convenções |
| [08-domain-model](08-domain-model.md) | Entidades, invariantes, nomenclatura obrigatória |
| [adr/](adr/README.md) | ADR-001…ADR-016 |
| [contracts/](contracts/README.md) | Interfaces C# (C01–C09) e JSON Schemas (digest, projeto, pack) |
| [diagrams/](diagrams/README.md) | Diagramas consolidados |

## 3. Especificação técnica

| Doc | Conteúdo |
|---|---|
| [09-internal-database](09-internal-database.md) | Catálogo SQLite: DDL, staging, seeds, migrações, backup |
| [10-database-providers](10-database-providers.md) | Providers e particularidades de cada SGBD |
| [11-schema-introspection](11-schema-introspection.md) | Extração de metadados por SGBD |
| [12-schema-digest](12-schema-digest.md) | Formato do digest, hash, validação, diff, evolução |
| [13-template-catalog](13-template-catalog.md) | Templates escalares/compostos, aliases, presets, importação por script, packs |
| [14-semantic-mapping](14-semantic-mapping.md) | Algoritmo determinístico de mapeamento e confiança |
| [15-generation-engine](15-generation-engine.md) | Motor, determinismo, geradores, unicidade, MDS-Expr |
| [16-relational-generation](16-relational-generation.md) | Dependências, ciclos, chaves, cardinalidade, fontes de referência |
| [17-constraint-validation](17-constraint-validation.md) | Validação L1–L4 |
| [18-sql-generation](18-sql-generation.md) | Scripts SQL por dialeto, segmentação, limpeza, CSV/JSON |
| [19-direct-database-execution](19-direct-database-execution.md) | Inserção direta com autorização |
| [20-sql-editor](20-sql-editor.md) | Editor SQL, lexer/splitter, autocomplete, proteções |
| [21-user-interface-ux](21-user-interface-ux.md) | Design system e telas S01–S19 |
| [22-project-file-format](22-project-file-format.md) | Formato `.mdsproj` |
| [23-security](23-security.md) | Ameaças e controles |
| [24-performance-scalability](24-performance-scalability.md) | Streaming, metas de desempenho |
| [25-error-handling-observability](25-error-handling-observability.md) | Erros, códigos, logs |

## 4. Qualidade, plano e governança

| Doc | Conteúdo |
|---|---|
| [26-testing-strategy](26-testing-strategy.md) | Estratégia e catálogo de testes, matriz de versões, CI |
| [27-development-roadmap](27-development-roadmap.md) | MVP/V1/V2, avaliação das funcionalidades premium, contratos antecipados |
| [28-implementation-backlog](28-implementation-backlog.md) | Épicos e tarefas ordenadas por dependência |
| [29-acceptance-criteria](29-acceptance-criteria.md) | Definition of Done, gates de marco, cenários Gherkin |
| [30-risks-and-tradeoffs](30-risks-and-tradeoffs.md) | Problemas profundos A–J e registro de riscos |
| [31-extension-strategy](31-extension-strategy.md) | Pontos de extensão, plugins, CLI |
| [32-decisions-and-assumptions](32-decisions-and-assumptions.md) | Decisões autônomas, premissas, decisões de produto, verificações pendentes |
| [33-executor-instructions](33-executor-instructions.md) | Regras para a IA Executora |
| [34-glossary](34-glossary.md) | Glossário |
| [35-traceability-matrix](35-traceability-matrix.md) | Requisito → Módulo → Contrato → Tarefa → Teste → Aceite |
| [36-audit-report](36-audit-report.md) | Auditoria independente da especificação, correções e riscos residuais |
| [37-implementation-audit](37-implementation-audit.md) | Auditoria da implementação (2026-10-12): o que foi feito, achados e o que falta |
| [progress/](progress/plano-sequencial-sprints-0-6.md) | Plano sequencial arquivado (Sprints 0–6) e evidências por sprint |

## 5. Exemplos

| Arquivo | Descrição |
|---|---|
| [examples/digest-sqlite.json](examples/digest-sqlite.json) · [mysql](examples/digest-mysql.json) · [postgresql](examples/digest-postgresql.json) · [firebird](examples/digest-firebird.json) | Mesmo schema "loja" nos 4 SGBDs (válidos pelo schema) |
| [examples/project-loja.mdsproj](examples/project-loja.mdsproj) | Projeto completo com bindings e perfil (válido pelo schema) |
| [examples/catalog-import-example.sql](examples/catalog-import-example.sql) | Script de importação para o staging do catálogo (executado em SQLite) |
| [examples/pack-exemplo.mdspack.json](examples/pack-exemplo.mdspack.json) | Pack de templates (válido pelo schema) |
| [examples/schema-loja-sqlite.sql](examples/schema-loja-sqlite.sql) | DDL de referência SQLite |
| [examples/output-sqlite.sql](examples/output-sqlite.sql) | Script gerado (executado em SQLite com FKs verificadas) |
| [examples/output-postgresql.sql](examples/output-postgresql.sql) · [output-firebird.sql](examples/output-firebird.sql) | Scripts gerados ilustrativos (não executados) |

## 6. Pendências que exigem o responsável pelo produto

DP-001 a DP-009 em [32 §3](32-decisions-and-assumptions.md) — nenhuma bloqueia o início da implementação (há padrões provisórios).
