# ADR-016 — Importação de scripts do catálogo via staging isolado

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Requisito: o usuário popula templates/aliases/valores via scripts SQL (possivelmente gerados por IA). Executar SQL livre direto no `catalog.db` permite `DROP`, `DELETE` em massa e corrupção de IDs internos e regras.

## Alternativas
1. Executar diretamente no catálogo com confirmação.
2. Executar no catálogo com `sqlite3_set_authorizer` restringindo operações.
3. **Executar em um banco SQLite temporário ("staging") com schema público simplificado (códigos, não IDs), validar e mesclar no catálogo em uma transação.**

## Decisão
Opção 3.

## Justificativa
Isolamento por construção (o script não enxerga o catálogo); scripts portáveis (usam códigos textuais); validação completa antes do merge; preview de diferenças (novos/alterados/ignorados).

## Consequências positivas
Segurança e portabilidade dos scripts.

## Consequências negativas
O script não altera diretamente dados existentes; alterações ocorrem pelo modo de merge `Upsert` ([13 §7](../13-template-catalog.md)).

## Riscos
Script grande → staging em arquivo temporário (não em memória) quando > 50 MB.

## Condições para revisão
Se usuários exigirem exclusão via script → tabela `stg_delete` explícita (V2).
