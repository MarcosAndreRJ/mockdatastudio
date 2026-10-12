# ADR-012 — MySqlConnector em vez de MySql.Data

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Escolha do driver ADO.NET para MySQL (e MariaDB em melhor esforço).

## Alternativas
1. **MySqlConnector** — MIT, async real, suporta MariaDB [V].
2. MySql.Data (Oracle Connector/NET) — GPLv2 + Universal FOSS Exception [V]; licença comercial para software proprietário.
3. Devart dotConnect — comercial.

## Decisão
MySqlConnector.

## Justificativa
Licença compatível com qualquer modelo de distribuição; melhor suporte a MariaDB; async verdadeiro.

## Consequências positivas
Sem risco de licença.

## Consequências negativas
Algumas opções de connection string diferem de MySql.Data (documentadas em [10 §4](../10-database-providers.md)).

## Riscos
Nenhum relevante identificado.

## Condições para revisão
Não previstas.
