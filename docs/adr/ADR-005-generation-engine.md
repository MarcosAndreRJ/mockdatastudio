# ADR-005 — Motor de geração próprio

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Necessário: geração por coluna guiada por templates do catálogo, relacional, determinística entre versões, paralelizável, com unicidade construtiva.

## Alternativas
1. Bogus como núcleo + adaptadores.
2. AutoBogus/AutoFixture.
3. **Motor próprio** com geradores plugáveis; Bogus opcional como pacote V2.

## Decisão
Opção 3.

## Justificativa
- Bogus usa RNG sequencial e datasets internos que mudam entre versões → quebra reprodutibilidade de longo prazo.
- Os dados do MDS vêm principalmente do catálogo do usuário, não de datasets de biblioteca.
- Geração relacional, unicidade bijetiva e RNG por contador não existem em bibliotecas faker.

## Consequências positivas
Determinismo forte; controle de memória; versionamento por gerador.

## Consequências negativas
Mais código próprio; datasets pt-BR precisam ser criados (pacote autoral, sem copiar datasets de licença incerta).

## Riscos
Escopo do motor crescer demais. Mitigação: lista fechada de geradores V1 ([15 §6](../15-generation-engine.md)).

## Condições para revisão
Se usuários pedirem amplamente locales não cobertos → pacote "Bogus Bridge" (V2) com `Reproducibility = VersionBound`.
