# 37 — Auditoria de Implementação (2026-10-12)

| Campo | Valor |
|---|---|
| Objetivo | Confrontar o que foi declarado como concluído (plano sequencial, Sprints 0–6) com o código, os testes e a documentação; listar o que falta. |
| Escopo | Repositório em `main` no commit `d9f3bf9`. |
| Método | Build Release completo, execução dos projetos de teste, leitura do código e comparação com contratos e documentos. |
| Resultado | Base sólida e bem documentada até o Sprint 6. Há divergências relevantes, listadas em §4, que entram como trabalho de saneamento no [Plano de Ação](../PLANO-DE-ACAO.md). |

## 1. Verificações executadas nesta auditoria

| Verificação | Resultado |
|---|---|
| `dotnet build MockDataStudio.slnx -c Release` | ✅ 0 erros. ⚠ **29 avisos NuGet de vulnerabilidade** (NU1903/NU1904): `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (alta, transitivo do Microsoft.Data.Sqlite), `SSH.NET` 2024.2.0 (alta, transitivo do Testcontainers), `System.Drawing.Common` 5.0.2 (**crítica**, transitivo do FlaUI). `TreatWarningsAsErrors` não cobre NU19xx, então a afirmação "build sem warnings" do plano anterior vale só para avisos de compilação |
| Core.Tests | ✅ 100/100 |
| Generation.Tests | ✅ 62 aprovados, 1 ignorado (T-GEN-UQ-003, sufixo pendente) |
| Mapping.Tests | ✅ 21/21 |
| Catalog.Tests | ✅ 21/21 (≈ 1 min) |
| SqlTooling.Tests | ✅ 32/32 |
| Architecture.Tests | ✅ 10/10 |
| Providers.ContractTests (SQLite) | ✅ 12 aprovados, 2 ignorados com justificativa (DA-039). ⚠ **Encerramento intermitente do processo de teste**: numa execução o host ficou travado após os testes e foi abortado por timeout (≈ 280 s); em outra, executada em paralelo, terminou normalmente (exit 0) |
| Providers.IntegrationTests | ❌ **Não executado de fato**: não há Docker nesta máquina, e o "smoke test Testcontainers" não sobe container (ver A-03) |
| App.UiTests (FlaUI) | ⚪ Não executado nesta auditoria (exige sessão interativa) |
| CI no GitHub | ⚪ Não verificado (sem `gh` CLI nesta máquina). O workflow existe; o job de integração tem `continue-on-error: true` |
| **Total executado** | **261 testes: 258 aprovados, 3 ignorados, 0 falhas** |

## 2. Inventário do que existe

| Projeto | Arquivos `.cs` | Linhas | Situação |
|---|---|---|---|
| Core | 27 | 3.921 | Nomes canônicos, SchemaModel, digest (serialização/validação), hash, diff, Connections, Bindings, Templates. **Falta `Core.Profiles`** |
| Generation | 17 | 2.884 | RNG, 21 geradores, coerção, MDS-Expr, Feistel, motor por tabela. **API do motor diverge de C06** (ver A-05) |
| Catalog | 11 | 2.422 | Migrações, repositórios, staging, merge, manutenção, pack MVP |
| Mapping | 7 | 1.022 | Normalizador, binder estrutural, pontuação, slots |
| SqlTooling | 9 | 1.172 | Lexer, splitter, classificador (sem conhecer SGBDs) |
| Providers.Abstractions | 13 | 279 | Contratos C02 |
| Providers.Sqlite | 10 | 1.320 | Provider completo (contrato verde) |
| App (WPF) | 29 | 1.270 | Host/DI, Shell MVVM, ViewModels sobre `IDesignDataSource` (ainda dados de exemplo), 7 componentes, SQL Editor separado |
| Application, Infrastructure, Output, Projects | 0 | 0 | **Vazios** |
| Providers.MySql, PostgreSql, Firebird | 0 | 0 | **Vazios** |
| Testes vazios | — | — | Application.Tests, Output.Tests, Projects.Tests |

## 3. Situação por sprint do plano sequencial

| Sprint | Declarado | Constatado |
|---|---|---|
| 0 Fundação | Concluído | Concluído, exceto: smoke de integração não é real (A-03); fixtures `loja.*` divergem da especificação (A-02); "sem warnings" não considera NU19xx (A-04) |
| 1 MVVM/DI | Parcial (1.5, 1.7 e verificação visual pendentes) | Confirmado. Pendências: tema escuro, docs/21 §2.1, textos em `Strings.resx`, logo MySQL, aprovação visual do SQL Editor, idioma (PV-016) |
| 2 Schema/Digest | 2.2 marcado como parcial | `Core.Bindings` e `Core.Templates` **já existem** (anotação desatualizada); falta apenas `Core.Profiles` |
| 3 SqlTooling/SQLite | Concluído com parciais | Confirmado. Corpus de split com 1 arquivo por dialeto; host de testes de contrato trava (A-06) |
| 4 Catálogo | Concluído com parciais | Confirmado. Sem teste de `CAT-IMP-SIZE`; licença do pack pendente (DP-002) |
| 5 Mapeamento | 5.1 parcial | Confirmado. 15 de 200 casos do normalizador; evidências `HistoryMatch`/`CheckConstraintHint`/`UniqueCapacity` ausentes; corpus sintético |
| 6 Geração | Concluído com parciais | Confirmado. Pendências DA-049 (normal/zipf, weighted/switch, lorem, br.*, sufixo, base mista, cpf/cnpj/parent, weekdays/hours, datas com anos/meses) |
| 7–19 | Não iniciados | Confirmado (o motor do Sprint 7.5 existe em forma preliminar) |

**Observação de processo:** o Sprint 2 começou com a verificação do Sprint 1 incompleta, contrariando a regra 7 do plano. A causa é legítima (decisões de produto pendentes), mas foi corrigida no novo plano: pendências que dependem do responsável viram itens explícitos, que não bloqueiam as trilhas técnicas.

## 4. Achados

| ID | Severidade | Achado | Encaminhamento |
|---|---|---|---|
| A-01 | **Alto** | Testes de integração (MySQL/PostgreSQL/Firebird) nunca rodaram: não há Docker local e o CI ignora falhas de integração | Sprint **B1** |
| A-02 | **Alto** | `tests/fixtures/schemas/loja.*.sql` usa nomes no plural (`clientes`, `pedidos`, `itens_pedido`) e **não tem a tabela `entrega`** (FK composta), divergindo de `docs/examples/digest-*.json` e de `schema-loja-sqlite.sql`. Os providers PG/FB/MySQL seriam validados contra um schema diferente do especificado | Sprint **B1** (DA-053) |
| A-03 | **Alto** | `ContainerSmokeTests` não usa Testcontainers: lê arquivos e, sem Docker, **retorna sucesso** ("ignorado com sucesso"). Isso produz verde falso, violando docs/33 §15 | Sprint **B1**; regra nova em docs/33 §15 e docs/26 §1 |
| A-04 | **Alto** | Vulnerabilidades em dependências transitivas (uma crítica, três altas). Nenhuma está no caminho de produção exposto à rede, mas `SQLitePCLRaw` é usado pelo produto | Sprint **B1** (PV-017) |
| A-05 | Médio | O motor implementado (`GenerationEngine.RunAsync(TablePlan, IRowSink)`, `IRowSink` só com `WriteBatchAsync`, `TablePlan` por tabela) não segue C06 (`GenerationPlan`, `PlanStep`, `IKeyOracle`, `IRowSink` completo), e a divergência não estava registrada | Sprints **A1/A2**; registrado como DA-052 e nota em C06 |
| A-06 | Médio | O host de `Providers.ContractTests` às vezes não encerra após os testes — comportamento **intermitente** (uma execução travou até o timeout, outra terminou com exit 0). Causa provável: conexão/pool SQLite ou thread de cancelamento pendente | Sprint **B1** |
| A-07 | Médio | Contratos C01/C02 não refletiam DA-030 (`DigestValidationError.Severity`, `TriggerModel.Table: ObjectName?`) nem DA-034 (`ConnectionEnvironment` no lugar de `EnvironmentTag`) | Corrigido nesta revisão (C01, C02) |
| A-08 | Médio | Anotação desatualizada no plano (2.2 dizia faltar `Core.Bindings`/`Core.Templates`, que já existem) | Corrigido no novo plano |
| A-09 | Médio | Divergência de nomes de ambiente: mockup (Development/QA/Local/Production) × domínio (Development/Test/Staging/Production/Unknown). O código segue o domínio e o docs/21 ainda não registra a decisão (DA-027) | Decisão do responsável **D-03**; Sprint **C1** |
| A-10 | Baixo | Métricas do corpus gravadas em `TestResults/`, que está no `.gitignore`: a evidência não é versionada | Sprint **A1** (gravar resumo em `docs/progress/`) |
| A-11 | Baixo | Logo MySQL ainda ausente (DA-025) | Decisão **D-04** / Sprint **C1** |
| A-12 | Baixo | DP-004 (plataforma de CI) está resolvida de fato (GitHub + Actions), mas segue marcada como pendente | Corrigido em docs/32 |

## 5. O que falta (visão consolidada)

1. **Para o MVP**: planejador relacional completo; writer de script SQL; providers PostgreSQL e Firebird; Projects; Application; Infrastructure; UI ligada a dados reais; E2E e Gate MVP.
2. **Saneamento**: itens A-01..A-11 e as pendências declaradas dos Sprints 1–6.
3. **Para a V1**: MySQL; inserção direta; editor SQL real; Explorer real; evolução de schema; catálogo completo na UI; segurança de execução; formatos e volume; hardening/release.

A distribuição desse trabalho em trilhas paralelas está no [Plano de Ação](../PLANO-DE-ACAO.md).

## 6. Decisões pendentes do responsável (impactam a UI)

| ID | Decisão | Padrão provisório |
|---|---|---|
| D-01 | Idioma da interface: pt-BR (spec, DA-026) ou inglês (mockups) — PV-016 | Manter os textos dos mockups, todos externalizados em `Strings.resx` |
| D-02 | Aprovação visual do SQL Editor separado (DA-028) | Manter a versão separada |
| D-03 | Rótulos de ambiente: os do mockup ou os do domínio | Domínio (Development/Test/Staging/Production/Unknown), com cores do mockup |
| D-04 | Fornecimento do logo MySQL (marca de terceiros) | Ícone genérico de banco com o texto "MySQL" |
| D-05 | Licença do pack de dados `CORE_PTBR` (DP-002) | Dados autorais; licença a definir antes do release |
