# ADR-010 — Projeto como arquivo JSON versionado (`.mdsproj`)

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Projetos precisam ser portáteis, versionáveis em Git, compartilháveis sem segredos e evoluir com o formato.

## Alternativas
1. Projeto armazenado no catálogo interno.
2. **Arquivo JSON UTF-8** (`.mdsproj`), digest embutido.
3. Arquivo SQLite por projeto.
4. Pacote ZIP (JSON + anexos).

## Decisão
Opção 2, com digests embutidos e campo `formatVersion`. Exportação compartilhável "sanitizada" (sem referências locais). Pacote ZIP (`.mdspkg`) incluindo template packs — V2.

## Justificativa
Diff legível em Git, migração por versão, inspeção humana, validação por JSON Schema.

## Consequências positivas
Portabilidade, revisão em pull requests.

## Consequências negativas
Arquivo cresce com muitos snapshots (mitigação: no máximo 10 snapshots retidos; antigos removíveis).

## Riscos
Edição manual inválida → validação por JSON Schema com mensagens contendo o caminho JSON.

## Condições para revisão
Se projetos excederem 20 MB com frequência → opção de digest externo referenciado.
