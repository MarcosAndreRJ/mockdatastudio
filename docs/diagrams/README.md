# Diagramas

| Campo | Valor |
|---|---|
| Objetivo | Reunir os diagramas principais (Mermaid) para consulta rápida. Os diagramas também aparecem, com contexto, nos documentos de origem. |
| Fonte de verdade | O documento de origem indicado em cada seção; em divergência, prevalece o documento de origem. |

## 1. Componentes e dependências (origem: [05 §2](../05-system-architecture.md))

```mermaid
flowchart TB
    App[App - WPF] --> AppSvc[Application]
    App -. composition root .-> P1 & P2 & P3 & P4
    AppSvc --> Core & Map & Gen & Out & SqlT & Proj & Cat & PA
    Inf[Infrastructure] --> AppSvc
    Map[Mapping] --> Core
    Gen[Generation] --> Core
    Out[Output] --> Core & Gen & PA
    Proj[Projects] --> Core
    SqlT[SqlTooling] --> Core
    Cat[Catalog] --> Core & SqlT
    PA[Providers.Abstractions] --> Core
    P1[Providers.Sqlite] & P2[Providers.MySql] & P3[Providers.PostgreSql] & P4[Providers.Firebird] --> PA & SqlT
```

## 2. Pipeline ponta a ponta

```mermaid
flowchart LR
    DB[(Banco de origem)] -->|ISchemaExtractor| SM[SchemaModel]
    SM -->|DigestSerializer| DG[Digest / Snapshot]
    CAT[(Catálogo)] -->|MappingKnowledge| MAP[MappingEngine]
    SM --> MAP
    MAP -->|ColumnBinding| PRJ[Projeto .mdsproj]
    PRJ --> PL[GenerationPlanner]
    TS[TargetState] --> PL
    PL --> VAL[PreflightValidator]
    VAL --> ENG[GenerationEngine]
    ENG -->|RowBatch| SINK{IRowSink}
    SINK --> SQL[Script SQL segmentado]
    SINK --> DIR[(Banco de destino)]
    SINK --> CSV[CSV / JSONL]
```

## 3. Modelo do catálogo (origem: [09 §2](../09-internal-database.md))

```mermaid
erDiagram
    template_pack ||--o{ template : contem
    template ||--o{ template_field : campos
    template ||--o{ template_alias : aliases
    template_field ||--o{ template_alias : "alias de campo"
    template ||--o{ template_value : valores
    template ||--o{ template_record : registros
    connection_profile ||--o{ query_history : historico
    connection_profile ||--o{ generation_run : destino
```

## 4. Estados de binding (origem: [12 §9](../12-schema-digest.md))

```mermaid
stateDiagram-v2
    [*] --> Active: AutoBind (High) / usuário / estrutural
    Active --> NeedsReview: fingerprint da coluna mudou
    Active --> Orphaned: coluna removida
    NeedsReview --> Active: usuário revisa
    Orphaned --> Active: aceitar proposta de renomeação
    Orphaned --> [*]: usuário exclui
```

## 5. Execução direta com autorização (origem: [19 §1](../19-direct-database-execution.md))

```mermaid
sequenceDiagram
    actor U as Usuário
    participant G as ExecutionGuard
    participant S as GenerationService
    participant T as Destino
    U->>S: Planejar (destino escolhido)
    S->>T: TargetState
    U->>G: Confirmar (digitação se Production)
    G-->>U: ExecutionAuthorization (fingerprint + PlanHash, 10 min)
    U->>S: Executar(autorização)
    S->>T: Revalida fingerprint e faixa de chaves
    S->>T: Batches (COPY / multi-row / prepared)
    S-->>U: Relatório + script de limpeza
```
