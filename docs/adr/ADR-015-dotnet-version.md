# ADR-015 — .NET 10 LTS

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Escolha do runtime em outubro de 2026.

## Alternativas
.NET 8 (fim em 2026-11-10 [V]), .NET 9 (STS, fim em 2026-11-10 [V]), **.NET 10 (LTS, fim em 2028-11-14 [V])**, .NET 11 (STS esperado para nov/2026 [K]).

## Decisão
.NET 10, `net10.0-windows`, C# 14, `Nullable=enable`, `TreatWarningsAsErrors=true`.

## Justificativa
Único LTS com suporte vigente durante todo o desenvolvimento do V1.

## Consequências positivas
Suporte longo.

## Consequências negativas
Planejar migração para o próximo LTS (.NET 12, previsto para nov/2027 [K]) antes de 2028-11.

## Riscos
Pacotes de terceiros sem alvo net10 (AvalonEdit visa net6.0-windows; compatível por TFM [K]).

## Condições para revisão
Novembro de 2027.
