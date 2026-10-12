# 25 — Tratamento de Erros e Observabilidade

| Campo | Valor |
|---|---|
| Objetivo | Padronizar classificação de erros, códigos, mensagens, logs e diagnósticos. |
| Escopo | Todo o produto. |
| Relacionados | [05 §9](05-system-architecture.md), [23 §6](23-security.md), [C01](contracts/C01-common-and-schema.md) |

## 1. Classificação

| Classe | Representação | Exemplo | Tratamento |
|---|---|---|---|
| Erro esperado de domínio | `Result<T>.Fail(MdsError)` | Parâmetro de gerador inválido | Mensagem ao usuário, sem log de erro (log `Information`) |
| Diagnóstico | `PlanDiagnostic` / `ValidationIssue` / `ImportIssue` | Ciclo não quebrável | Lista na UI |
| Erro externo esperado | Exceção do driver → `IErrorTranslator` → `MdsError` | Violação de unicidade | Mensagem + código SGBD; log `Warning` |
| Cancelamento | `OperationCanceledException` | Usuário cancelou | Não é erro; log `Information` |
| Defeito | Exceção inesperada | `NullReferenceException` | Handler global: log `Error` com correlação, diálogo "Erro inesperado" com ID, opção de copiar detalhes |

## 2. Estrutura de `MdsError`
`Code` (estável, documentado), `Message` (pt-BR para o usuário: o que/por quê/o que fazer), `Details` (mapa: `table`, `column`, `row`, `dbCode`, `position`, `path`). Mensagens em `Strings.resx` com chave = `Code`.

## 3. Registro de códigos (prefixos e principais)

| Prefixo | Domínio | Códigos principais (não exaustivo; lista completa mantida em `src/MockDataStudio.Core/Common/ErrorCodes.cs` e **sincronizada** com este documento) |
|---|---|---|
| `PRV-` | Providers | `PRV-CONN-FAILED`, `PRV-CONN-LOST`, `PRV-TIMEOUT`, `PRV-INTROSPECT-PARTIAL`, `PRV-SQLITE-NOFILE`, `PRV-UNSUPPORTED-VERSION` |
| `DIG-` | Digest | `DIG-JSON`, `DIG-FORMAT`, `DIG-VERSION`, `DIG-MAJOR-UNSUPPORTED`, `DIG-MINOR-NEWER`, `DIG-SCHEMA`, `DIG-DUP`, `DIG-PK-COL`, `DIG-FK-TARGET`, `DIG-FK-EXTERNAL`, `DIG-FK-ARITY`, `DIG-FLAGS`, `DIG-HASH-MISMATCH` |
| `CAT-` | Catálogo | `CAT-BUILTIN-READONLY`, `CAT-MIG-CHECKSUM`, `CAT-NEWER-SCHEMA`, `CAT-STG-*`, `CAT-IMP-*` (13 §7.2) |
| `MAP-` | Mapeamento | `MAP-UNMAPPED`, `MAP-AMBIGUOUS`, `MAP-LOWCONF`, `MAP-TYPE`, `MAP-ORPHAN`, `MAP-REVIEW` |
| `GEN-` | Geração | `GEN-PARAM-REQUIRED`, `GEN-PARAM-RANGE`, `GEN-PARAM-TYPE`, `GEN-COERCE`, `GEN-UNIQUE-EXHAUSTED`, `GEN-EXPR-SYNTAX`, `GEN-EXPR-TYPE`, `GEN-EXPR-UNKNOWN-FN`, `GEN-UQ-UNIFORM` |
| `PLN-` | Planejamento | [16 §10](16-relational-generation.md) |
| `VAL-` | Validação | [17 §2](17-constraint-validation.md) |
| `OUT-` | Saída | `OUT-IO`, `OUT-SEGMENT-OVERSIZE`, `OUT-DISK-FULL` |
| `RUN-` | Execução | `RUN-KEY-RANGE-TAKEN`, `RUN-BATCH-FAILED`, `RUN-INTERRUPTED` |
| `SQL-` | Editor | `SQL-SPLIT`, `SQL-BLOCKED-READONLY`, `SQL-CANCELLED`, `SQL-DISCONNECTED` |
| `PRJ-` | Projeto | [22 §5](22-project-file-format.md) |
| `SEC-` | Segurança | `SEC-NO-AUTHORIZATION`, `SEC-AUTH-EXPIRED`, `SEC-FINGERPRINT-MISMATCH`, `SEC-TRIAL-PROD`, `SEC-UNTRUSTED-PROJECT` |

Teste T-ERR-001: todo código usado em `MdsError`/diagnóstico existe em `ErrorCodes` e tem mensagem em `Strings.resx`.

## 4. Logs
- Serilog, arquivo rolling diário em `%LOCALAPPDATA%\MockDataStudio\logs\mds-<data>.log`, retenção 14 dias, 20 MB/arquivo; formato JSON compacto (CLEF) + console em Debug.
- Níveis: `Information` padrão; `Debug` via Configurações; `Trace` com aviso (valores gerados).
- Propriedades de contexto: `SessionId` (por execução do app), `OperationId` (por operação: introspecção, execução, run), `RunId`, `ProfileId` (nunca nome de host/usuário), `Engine`.
- Eventos obrigatórios: início/fim de introspecção (duração, nº objetos), planejamento (hash, diagnósticos por severidade), execução (por tabela: linhas, duração), merges de catálogo (contagens), migrações, autorização emitida/recusada (sem texto digitado), erros.

## 5. Diagnóstico pelo usuário
- Painel "Saída" com log da sessão filtrável.
- "Ajuda → Gerar pacote de diagnóstico": zip com logs (redigidos), versão, configurações (sem perfis), `catalog` **não** incluído; o usuário revisa antes de compartilhar.

## 6. Execuções interrompidas
`generation_run.status = running` encontrado na inicialização (app encerrado abruptamente) → marcado `interrupted`; relatório parcial com últimos batches confirmados (registrados por batch no catálogo quando inserção direta: tabela `generation_run.report` atualizada a cada 10 batches) e oferta de limpeza.
