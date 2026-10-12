# Contratos Técnicos

| Campo | Valor |
|---|---|
| Objetivo | Definir as interfaces C# e os formatos JSON públicos que a IA Executora deve implementar. |
| Escopo | Contratos **propostos para implementação** (não pseudocódigo), salvo quando marcado `// PSEUDOCÓDIGO`. |
| Relacionados | [05](../05-system-architecture.md), [08](../08-domain-model.md) |

## Regras de interpretação

1. Os nomes de tipos, membros e namespaces aqui são **normativos**. A IA Executora pode acrescentar membros (internos ou públicos) mas não pode renomear/remover sem ADR.
2. Assinaturas omitidas com `// ...` são detalhes livres de implementação.
3. Todo método assíncrono recebe `CancellationToken ct` como último parâmetro.
4. Coleções públicas são `IReadOnlyList<T>`/`IReadOnlyDictionary<K,V>`; records de `Core` são imutáveis.
5. Erros esperados retornam `Result<T>`; exceções somente para defeitos/infraestrutura.
6. Versões de contrato: cada interface de extensão expõe `ContractVersion` via atributo de assembly `[assembly: MdsContractVersion(1)]` no assembly de abstrações.

## Índice

| Arquivo | Conteúdo | Assembly |
|---|---|---|
| [C01-common-and-schema.md](C01-common-and-schema.md) | `Result<T>`, `MdsError`, `ObjectName`, modelo canônico, digest, hash, diff | Core |
| [C02-providers.md](C02-providers.md) | `IDatabaseProvider`, `ISchemaExtractor`, `ISqlDialect`, `ITypeMapper`, `IBulkInserter`, `ITargetInspector`, `IQueryExecutor` | Providers.Abstractions |
| [C03-catalog.md](C03-catalog.md) | Repositórios do catálogo, importação staging, packs | Catalog |
| [C04-mapping.md](C04-mapping.md) | `IMappingEngine`, `INameNormalizer`, sugestões | Mapping |
| [C05-generation.md](C05-generation.md) | RNG, `IValueGenerator`, `IGeneratorFactory`, `GeneratorSpec`, MDS-Expr | Generation |
| [C06-planning.md](C06-planning.md) | `IGenerationPlanner`, `GenerationPlan`, grafo e ciclos, `KeyOracle` | Generation |
| [C07-validation.md](C07-validation.md) | `IPreflightValidator`, `ValidationIssue` | Generation / Application |
| [C08-output.md](C08-output.md) | `IOutputWriter`, `ISqlLiteralFormatter`, segmentação, limpeza, inserção direta | Output |
| [C09-projects-and-application.md](C09-projects-and-application.md) | `MdsProject`, `IProjectStore`, serviços de aplicação | Projects / Application |
| [schemas/digest-1.0.schema.json](schemas/digest-1.0.schema.json) | JSON Schema do digest | público |
| [schemas/project-1.0.schema.json](schemas/project-1.0.schema.json) | JSON Schema do projeto | público |
| [schemas/template-pack-1.0.schema.json](schemas/template-pack-1.0.schema.json) | JSON Schema de pacote de templates | público |
