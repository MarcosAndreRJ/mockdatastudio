# Registro de Dependências Externas (NuGet)

| Campo | Valor |
|---|---|
| Objetivo | Catálogo auditado de pacotes NuGet com versão centralizada (CPM), licença, data de verificação e escopo de uso. |
| Regra | Nenhuma dependência GPL/AGPL/comercial. Toda nova adição exige registro prévio aqui. |
| Última verificação | 2026-10-12 (Sprint 0 / Etapa 0.6) |

---

## 1. Pacotes de Produção (`src/*`)

| Pacote | Versão | Licença | Data Verif. | Projetos de Uso | Finalidade / Justificativa |
|---|---|---|---|---|---|
| `AvalonEdit` | 6.3.0.90 | MIT | 2026-10-12 | `App` | Componente de edição de texto com realce de sintaxe SQL |
| `CommunityToolkit.Mvvm` | 8.4.0 | MIT | 2026-10-12 | `App` | Base MVVM (ObservableObject, RelayCommand, source generators) |
| `Microsoft.Extensions.Hosting` | 10.0.0 | MIT | 2026-10-12 | `App` | Generic Host para inicialização e ciclo de vida da aplicação desktop |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.0 | MIT | 2026-10-12 | `Application` | Abstrações de ciclo de vida e hosting |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.0 | MIT | 2026-10-12 | `Application` | Abstrações para injeção de dependência |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.0 | MIT | 2026-10-12 | `Application` | Abstrações de log ILogger |
| `Microsoft.Extensions.Options` | 10.0.0 | MIT | 2026-10-12 | `Application` | Padrão IOptions para configurações |
| `Microsoft.Data.Sqlite` | 10.0.0 | MIT | 2026-10-12 | `Providers.Sqlite`, `Catalog` | Driver ADO.NET para SQLite externo e banco interno `catalog.db` |
| `MySqlConnector` | 2.4.0 | MIT | 2026-10-12 | `Providers.MySql` | Driver ADO.NET puro assíncrono para MySQL e MariaDB (ADR-012) |
| `Npgsql` | 10.0.0 | PostgreSQL (MIT-style) | 2026-10-12 | `Providers.PostgreSql` | Driver ADO.NET para PostgreSQL com suporte a COPY binário |
| `FirebirdSql.Data.FirebirdClient` | 10.3.4 | IDPL | 2026-10-12 | `Providers.Firebird` | Driver oficial ADO.NET para Firebird 3.0, 4.0 e 5.0 |
| `Dapper` | 2.1.66 | Apache-2.0 | 2026-10-12 | `Catalog` | Micro-ORM para consultas de alto desempenho no SQLite interno |
| `System.IO.Hashing` | 10.0.0 | MIT | 2026-10-12 | `Core`, `Generation` | Algoritmos de hash não-criptográfico (XxHash64, XxHash3) |
| `System.Security.Cryptography.ProtectedData` | 10.0.0 | MIT | 2026-10-12 | `Infrastructure` | DPAPI do Windows para proteção local de credenciais de conexão |
| `Serilog` | 4.2.0 | Apache-2.0 | 2026-10-12 | `Infrastructure` | Mecanismo central de logging estruturado |
| `Serilog.Extensions.Hosting` | 9.0.0 | Apache-2.0 | 2026-10-12 | `Infrastructure` | Integração Serilog com Microsoft.Extensions.Hosting |
| `Serilog.Sinks.File` | 6.0.0 | Apache-2.0 | 2026-10-12 | `Infrastructure` | Sink de arquivo com rotação e retenção diária |

---

## 2. Pacotes de Teste e Qualidade (`tests/*`)

| Pacote | Versão | Licença | Data Verif. | Projetos de Uso | Finalidade / Justificativa |
|---|---|---|---|---|---|
| `Microsoft.NET.Test.Sdk` | 17.13.0 | MS-PL / MIT | 2026-10-12 | Todos os de teste | SDK de execução de testes do .NET |
| `xunit.v3` | 1.1.0 | Apache-2.0 | 2026-10-12 | Todos os de teste | Framework de testes unitários e de integração |
| `xunit.runner.visualstudio` | 3.0.2 | Apache-2.0 | 2026-10-12 | Todos os de teste | Adaptador para dotnet test e Visual Studio |
| `Shouldly` | 4.3.0 | BSD-3-Clause | 2026-10-12 | Todos os de teste | Biblioteca de asserções fluentes com mensagens explicativas |
| `Verify.XunitV3` | 28.16.1 | MIT | 2026-10-12 | `Core.Tests`, `Output.Tests` | Testes de snapshot para digests e scripts SQL |
| `Testcontainers` | 4.3.0 | MIT | 2026-10-12 | `Providers.IntegrationTests` | Orquestração de containers Docker em testes de integração |
| `Testcontainers.PostgreSql` | 4.3.0 | MIT | 2026-10-12 | `Providers.IntegrationTests` | Módulo oficial para PostgreSQL |
| `Testcontainers.MySql` | 4.3.0 | MIT | 2026-10-12 | `Providers.IntegrationTests` | Módulo oficial para MySQL |
| `coverlet.collector` | 6.0.4 | MIT | 2026-10-12 | CI / Testes | Coleta de cobertura de código |
| `FlaUI.UIA3` | 4.0.0 | MIT | 2026-10-12 | `App.UiTests` | Automação de UI para WPF via UI Automation 3 |
| `BenchmarkDotNet` | 0.14.0 | MIT | 2026-10-12 | `Benchmarks` | Medição precisa de microbenchmarks de CPU e memória |
| `JsonSchema.Net` | 7.3.3 | MIT | 2026-10-12 | Testes de validação | Validador JSON Schema de referência em testes (T-DIG-010) |
