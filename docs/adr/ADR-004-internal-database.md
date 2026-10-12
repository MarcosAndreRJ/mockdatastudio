# ADR-004 — Banco interno: SQLite + Dapper + migrações SQL

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
O catálogo (templates, aliases, valores, regras, histórico, perfis de conexão) precisa ser local, transacional, com backup simples. O usuário escreve scripts SQL de carga contra um **schema público estável** (staging, ADR-016).

## Alternativas
1. **SQLite + Dapper + migrações SQL numeradas** embutidas como recursos.
2. SQLite + EF Core (migrations geradas).
3. LiteDB (documento).
4. Arquivos JSON.

## Decisão
Opção 1. Arquivo `%LOCALAPPDATA%\MockDataStudio\catalog.db`, `journal_mode=WAL`, `foreign_keys=ON`, migrações `V0001__init.sql`, `V0002__...sql` aplicadas por `CatalogMigrator` em transação, registradas em `schema_version`.

## Justificativa
- O schema é contrato público documentado ([09](../09-internal-database.md)) → escrito à mão, não derivado de classes.
- SQLite permite que o usuário use SQL (requisito original) — LiteDB/JSON não.
- Dapper reduz boilerplate sem esconder SQL.

## Consequências positivas
Schema explícito, migrações revisáveis, backup via `VACUUM INTO`.

## Consequências negativas
Sem change tracking; repositórios escritos manualmente.

## Riscos
Migração com defeito corrompe o catálogo. Mitigação: backup automático antes de migrar; teste de migração a partir de cada versão anterior (T-CAT-MIG-*).

## Condições para revisão
Se o catálogo precisar ser compartilhado em rede por equipes → avaliar servidor (pós-V2).
