# ADR-014 — Linguagem de expressão própria (MDS-Expr)

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Necessário: colunas derivadas (`email` a partir de `nome`), strings formatadas, condições simples, funções aleatórias determinísticas ligadas ao RNG da linha.

## Alternativas
1. NCalc (MIT) [K].
2. DynamicExpresso (MIT) [K] — expressões C#.
3. Roslyn Scripting — poder total, superfície de segurança enorme, startup lento.
4. **MDS-Expr**: gramática pequena (EBNF em [15 §9](../15-generation-engine.md)), interpretador por árvore, funções registradas.

## Decisão
Opção 4.

## Justificativa
Integração nativa com o RNG por contador e o `RowContext`; mensagens de erro em português com posição; sem acesso a reflexão/IO por construção; determinismo garantido; implementação pequena.

## Consequências positivas
Controle total da semântica e do determinismo.

## Consequências negativas
Usuários não podem usar C# arbitrário (intencional).

## Riscos
Pressão por novas funções. Mitigação: catálogo de funções versionado, extensível via `IExprFunction`.

## Condições para revisão
Se a gramática precisar de loops/variáveis → reavaliar.
