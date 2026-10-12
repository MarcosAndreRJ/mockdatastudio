# ADRs — Architecture Decision Records

Formato: Contexto · Alternativas · Decisão · Justificativa · Consequências (+/−) · Riscos · Condições para revisão.
Status possíveis: `Proposto`, `Aceito`, `Substituído por ADR-xxx`, `Obsoleto`.
Novos ADRs seguem a numeração sequencial; a IA Executora cria ADR conforme [33 §8](../33-executor-instructions.md).

| ADR | Título | Status |
|---|---|---|
| [ADR-001](ADR-001-ui-framework.md) | WPF vs WinUI 3 vs Avalonia | Aceito |
| [ADR-002](ADR-002-modular-monolith.md) | Monólito modular | Aceito |
| [ADR-003](ADR-003-external-data-access.md) | ADO.NET puro para bancos externos | Aceito |
| [ADR-004](ADR-004-internal-database.md) | SQLite + Dapper + migrações SQL para o banco interno | Aceito |
| [ADR-005](ADR-005-generation-engine.md) | Motor de geração próprio (Bogus fora do núcleo) | Aceito |
| [ADR-006](ADR-006-canonical-model.md) | Modelo canônico + digest como projeção | Aceito |
| [ADR-007](ADR-007-sql-execution.md) | Execução SQL via lexer por dialeto | Aceito |
| [ADR-008](ADR-008-key-strategy.md) | Chaves explícitas pré-atribuídas como padrão | Aceito |
| [ADR-009](ADR-009-plugin-strategy.md) | Extensibilidade interna no V1, plugins no V2 | Aceito |
| [ADR-010](ADR-010-project-persistence.md) | Projeto como arquivo JSON versionado | Aceito |
| [ADR-011](ADR-011-credential-storage.md) | DPAPI para credenciais | Aceito |
| [ADR-012](ADR-012-mysql-driver.md) | MySqlConnector em vez de MySql.Data | Aceito |
| [ADR-013](ADR-013-deterministic-rng.md) | RNG baseado em contador | Aceito |
| [ADR-014](ADR-014-expression-language.md) | Linguagem de expressão própria (MDS-Expr) | Aceito |
| [ADR-015](ADR-015-dotnet-version.md) | .NET 10 LTS | Aceito |
| [ADR-016](ADR-016-catalog-import-staging.md) | Importação de scripts do catálogo via staging isolado | Aceito |
