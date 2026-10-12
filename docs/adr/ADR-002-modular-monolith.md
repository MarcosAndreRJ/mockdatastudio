# ADR-002 — Monólito modular

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Aplicação local, um usuário, offline. Necessidade de testabilidade, futura CLI (V2) reutilizando o mesmo núcleo e possíveis plugins.

## Alternativas
1. Monólito em um único projeto.
2. **Monólito modular** (vários assemblies com dependências controladas, um processo).
3. Cliente/serviço local (UI + serviço em background via gRPC/named pipes).
4. Microsserviços — descartado (sem justificativa para app desktop).

## Decisão
Monólito modular: assemblies por responsabilidade ([07](../07-solution-structure.md)), um processo, DI com `Microsoft.Extensions.Hosting`.

## Justificativa
- Reuso direto do núcleo pela CLI (V2) sem processo servidor.
- Regras de dependência verificáveis por testes de arquitetura.
- A opção 3 adicionaria serialização, ciclo de vida de serviço e complexidade de depuração sem benefício (não há múltiplos clientes).

## Consequências positivas
Núcleo testável sem UI; fronteiras claras; build único.

## Consequências negativas
Uma falha nativa de driver pode derrubar o processo (sem isolamento). Aceito.

## Riscos
Erosão das fronteiras. Mitigação: testes de arquitetura obrigatórios no CI (T-ARC-001..005).

## Condições para revisão
Se surgir modo servidor/colaborativo, extrair `Application` como serviço.
