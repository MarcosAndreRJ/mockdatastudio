# ADR-007 — Execução SQL via lexer por dialeto

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
O editor executa scripts com múltiplos comandos. Separar por `;` falha com strings, comentários, `$$` (PostgreSQL), blocos `BEGIN...END` (Firebird `EXECUTE BLOCK`, procedures) e diretivas de cliente (`SET TERM`, `DELIMITER`).

## Alternativas
1. Split por regex/`;`.
2. **Lexer por dialeto** (tokens: string, identificador citado, comentário, dollar-quote, terminador) + diretivas de cliente + rastreamento de profundidade de blocos.
3. Parser completo (gramáticas ANTLR para cada dialeto).

## Decisão
Opção 2 (`MockDataStudio.SqlTooling`). A classificação do comando (SELECT/DML/DDL/outros) usa a primeira palavra-chave significativa após comentários e análise de `WITH`.

## Justificativa
Parser completo é caro, frágil frente a versões e desnecessário para dividir/classificar. O lexer cobre os casos que quebram o split ingênuo.

## Consequências positivas
Rápido, previsível, testável com corpus.

## Consequências negativas
Classificação de `WITH ... DELETE` (CTE com DML no PostgreSQL) exige regra extra (documentada em [20 §5](../20-sql-editor.md)).

## Riscos
Corpos de procedures PSQL sem `SET TERM`. Mitigação: rastreamento `BEGIN/END` com profundidade quando o comando inicia com `CREATE [OR ALTER] PROCEDURE|TRIGGER|FUNCTION`, `RECREATE ...`, `EXECUTE BLOCK`.

## Condições para revisão
Se o autocomplete exigir análise semântica profunda (V2), avaliar parser parcial.
