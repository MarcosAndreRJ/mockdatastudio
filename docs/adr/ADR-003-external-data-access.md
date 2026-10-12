# ADR-003 — ADO.NET puro para bancos externos

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Os bancos externos têm schemas desconhecidos em tempo de compilação. Operações: introspecção (consultas a catálogos de sistema), execução de SQL arbitrário do editor, inserção em massa.

## Alternativas
1. **ADO.NET puro** (`DbConnection`, `DbCommand`, `DbDataReader`).
2. Dapper para tudo.
3. EF Core com modelos dinâmicos.
4. linq2db.

## Decisão
ADO.NET puro em todos os providers externos. Dapper permitido apenas no catálogo interno (ADR-004).

## Justificativa
- Leitura de resultados de schema arbitrário exige `DbDataReader` de qualquer forma.
- Bulk insert usa APIs específicas (COPY do Npgsql) que fogem de ORMs.
- Menos camadas = mensagens de erro do driver preservadas.

## Consequências positivas
Controle total de comandos, timeouts, cancelamento e tipos.

## Consequências negativas
Mais código de mapeamento manual nos extractors (aceitável, isolado por provider).

## Riscos
Diferenças de tipos .NET retornados por driver (ex.: `UInt64` no MySQL). Mitigação: conversores por provider e testes de contrato (T-PRV-*).

## Condições para revisão
Não previstas.
