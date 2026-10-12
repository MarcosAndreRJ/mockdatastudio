# ADR-009 — Extensibilidade: interna no V1, plugins no V2

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Requisito de providers e geradores extensíveis. Plugins de terceiros executam código arbitrário no processo.

## Alternativas
1. Plugins desde o MVP via `AssemblyLoadContext`.
2. **Pontos de extensão internos** (interfaces + registro por DI) no V1; carregamento externo no V2.
3. Plugins fora de processo.
4. Scripts (Roslyn/Lua) como plugins.

## Decisão
Opção 2. Interfaces estáveis (`IDatabaseProvider`, `IGeneratorFactory`, `IOutputWriterFactory`, `ITemplatePackSource`) desde o MVP; registro via `IMdsModule`. No V2, `PluginLoader` carrega `plugins/<nome>/<nome>.dll` com manifesto `plugin.json` e `ContractVersion` ([31](../31-extension-strategy.md)).

## Justificativa
Evita congelar contratos imaturos; prepara o terreno sem antecipar custos de segurança.

## Consequências positivas
Contratos testados internamente antes de serem abertos.

## Consequências negativas
Terceiros só estendem a partir do V2.

## Riscos
Plugins não são sandbox (.NET não oferece sandbox in-process). Mitigação V2: aviso de confiança, lista de permitidos, assinatura opcional.

## Condições para revisão
Na abertura do V2.
