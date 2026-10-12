# Mock Data Studio

Aplicação desktop para Windows que conecta a bancos **SQLite, MySQL, PostgreSQL e Firebird**, extrai a estrutura do banco num **Schema Digest** compacto, reconhece o significado das colunas e gera **dados fictícios coerentes, relacionados e reproduzíveis**, entregues como script SQL ou inseridos direto no banco.

Funciona **offline** e não depende de IA. Uma IA externa pode ser usada, de forma opcional, para produzir scripts que alimentam o catálogo de templates.

> **Status (2026-10-12):** especificação completa em [`docs/`](docs/00-index.md). Há uma **interface protótipo** em WPF, construída a partir dos mockups, com dados fixos. Ainda não existe backend: nenhuma conexão, geração ou persistência real.
> A execução segue o [**Plano de Ação**](PLANO-DE-ACAO.md).

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
dotnet build MockDataStudio.sln -c Release
```

```bash
dotnet run --project src/MockDataStudio.App
```

Testes (quando os projetos de teste existirem, a partir do Sprint 0):

```bash
dotnet test -c Release --filter "Category=Unit|Category=Architecture"
```

## Estrutura do repositório

```text
MockDataStudio/
├── README.md                  # este arquivo
├── PLANO-DE-ACAO.md           # sprints em ordem de execução (com checklist)
├── MockDataStudio.sln
├── docs/                      # especificação completa (fonte de verdade)
├── images/                    # mockups de referência da interface
└── src/
    └── MockDataStudio.App/    # WPF — protótipo de interface (dados de exemplo em Samples/)
        ├── Views/             # Home, Connections, Explorer, Schema Digest, Templates, Mapping, Placeholder
        ├── Controls/          # SearchField, extensões de TreeView
        ├── Themes/            # Colors.xaml, Styles.xaml
        ├── Resources/         # realce SQL (sql.xshd), logos dos SGBDs
        └── Samples/           # SampleData.cs — dados fixos dos mockups (temporário)
```

A estrutura completa, com os demais projetos (Core, Providers, Catalog, Mapping, Generation, Output, SqlTooling, Projects, Application, Infrastructure e testes), está em [docs/07-solution-structure.md](docs/07-solution-structure.md). Ela é criada no Sprint 0 do plano.

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

## Licença

A definir (decisão de produto DP-002 em [docs/32-decisions-and-assumptions.md](docs/32-decisions-and-assumptions.md)). Todas as dependências escolhidas têm licença permissiva.
