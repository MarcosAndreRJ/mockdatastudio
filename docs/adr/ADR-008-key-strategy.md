# ADR-008 — Estratégia de chaves: explícitas pré-atribuídas como padrão

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Filhos precisam conhecer as chaves dos pais gerados. Chaves geradas pelo banco (identity, auto_increment, serial, generators) só são conhecidas após o INSERT; em scripts SQL offline não há forma genérica de capturá-las. No MySQL com `innodb_autoinc_lock_mode=2` (padrão desde 8.0 [V]), `LAST_INSERT_ID()+n` não é premissa segura para inserções multi-linha.

## Alternativas
1. **Explicit (pré-atribuídas)**: o motor reserva uma faixa `[start, start+n)` acima do máximo atual e insere valores explícitos; depois ressincroniza a sequence.
2. **Captured**: inserção direta linha a linha com `RETURNING` / `LAST_INSERT_ID()`; chaves guardadas em `KeyRegistry`.
3. **Lookup por chave natural**: filhos usam `(SELECT id FROM pai WHERE uk = ...)` em script.
4. Variáveis de sessão por dialeto no script.

## Decisão
- Padrão: **Explicit** para chaves inteiras e UUID.
- **Captured**: somente em inserção direta, para tabelas em que Explicit é impossível (ver [16 §5](../16-relational-generation.md)).
- **NaturalKeyLookup**: em script quando o pai possui UNIQUE gerado pelo motor (V1, opção avançada).
- Variáveis de sessão: rejeitado (não portável, scripts ilegíveis).

## Justificativa
Explicit é determinístico, funciona em script e inserção direta, permite memória O(1) (chave = f(índice)) e habilita script de limpeza por faixa.

## Consequências positivas
Reprodutibilidade; desempenho (bulk sem round-trip); limpeza trivial.

## Consequências negativas
Requer leitura de `MAX(pk)` no destino durante o planejamento e ressincronização de sequence; risco de concorrência com outros usuários inserindo no mesmo intervalo (mitigado por *offset* configurável e reverificação imediatamente antes da execução).

## Riscos
- PostgreSQL `GENERATED ALWAYS` exige `OVERRIDING SYSTEM VALUE` [K].
- Firebird 4+ aceita `OVERRIDING SYSTEM VALUE` para `GENERATED ALWAYS` [K — verificar em T-PRV-FB-KEY-002]; Firebird 3 só tem identity `BY DEFAULT` [K].
- Triggers `BEFORE INSERT` legadas do Firebird que **sempre** atribuem `NEW.ID = GEN_ID(...)` invalidam a estratégia → detectadas pela inserção de teste ([17 §6](../17-constraint-validation.md)) e convertidas para Captured.

## Condições para revisão
Se Captured for necessário com frequência em MySQL, avaliar inserção multi-linha + releitura por chave natural.
