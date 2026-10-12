# 07 — Estrutura da Solução e do Repositório

| Campo | Valor |
|---|---|
| Objetivo | Definir a árvore do repositório, projetos, pacotes, convenções de build e de código. |
| Escopo | Repositório Git único. |
| Relacionados | [05](05-system-architecture.md), [06](06-technology-decisions.md), [33](33-executor-instructions.md) |

## 1. Árvore do repositório

```text
MockDataStudio/
├── docs/                                  # esta documentação (fonte de verdade)
├── src/
│   ├── MockDataStudio.Core/
│   │   ├── Schema/                        # SchemaModel, TableModel, ColumnModel, ...
│   │   ├── Naming/                        # ObjectName, CanonicalId
│   │   ├── Types/                         # LogicalType, DataTypeInfo
│   │   ├── Digest/                        # DigestSerializer, DigestValidator, digest-1.0.schema.json (embedded)
│   │   ├── Hashing/                       # StructureHasher, CanonicalJson
│   │   ├── Diff/                          # SchemaDiffer, SchemaDiff
│   │   └── Common/                        # Result<T>, MdsError, IClock, Guard
│   ├── MockDataStudio.Providers.Abstractions/
│   ├── MockDataStudio.Providers.Sqlite/
│   ├── MockDataStudio.Providers.MySql/
│   ├── MockDataStudio.Providers.PostgreSql/
│   ├── MockDataStudio.Providers.Firebird/
│   ├── MockDataStudio.Catalog/
│   │   ├── Migrations/                    # V0001__init.sql ... (EmbeddedResource)
│   │   ├── Seeds/                         # pack-core-ptbr.json (EmbeddedResource)
│   │   ├── Repositories/
│   │   └── Staging/                       # StagingImporter, staging-schema.sql
│   ├── MockDataStudio.Mapping/
│   ├── MockDataStudio.Generation/
│   │   ├── Random/                        # DeterministicRandom, StreamKey
│   │   ├── Generators/                    # Core/, Catalog/, Relational/, Br/
│   │   ├── Expressions/                   # MDS-Expr lexer/parser/evaluator
│   │   ├── Uniqueness/                    # FeistelPermutation, MixedRadix
│   │   ├── Planning/                      # GenerationPlanner, DependencyGraph, CycleResolver
│   │   └── Engine/                        # GenerationEngine, RowBatch, KeyOracle
│   ├── MockDataStudio.Output/
│   │   ├── Sql/                           # SqlScriptWriter + formatadores por dialeto
│   │   ├── Tabular/                       # CsvWriter, JsonLinesWriter
│   │   ├── Direct/                        # DirectInsertWriter
│   │   └── Cleanup/                       # CleanupScriptBuilder
│   ├── MockDataStudio.SqlTooling/
│   ├── MockDataStudio.Projects/
│   ├── MockDataStudio.Application/
│   ├── MockDataStudio.Infrastructure/
│   ├── MockDataStudio.App/                # WPF (WinExe)
│   │   ├── Composition/ServiceRegistration.cs
│   │   ├── Shell/  Views/  ViewModels/  Controls/  Themes/  Resources/Highlighting/*.xshd
│   └── MockDataStudio.Cli/                # V2 (projeto criado vazio somente no V2)
├── tests/
│   ├── MockDataStudio.Core.Tests/
│   ├── MockDataStudio.Generation.Tests/
│   ├── MockDataStudio.Mapping.Tests/
│   ├── MockDataStudio.SqlTooling.Tests/
│   ├── MockDataStudio.Catalog.Tests/
│   ├── MockDataStudio.Output.Tests/
│   ├── MockDataStudio.Projects.Tests/
│   ├── MockDataStudio.Application.Tests/
│   ├── MockDataStudio.Providers.ContractTests/    # suíte abstrata executada por SGBD
│   ├── MockDataStudio.Providers.IntegrationTests/ # Testcontainers (Trait "Category=Integration")
│   ├── MockDataStudio.Architecture.Tests/
│   ├── MockDataStudio.App.UiTests/                # FlaUI (Trait "Category=UI")
│   ├── MockDataStudio.Benchmarks/
│   └── fixtures/
│       ├── schemas/                        # DDL de referência: shop, erp-firebird, cycles, composite-keys, ...
│       ├── digests/                        # digests esperados (snapshots Verify)
│       └── mapping-corpus/                 # corpus rotulado de nomes de colunas
├── tools/
│   └── docker/compose.test.yml            # MySQL 8.4, PostgreSQL 17, Firebird 5 para dev local
├── Directory.Build.props
├── Directory.Packages.props               # Central Package Management
├── global.json                            # SDK 10.0.x
├── .editorconfig
├── MockDataStudio.slnx
└── README.md
```

## 2. Projetos e referências

| Projeto | Tipo | Referências de projeto | Pacotes |
|---|---|---|---|
| Core | classlib `net10.0` | — | System.IO.Hashing |
| Providers.Abstractions | classlib `net10.0` | Core | — |
| Providers.Sqlite | classlib | Providers.Abstractions, SqlTooling | Microsoft.Data.Sqlite |
| Providers.MySql | classlib | Providers.Abstractions, SqlTooling | MySqlConnector |
| Providers.PostgreSql | classlib | Providers.Abstractions, SqlTooling | Npgsql |
| Providers.Firebird | classlib | Providers.Abstractions, SqlTooling | FirebirdSql.Data.FirebirdClient |
| Catalog | classlib | Core, SqlTooling | Microsoft.Data.Sqlite, Dapper |
| Mapping | classlib | Core | — |
| Generation | classlib | Core | System.IO.Hashing |
| Output | classlib | Core, Generation, Providers.Abstractions | — |
| SqlTooling | classlib | Core | — |
| Projects | classlib | Core | — (validação JSON Schema própria limitada, ver [22 §6](22-project-file-format.md)) |
| Application | classlib | todos acima exceto App/Providers concretos | Microsoft.Extensions.* abstractions |
| Infrastructure | classlib `net10.0-windows` | Application | System.Security.Cryptography.ProtectedData, Serilog.* |
| App | WinExe `net10.0-windows` (`UseWPF`) | Application, Infrastructure, Providers.* | CommunityToolkit.Mvvm, AvalonEdit, Microsoft.Extensions.Hosting |

> **Observação sobre `Core` com `net10.0`**: bibliotecas sem dependência de Windows usam `net10.0` para permitir testes e CLI multiplataforma no futuro.

> **Validação JSON Schema**: .NET 10 não inclui validador de JSON Schema completo na BCL [K]. Decisão: validação estrutural codificada (`DigestValidator`, `ProjectValidator`) seguindo os schemas publicados em `docs/contracts/schemas/`; os arquivos `.schema.json` são o contrato público para ferramentas externas. Testes T-DIG-010 garantem que exemplos válidos/inválidos têm o mesmo veredito no validador codificado e em um validador de referência executado apenas nos testes (pacote `JsonSchema.Net`, MIT [K], somente em projetos de teste).

## 3. Convenções de build

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Deterministic>true</Deterministic>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```
- `Directory.Packages.props` com versões centralizadas (CPM). Toda nova dependência exige entrada em [06 §8](06-technology-decisions.md) ou ADR.
- Comando de verificação obrigatório: `dotnet build -c Release` + `dotnet test --filter "Category!=Integration&Category!=UI"` (rápido) e suíte completa em marcos.

## 4. Convenções de código

| Tema | Regra |
|---|---|
| Namespaces | `MockDataStudio.<Projeto>.<Pasta>` |
| Async | Sufixo `Async`; `ConfigureAwait(false)` em bibliotecas (não em App) |
| Erros esperados | `Result<T>` com `MdsError { Code, Message, Details }`; exceções apenas para falhas inesperadas |
| Códigos de erro | Prefixos: `PRV-` provider, `DIG-` digest, `CAT-` catálogo, `MAP-` mapeamento, `GEN-` geração, `PLN-` planejamento, `VAL-` validação, `OUT-` saída, `SQL-` editor, `PRJ-` projeto, `SEC-` segurança. Lista em [25 §3](25-error-handling-observability.md) |
| Cultura | Toda formatação de valores para SQL/CSV usa `CultureInfo.InvariantCulture`. UI usa a cultura do usuário (pt-BR padrão). |
| Strings de UI | Arquivo de recursos `Strings.resx` (pt-BR) desde o início (permite en-US futuramente) |
| Logs | Mensagens estruturadas; nunca logar valores de senha ou connection string completa |
| Testes | Nome `Metodo_Cenario_Resultado`; cada teste referenciando ID `T-...` em `[Trait("Id","T-GEN-001")]` |

## 5. Pendências
- DP-003 (empacotamento MSIX vs instalador) — [32](32-decisions-and-assumptions.md).
