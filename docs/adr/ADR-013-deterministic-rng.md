# ADR-013 — RNG baseado em contador

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Reprodutibilidade exige que o valor de cada célula seja função de (seed, objeto, linha) — independente de ordem de execução, paralelismo e de quantas outras colunas existem.

## Alternativas
1. `System.Random(seed)` global sequencial.
2. Um `Random` sequencial por coluna.
3. **Counter-based**: `state = Mix(StreamKey(seed, tableId, columnId, purpose), rowIndex)` → gerador xoshiro256** inicializado por SplitMix64 para a linha.

## Decisão
Opção 3, implementação própria (`DeterministicRandom`), com chaves de fluxo derivadas por `XxHash64` (System.IO.Hashing) sobre bytes UTF-8 canônicos. Especificação exata em [15 §4](../15-generation-engine.md).

## Justificativa
- Paralelismo trivial (qualquer linha é calculável isoladamente).
- Adicionar uma coluna não altera os valores das outras.
- A chave do pai `i` é recomputável sem armazenamento (KeyOracle).
- Independe da implementação de `System.Random` entre versões do .NET.

## Consequências positivas
Determinismo forte.

## Consequências negativas
Custo de inicialização por célula (dezenas de ns; aceitável — benchmark B-RNG-001).

## Riscos
Um bug no mixer altera todas as saídas → algoritmo congelado com vetores de teste fixos (T-GEN-RNG-001). Mudança exige novo `RngAlgorithmVersion`.

## Condições para revisão
O algoritmo v1 nunca é alterado; novas versões coexistem.
