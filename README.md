# Mock Data Studio

Aplicação desktop para Windows que conecta a bancos **SQLite, MySQL, PostgreSQL e Firebird**, extrai a estrutura do banco num **Schema Digest** compacto, reconhece o significado das colunas e gera **dados fictícios coerentes, relacionados e reproduzíveis**, entregues como script SQL ou inseridos direto no banco.

Funciona **offline** e não depende de IA. Uma IA externa pode ser usada, de forma opcional, para produzir scripts que alimentam o catálogo de templates.

> **Status (2026-10-12):** especificação completa em [`docs/`](docs/00-index.md). Implementados e testados: núcleo de schema e Schema Digest, SqlTooling, provider **SQLite**, catálogo interno, mapeamento semântico e motor de geração (258 testes aprovados). A interface WPF já usa MVVM, mas ainda mostra dados de exemplo. Ainda faltam: planejamento relacional, saída SQL, providers PostgreSQL/Firebird/MySQL, serviços de aplicação e a interface ligada a dados reais. Situação detalhada na [auditoria de implementação](docs/37-implementation-audit.md).
> O trabalho restante segue o [**Plano de Ação**](PLANO-DE-ACAO.md), em **três trilhas paralelas** (uma branch por sprint, executadas por 2 ou 3 IAs).

---

## Principais recursos (alvo da V1)

- **Conexões** com perfis por ambiente (Dev/Teste/Homologação/Produção), senhas protegidas pelo Windows (DPAPI) e modo somente leitura.
- **Explorer e editor SQL** com realce de sintaxe, autocomplete, transação manual e proteção contra comandos destrutivos.
- **Schema Digest**: JSON compacto e versionado com tabelas, colunas, chaves, índices, UNIQUEs, CHECKs, sequences, triggers e procedures (só o nome, sem o código). Inclui hash estrutural e comparação entre versões.
- **Catálogo de templates** (nome, CPF, CEP, endereço…) com aliases. Por exemplo, a coluna `ZAP` é reconhecida como `TELEFONE`. Os dados podem ser populados por **scripts SQL** executados num ambiente isolado.
- **Templates compostos**: CEP, cidade e UF do mesmo registro, sempre coerentes.
- **Mapeamento automático e explicável**: cada sugestão traz pontuação e evidências, e o usuário pode revisar.
- **Geradores**: sequências, intervalos numéricos e de datas, máscaras, listas ponderadas, UUID, expressões entre colunas, CPF/CNPJ válidos etc.
- **Geração relacional**: FKs (inclusive compostas), ciclos, autorreferência, cardinalidade, uso de registros já existentes no banco.
- **Saída**: script SQL por dialeto (com segmentação e script de limpeza), inserção direta em lotes, CSV e JSON Lines.
- **Reprodutibilidade**: a mesma seed gera o mesmo script, byte a byte.

## Stack

| Item | Tecnologia |
|---|---|
| Plataforma | .NET 10 (LTS), C# 14, Windows x64 |
| UI | WPF + MVVM (CommunityToolkit.Mvvm), AvalonEdit |
| Bancos externos | ADO.NET: Microsoft.Data.Sqlite, MySqlConnector, Npgsql, FirebirdSql.Data.FirebirdClient |
| Banco interno | SQLite + Dapper (migrações SQL versionadas) |
| Testes | xUnit v3, Shouldly, Verify, Testcontainers, FlaUI, BenchmarkDotNet |

Justificativas em [docs/06-technology-decisions.md](docs/06-technology-decisions.md) e nos [ADRs](docs/adr/README.md).

## Requisitos para desenvolvimento

- Windows 10 22H2+ ou Windows 11 (x64)
- [.NET SDK 10.0](https://dotnet.microsoft.com/download)
- Visual Studio 2026 / Rider (opcional)
- Docker Desktop com containers Linux, apenas para os testes de integração com MySQL, PostgreSQL e Firebird

## Como compilar e executar

```bash
dotnet build MockDataStudio.slnx -c Release
```

```bash
dotnet run --project src/MockDataStudio.App
```

Suíte rápida (unitários, arquitetura e contrato SQLite):

```bash
dotnet test MockDataStudio.slnx -c Release --filter "Category=Unit|Category=Architecture|Category=Contract"
```

Integração com MySQL/PostgreSQL/Firebird (exige Docker):

```bash
dotnet test MockDataStudio.slnx -c Release --filter "Category=Integration"
```

Alternativamente, os scripts `build.ps1` e `test.ps1` na raiz.

## Estrutura do repositório

```text
MockDataStudio/
├── README.md                     # este arquivo
├── PLANO-DE-ACAO.md              # trilhas paralelas, sprints e checklist
├── MockDataStudio.slnx           # solução (.NET 10)
├── Directory.Build.props / Directory.Packages.props / global.json
├── build.ps1 / test.ps1          # scripts locais equivalentes ao CI
├── .github/workflows/ci.yml      # CI (GitHub Actions)
├── docs/                         # especificação (fonte de verdade), auditorias e progresso
├── images/                       # mockups de referência da interface
├── src/
│   ├── MockDataStudio.Core/                  # modelo canônico, digest, hash, diff, tipos comuns   [implementado]
│   ├── MockDataStudio.SqlTooling/            # lexer, splitter, classificador SQL               [implementado]
│   ├── MockDataStudio.Providers.Abstractions/# contratos de providers                          [implementado]
│   ├── MockDataStudio.Providers.Sqlite/      # provider SQLite                                 [implementado]
│   ├── MockDataStudio.Providers.{PostgreSql,Firebird,MySql}/                               [a fazer]
│   ├── MockDataStudio.Catalog/               # banco interno, staging, pack pt-BR              [implementado]
│   ├── MockDataStudio.Mapping/               # mapeamento semântico                            [implementado]
│   ├── MockDataStudio.Generation/            # RNG, geradores, MDS-Expr, motor                 [parcial]
│   ├── MockDataStudio.{Output,Projects,Application,Infrastructure}/                       [a fazer]
│   └── MockDataStudio.App/                   # WPF (MVVM; dados de exemplo em Samples/)        [protótipo]
├── tests/                        # testes por projeto + fixtures (schemas, snapshots, corpus)
└── tools/docker/                 # compose para bancos de teste
```

Detalhes de cada projeto e das regras de dependência: [docs/07-solution-structure.md](docs/07-solution-structure.md).

## Documentação

| Para… | Leia |
|---|---|
| Visão geral e índice | [docs/00-index.md](docs/00-index.md) |
| Entender o produto e as decisões | [docs/01-product-vision.md](docs/01-product-vision.md) |
| Arquitetura | [docs/05-system-architecture.md](docs/05-system-architecture.md) |
| Contratos (interfaces C# e JSON Schemas) | [docs/contracts/](docs/contracts/README.md) |
| Telas e design system | [docs/21-user-interface-ux.md](docs/21-user-interface-ux.md) |
| Implementar (IA ou pessoa) | [docs/33-executor-instructions.md](docs/33-executor-instructions.md) e [PLANO-DE-ACAO.md](PLANO-DE-ACAO.md) |

## Regras de contribuição (resumo)

1. `docs/` é a fonte de verdade. Código e documentação mudam no mesmo commit.
2. Build sem warnings. Nenhuma tarefa é dada como concluída sem testes executados e evidência registrada ([docs/29 §1](docs/29-acceptance-criteria.md)).
3. Nenhum segredo (senha, connection string) em projetos, logs ou repositório.
4. Testes **nunca** usam bancos reais. Usam apenas containers ou arquivos temporários criados pelo próprio teste.
5. Novas dependências exigem licença permissiva e registro em `docs/dependencies.md`.
6. Trabalho em branches `sprint/<ID>-<slug>`, uma por sprint, mescladas na `main` com `--no-ff` ao fim da sprint. Nunca use `push --force` na `main` ([PLANO-DE-ACAO.md §3](PLANO-DE-ACAO.md)).

## Licença

A definir (decisão de produto DP-002 em [docs/32-decisions-and-assumptions.md](docs/32-decisions-and-assumptions.md)). Todas as dependências escolhidas têm licença permissiva.
