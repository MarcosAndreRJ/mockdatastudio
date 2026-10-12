# 22 — Formato de Arquivo de Projeto (`.mdsproj`)

| Campo | Valor |
|---|---|
| Objetivo | Especificar o formato portátil, versionado e seguro dos projetos. |
| Escopo | Formato `mds.project` versão **1.0**. |
| Relacionados | ADR-010, [C09](contracts/C09-projects-and-application.md), [schema](contracts/schemas/project-1.0.schema.json), [exemplo](examples/project-loja.mdsproj) |
| Requisitos | FR-110..FR-116 |

## 1. Estrutura

```json
{
  "format": "mds.project",
  "formatVersion": "1.0",
  "id": "GUID",
  "name": "Loja Demo",
  "createdAt": "ISO", "modifiedAt": "ISO",
  "toolVersion": "1.0.0",
  "connections": { "source": ConnectionRef?, "target": ConnectionRef?, "reference": ConnectionRef? },
  "snapshots": [ { "id", "label", "capturedAt", "structureHash", "sourceFingerprint", "digest": { ...digest 1.x... } } ],
  "activeSnapshotId": "snap-0001",
  "bindings": { "<ColumnRef.CanonicalId>": ColumnBinding },
  "projectRules": [ MappingRule (scope project) ],
  "profiles": [ GenerationProfile ],
  "activeProfile": "default",
  "catalogDependencies": [ { "templateCode", "contentHash", "valueCount" } ],
  "runs": [ RunSummary ],
  "notes": "texto livre"
}
```

- O digest é **embutido como objeto JSON** (não string) para legibilidade e diff. (`SnapshotEntry.DigestJson` no contrato é a forma serializada desse objeto.)
- `ConnectionRef` = `{ "profileId"?: GUID, "displayName", "engine", "fingerprint"? }` — **nunca** host, usuário, senha ou connection string.
- `seed` é serializado como **string** decimal (ulong excede 2^53).

## 2. Regras de serialização

1. UTF-8 sem BOM, indentado com 2 espaços, `\n`.
2. Chaves de objetos de mapa (`bindings`, `tables`, `relations`) ordenadas ordinalmente → diffs estáveis no Git.
3. Arrays com ordem semântica preservada (snapshots por data, profiles na ordem do usuário).
4. Escrita atômica: `arquivo.mdsproj.tmp` → `File.Replace` com backup `arquivo.mdsproj.bak` (mantido 1).
5. Valores default omitidos (ex.: `nullRate: 0`, `state: "active"`).

## 3. Portabilidade e segurança

| Item | No arquivo? | Motivo |
|---|---|---|
| Senhas, connection strings, host, usuário | **Nunca** | Segurança |
| `profileId` local | Sim (opcional) | Reabrir com a conexão do usuário; em outra máquina vira "não resolvido" e o usuário associa um perfil local |
| Fingerprints | Sim (hash, não reversível) | Detectar troca de banco |
| Dados gerados | Não | Apenas resumos de execução |
| Valores de templates | Não (somente `contentHash`) | O catálogo é separado; packs viajam como `.mdspack.json` (V2: `.mdspkg` com tudo) |
| Valores `Fixed` de relacionamentos | Sim | Fazem parte da configuração |
| Caminho de `Dataset` | Relativo ao projeto quando possível | Portabilidade |
| Queries de referência (`Query`) | Sim | Configuração; validadas como SELECT ao abrir |

**Exportação sanitizada** (`ExportSanitizedAsync`): remove `connections.*.profileId` e fingerprints, histórico de execuções e (opcional) snapshots antigos — para compartilhar publicamente.

**Abertura em outra máquina**: perfis não resolvidos são listados; `catalogDependencies` verificadas: template ausente → bindings `NeedsReview` + mensagem "Importe o pack X"; `contentHash` diferente → aviso de reprodutibilidade (não bloqueia).

## 4. Versionamento

- `formatVersion` `MAJOR.MINOR` com as mesmas regras do digest ([12 §5](12-schema-digest.md)).
- `ProjectMigrator`: cadeia de migrações `1.0→1.1→…` aplicada em memória ao abrir; salvar grava na versão atual (aviso "o arquivo será salvo no formato 1.x e não abrirá em versões anteriores do app" quando MINOR sobe e o arquivo tinha versão menor). Backup `arquivo.mdsproj.v1.0.bak` antes da primeira gravação migrada.
- Versão MAJOR maior que a suportada → somente leitura não é possível → erro `PRJ-MAJOR-UNSUPPORTED`.

## 5. Validação ao abrir

| Código | Regra | Severidade |
|---|---|---|
| PRJ-JSON | JSON válido | Error |
| PRJ-SCHEMA | Conformidade com `project-1.0.schema.json` (mensagem com JSON Path) | Error |
| PRJ-DIGEST | Cada snapshot passa no `DigestValidator` | Error |
| PRJ-BINDING-COLUMN | Binding referencia coluna inexistente no snapshot ativo → marcado `Orphaned` | Warning |
| PRJ-GENERATOR | `generatorId`/versão desconhecidos → `NeedsReview` | Warning |
| PRJ-PARAMS | Parâmetros inválidos para o gerador → `NeedsReview` | Warning |
| PRJ-QUERY | Query de referência não é SELECT único | Error (relação desativada) |
| PRJ-CATALOG | Dependência de catálogo ausente/alterada | Warning |

Arquivo com Errors estruturais não é aberto; o app oferece abrir o `.bak`.

## 6. Validação por JSON Schema
O schema publicado ([project-1.0.schema.json](contracts/schemas/project-1.0.schema.json)) é contrato para ferramentas externas e testes. Em runtime, `ProjectValidator` implementa as mesmas regras em código (ver nota em [07 §2](07-solution-structure.md)); T-PRJ-010 garante equivalência com fixtures válidas e inválidas.

## 7. Autosave e recuperação
- Autosave opcional (padrão ligado) a cada 60 s **em arquivo de recuperação** (`%LOCALAPPDATA%\MockDataStudio\recovery\<projectId>.mdsproj`), nunca sobrescrevendo o arquivo do usuário sem `Ctrl+S`.
- Ao abrir, se existir recuperação mais nova que o arquivo → oferecer "Recuperar alterações não salvas".
