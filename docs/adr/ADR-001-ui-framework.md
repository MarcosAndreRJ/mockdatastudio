# ADR-001 — Framework de UI: WPF

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Aplicação desktop exclusiva para Windows, com editor SQL (highlight, autocomplete), grades grandes virtualizadas, árvores de objetos e painéis redimensionáveis. A IA Executora precisa de um framework com ampla documentação.

## Alternativas
1. **WPF** — maduro, MVVM nativo, AvalonEdit, DataGrid virtualizado.
2. **WinUI 3 (Windows App SDK)** — visual moderno; sem DataGrid oficial, ecossistema de editor de código fraco, empacotamento mais complexo.
3. **Avalonia 11** — multiplataforma, AvaloniaEdit, XAML parecido com WPF; ecossistema menor para Windows-only.
4. **WinForms** — maduro, mas binding/MVVM pobre e alta densidade de código de UI.

## Decisão
WPF sobre .NET 10 com CommunityToolkit.Mvvm.

## Justificativa
Matriz ponderada em [06 §3.2](../06-technology-decisions.md) (WPF 63, Avalonia 57, WinForms 52, WinUI 40). O componente crítico (editor SQL) tem solução madura e MIT (AvalonEdit).

## Consequências positivas
- Componentes maduros; virtualização de UI conhecida; FlaUI para testes de UI.

## Consequências negativas
- Visual padrão datado → exige design system próprio ([21](../21-user-interface-ux.md)).
- Preso ao Windows (aceito pelos requisitos).

## Riscos
- AvalonEdit upstream com baixa atividade (último release verificado: 2021). Mitigação: encapsular em `SqlEditorControl`; fork AvalonEditB como plano B.

## Condições para revisão
Se macOS/Linux virar requisito → migrar `MockDataStudio.App` para Avalonia; os demais assemblies não referenciam WPF (regra AR-03 em [05](../05-system-architecture.md)).
