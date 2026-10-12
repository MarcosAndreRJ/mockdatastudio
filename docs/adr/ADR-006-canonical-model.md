# ADR-006 — Modelo canônico + digest como projeção

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Quatro SGBDs com catálogos distintos. Precisamos de: (a) modelo rico em memória para mapeamento/geração, (b) JSON compacto exportável, (c) snapshots comparáveis.

## Alternativas
1. Trabalhar diretamente com o JSON do digest em memória.
2. **Modelo canônico** (records imutáveis) + `DigestSerializer` que projeta/reconstrói.
3. Um modelo por SGBD.

## Decisão
Opção 2. O digest é uma serialização compacta e **sem perdas em relação ao modelo canônico** (round-trip garantido por T-DIG-002). Informações específicas de SGBD ficam em `ColumnModel.Extras`/`TableModel.Extras` com chaves registradas ([12 §4.6](../12-schema-digest.md)).

## Justificativa
Desacopla formato de transporte do modelo de trabalho; permite evoluir o formato (1.0 → 1.1) sem refatorar o motor.

## Consequências positivas
Gerador/mapeador independentes do SGBD; hash e diff sobre modelo normalizado.

## Consequências negativas
Duas representações a manter sincronizadas (mitigado por testes de round-trip).

## Riscos
Perda de nuance específica (ex.: domínios Firebird). Mitigação: campo `domain` nos extras da coluna.

## Condições para revisão
Se o digest precisar carregar estatísticas (V2 profiling) → seção opcional `stats` versionada.
