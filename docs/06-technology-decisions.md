# 06 — Decisões Tecnológicas, Pesquisa de Mercado e Referências

| Campo | Valor |
|---|---|
| Objetivo | Registrar a pesquisa de mercado/tecnologia, comparar alternativas e consolidar a pilha tecnológica. |
| Escopo | Plataforma, UI, acesso a dados, bibliotecas, ferramentas de teste. Decisões detalhadas em `adr/`. |
| Data da verificação | **2026-10-11** |
| Relacionados | [ADRs](adr/), [07-solution-structure](07-solution-structure.md), [32-decisions-and-assumptions](32-decisions-and-assumptions.md) |

---

## 1. Método e limitações da verificação

- Itens marcados **[V]** foram verificados nesta data por consulta web (nuget.org, dotnet.microsoft.com, firebirdsql.org, documentação MySQL/MariaDB).
- Itens marcados **[K]** vêm do conhecimento técnico da equipe (corte de conhecimento: meados de 2026) e **não** foram reconfirmados online nesta sessão. A IA Executora **deve** confirmar versão e licença no nuget.org antes de adicionar o pacote (regra em [33 §9](33-executor-instructions.md)).
- Nenhum componente foi validado empiricamente (sem protótipo executado). Toda afirmação de comportamento de SGBD marcada como "a verificar por teste" possui teste associado em [26](26-testing-strategy.md).

## 2. Pesquisa de mercado — ferramentas similares

| Ferramenta | Categoria | Padrões bem-sucedidos | Limitações observadas | Oportunidade para o MDS |
|---|---|---|---|---|
| **Bogus** (.NET) [K] | Biblioteca faker | API fluente, locales (inclui `pt_BR`), seed via `Randomizer.Seed` | Saída muda entre versões; sem consciência de schema/FK; datasets embutidos não editáveis pelo usuário | Catálogo editável + estabilidade de saída versionada |
| **Faker** (Python), **Mimesis** [K] | Biblioteca faker | Providers por domínio, locales | Mesmas limitações; não relacional | Idem |
| **Mockaroo** [K] | SaaS | Tipos semânticos, fórmulas, "Dataset column" | Online, limites de linhas, sem introspecção direta de banco | Offline, introspecção nativa, Firebird |
| **dbForge Data Generator** (Devart) [K] | Desktop comercial (SQL Server/MySQL/PG/Oracle) | Geradores por coluna com detecção por nome, FKs, preview | Comercial; sem Firebird/SQLite na mesma ferramenta; produtos separados por SGBD | Multi-SGBD unificado, projetos portáteis |
| **Redgate SQL Data Generator** [K] | Desktop comercial (SQL Server) | Detecção por nome de coluna via regex, seed, geradores "Python/regex" | Somente SQL Server | Mesmo conceito, multi-SGBD |
| **DBeaver – Mock Data** (edição paga) [K] | Plug-in de cliente SQL | Integrado ao explorer | Relacionamentos limitados, configuração por tabela isolada | Projeto global com dependências |
| **Snaplet / Tonic / Synth (getsynth)** [K] | Sintético moderno (CLI/SaaS) | "Schema-as-code", geração relacional, subsetting | Foco em PG/MySQL; nuvem ou CLI; Synth com manutenção reduzida | UX desktop + catálogo SQL + Firebird |
| **DataGrip / DBeaver / HeidiSQL / FlameRobin / IBExpert** [K] | Editores SQL | Autocomplete por contexto, tags de conexão coloridas (DataGrip), modo "manual commit" (DBeaver) | — | Adotar: cor por ambiente, *smart commit*, confirmação para destrutivos |

**Conclusões aplicadas:**
1. Detecção por nome de coluna é padrão de mercado (Redgate, dbForge) — o MDS a torna **explicável e treinável** (aliases + regras aprendidas).
2. Nenhuma ferramenta pesquisada combina: Firebird + catálogo editável por SQL + determinismo forte + offline.
3. A UX de editores maduros (cores por ambiente, transação manual) é copiada no conceito, não na implementação.

## 3. Plataforma e UI

### 3.1 .NET
- **.NET 10 é LTS, suporte até 2028-11-14** [V] ([política de suporte](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)). .NET 8 e 9 terminam em 2026-11-10 [V]; portanto iniciar em .NET 8 seria um erro.
- WPF é distribuído no *Windows Desktop Runtime* do .NET e segue o ciclo do .NET [K — a política citada não lista WPF explicitamente].
- Decisão: **`net10.0-windows`**, C# 14. ADR-015.

### 3.2 Framework de UI — comparação

| Critério (peso) | WPF | WinUI 3 | Avalonia 11 | WinForms |
|---|---|---|---|---|
| Maturidade/estabilidade (3) | 5 | 3 | 4 | 5 |
| Editor de código maduro disponível (3) | 5 (AvalonEdit) | 2 | 4 (AvaloniaEdit) | 3 |
| DataGrid virtualizado (2) | 4 | 2 (sem DataGrid oficial) | 4 | 4 |
| MVVM/data binding (2) | 5 | 4 | 5 | 2 |
| Somente Windows é aceitável (1) | 5 | 5 | 5 | 5 |
| Disponibilidade de conhecimento p/ IA Executora (2) | 5 | 3 | 4 | 5 |
| **Total ponderado** | **63** | 40 | 57 | 52 |

Decisão: **WPF**. Avalonia é a alternativa registrada caso multiplataforma vire requisito (a separação MVVM e a ausência de dependências WPF fora de `MockDataStudio.App` preservam essa opção). ADR-001.

### 3.3 Editor de código
- **AvalonEdit** — MIT [V]; versão utilizada **6.3.0.90** [V]. Verificado no protótipo: compila e executa diretamente em `net10.0-windows` sem avisos ou erros. Fork **AvalonEditB** (MIT, 2.4.0, busca/substituição e seleção em bloco) mantido como contingência.
- Decisão: usar **AvalonEdit** (pacote `AvalonEdit` 6.3.0.90 via CPM), encapsulado atrás de controle de editor para permitir troca sem impacto caso necessário.

### 3.4 Layout/docking
- Decisão: **sem biblioteca de docking no V1**. Layout fixo com `GridSplitter`, painéis recolhíveis e persistência de larguras. Motivo: menor dependência, menor risco; docking flutuante não é necessidade do fluxo principal. Reavaliar AvalonDock (Dirkster) no V2 [K: licença MS-PL — a verificar].

### 3.5 MVVM e DI
- **CommunityToolkit.Mvvm** (MIT, Microsoft) [K] — `ObservableObject`, `RelayCommand`, `AsyncRelayCommand` (com cancelamento), *source generators*.
- **Microsoft.Extensions.Hosting / DependencyInjection / Logging / Options** [K].

## 4. Acesso a dados

### 4.1 Drivers ADO.NET

| SGBD | Pacote | Licença | Versão observada | Observações |
|---|---|---|---|---|
| SQLite | `Microsoft.Data.Sqlite` (+ `SQLitePCLRaw.bundle_e_sqlite3`) | MIT [K] | 10.x [K] | SQLite embarcado; RETURNING requer SQLite ≥ 3.35 (o bundle atual é muito superior) [K] |
| MySQL | **`MySqlConnector`** | **MIT** [V] | 2.6.2 (2026-08-11) [V] | Async real, funciona com MariaDB [V]. `MySql.Data` é GPLv2 + FOSS exception [V] → rejeitado (ADR-012) |
| PostgreSQL | `Npgsql` | PostgreSQL License [K] | 10.0.3 [V] | COPY binário (`BeginBinaryImport`) [K] |
| Firebird | `FirebirdSql.Data.FirebirdClient` | IDPL [K] | 10.3.4 [V] | Documentação de *batching* existe [V]; nome exato da API de batch (`FbBatchCommand`) **não verificado** → otimização opcional, não requisito |

### 4.2 ORM vs ADO.NET (bancos externos)
Bancos externos têm schema desconhecido em tempo de compilação → ORM não agrega. **ADO.NET puro** com `DbConnection/DbCommand/DbDataReader`, parâmetros sempre tipados. ADR-003.

### 4.3 Banco interno
**SQLite + Dapper (Apache-2.0 [K]) + migrações SQL numeradas embutidas.** EF Core rejeitado porque: (a) o schema físico do catálogo é contrato público (usuários escrevem INSERTs contra o *staging*), (b) controle fino de pragmas/transações, (c) inicialização mais leve. ADR-004.

## 5. Geração de dados
- **Bogus** — avaliado. Licença MIT [K — não reconfirmado nesta sessão]; locale `pt_BR` existe [V]. Problemas: saída não estável entre versões (datasets mudam), RNG baseado em `System.Random` encapsulado, sem noção de schema. **Decisão: motor próprio** (ADR-005). Bogus pode entrar no V2 como *pacote de geradores opcional* marcado `Reproducibility = VersionBound`.
- Hashing estável: **`System.IO.Hashing`** (XxHash64/XxHash3, Microsoft, MIT) [K].
- Expressões: **linguagem própria MDS-Expr** (ADR-014). Alternativas avaliadas: NCalc (MIT) [K], DynamicExpresso (MIT) [K], Roslyn Scripting (pesado, superfície de segurança ampla).

## 6. Testes e qualidade

| Ferramenta | Uso | Licença |
|---|---|---|
| xUnit v3 [K] | Testes unitários/integração | Apache-2.0 |
| **Shouldly** [K] | Asserções | BSD-3 (FluentAssertions ≥ 8 tem licença comercial [K] → evitar) |
| Verify (`Verify.XunitV3`) [K] | *Snapshot testing* de digests e scripts SQL | MIT |
| Testcontainers for .NET [K] | MySQL, PostgreSQL, Firebird em Docker | MIT |
| FlaUI [K] | Testes de UI WPF (UIA3) | MIT |
| BenchmarkDotNet [K] | Desempenho | MIT |
| coverlet [K] | Cobertura | MIT |
| NetArchTest.eNhancedEdition ou testes de reflexão próprios [K] | Regras de dependência entre assemblies | MIT |

## 7. Versões de SGBD suportadas (matriz)

| SGBD | Oficial (testado em CI) | Melhor esforço | Não suportado | Fonte |
|---|---|---|---|---|
| SQLite | Biblioteca embarcada no app (≥ 3.45) | Arquivos criados por versões ≥ 3.8 | — | [K] |
| PostgreSQL | 14, 15, 16, 17, 18 | 13 (EOL) | ≤ 12 | [K] |
| MySQL | **8.4 LTS**, 9.x (última innovation) | 8.0 (EOL em 2026-04-30 [V]) | ≤ 5.7 | [V] |
| MariaDB | — | 10.11, 11.4, 11.8 (oficial no V2) | — | [K] |
| Firebird | 3.0, 4.0, 5.0 (5.0.4 / 4.0.7 / 3.0.14 lançados no T2/2026 [V]) | — | 2.5 (sem updates desde 2019 [V]) | [V] |
| Firebird 6.0 | — | Futuro (introduz schemas; ferramentas de terceiros já o suportam [V]; GA não confirmada) | — | [V] |

## 8. Pilha consolidada

```text
Runtime            .NET 10 (net10.0-windows), C# 14
UI                 WPF, CommunityToolkit.Mvvm, AvalonEdit
Hosting/DI/Log     Microsoft.Extensions.Hosting, Serilog (arquivo rolling) [K, Apache-2.0]
Interno            Microsoft.Data.Sqlite + Dapper
Externos           Microsoft.Data.Sqlite, MySqlConnector, Npgsql, FirebirdSql.Data.FirebirdClient
Hash               System.IO.Hashing
Segredos           System.Security.Cryptography.ProtectedData (DPAPI)
Serialização       System.Text.Json (source generators)
Testes             xUnit v3, Shouldly, Verify, Testcontainers, FlaUI, BenchmarkDotNet
Empacotamento      MSIX ou instalador self-contained (decisão DP-003 em 32)
```

## 9. Referências consultadas (2026-10-11)

- .NET Support Policy — https://dotnet.microsoft.com/platform/support/policy/dotnet-core
- FirebirdSql.Data.FirebirdClient — https://www.nuget.org/packages/FirebirdSql.Data.FirebirdClient
- Npgsql 10.0.3 — https://www.nuget.org/packages/npgsql/10.0.3
- MySqlConnector (MIT) — https://mariadb.com/kb/en/mysqlconnector-for-ado-net/ ; comparativo de licenças — https://www.devart.com/blog/top-4-mysql-ado-net-providers-for-2026.html (fonte de fornecedor concorrente; usar com cautela)
- AvalonEdit (MIT) — https://github.com/icsharpcode/AvalonEdit ; AvalonEditB — https://www.nuget.org/packages/avaloneditb
- InnoDB AUTO_INCREMENT handling — https://dev.mysql.com/doc/en/innodb-auto-increment-handling.html ; https://mariadb.com/kb/en/auto_increment-handling-in-innodb
- Firebird news Q2 2026 — https://www.firebirdsql.org/pt/news ; downloads — https://firebirdsql.org/download
- MySQL 8.0 EOL — https://www.openlogic.com/blog/mysql-8-end-of-life ; https://blogs.oracle.com/mysql/extending-mysql-8-0-support-in-mysql-heatwave
- Bogus pt_BR release notes — https://www.nuget.org/packages/Bogus/33.0.2

## 10. Pendências
Registro completo em [32 §5](32-decisions-and-assumptions.md). Principais:
- **PV-001**: confirmar licença IDPL do FirebirdClient para redistribuição em instalador (esperado: permitido) — TASK-19.04.
- **PV-002**: confirmar nome/existência da API de batch do FirebirdClient para Firebird 4+ — TASK-15.02.
- **PV-003**: confirmar licença e versão atual do Bogus antes de qualquer uso no V2.
