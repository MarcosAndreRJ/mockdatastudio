# 32 — Decisões, Premissas, Questões Abertas e Verificações Pendentes

| Campo | Valor |
|---|---|
| Objetivo | Registro central de ambiguidades e sua classificação; fonte para a IA Executora saber o que é firme, o que é hipótese e o que exige aprovação. |
| Escopo | Produto inteiro. |
| Regras | Toda nova decisão/premissa da IA Executora é registrada aqui (ou em ADR, se arquitetural) — [33 §8](33-executor-instructions.md). |

Classificação (briefing §17):
- **DA — Decisão autônoma**: resolvida por engenharia; pode ser revista com justificativa.
- **PA — Premissa documentada**: hipótese explícita; se falsa, impacto descrito.
- **DP — Decisão de produto**: requer aprovação do responsável; há um padrão provisório que não bloqueia o desenvolvimento.
- **BT — Bloqueio técnico**: impede especificação/implementação.
- **PV — Verificação pendente**: fato técnico a confirmar empiricamente ou em fonte oficial.

## 1. Decisões autônomas (DA)

| ID | Decisão | Referência |
|---|---|---|
| DA-001 | .NET 10 LTS, WPF, MVVM Toolkit, monólito modular | ADR-001/002/015 |
| DA-002 | ADO.NET para bancos externos; SQLite+Dapper para catálogo | ADR-003/004 |
| DA-003 | Motor de geração próprio; RNG por contador; MDS-Expr próprio | ADR-005/013/014 |
| DA-004 | Chaves Explicit por padrão | ADR-008 |
| DA-005 | Importação de catálogo via staging isolado | ADR-016 |
| DA-006 | `Unknown` tratado como `Production` para proteções; ambiente padrão de perfil novo = `Unknown` | 23 §5.1 |
| DA-007 | Presets de gerador são mapeáveis como templates (unificação da experiência) | 13 §1 |
| DA-008 | Namespace virtual `PUBLIC` para Firebird 3–5 | 08 §2.1 |
| DA-009 | Hash estrutural ignora nomes de constraints/índices e ordem de colunas | 12 §6 |
| DA-010 | Colunas no digest como tuplas posicionais; chaves curtas apenas em objetos folha | 12 §2–3 |
| DA-011 | `tinyint(1)` = Boolean no MySQL (configurável) | 10 §4 |
| DA-012 | SQLite: `foreign_keys=ON` por padrão nas conexões do app | 10 §3 |
| DA-013 | Sem biblioteca de docking no V1 | 06 §3.4 |
| DA-014 | `NormalizedName` com `Compact` (literal) e `ExpandedCompact` (abreviações expandidas) | 14 §2, C04 |
| DA-015 | Tipos de dados puros (perfis de conexão, capacidades, bindings, perfis de geração, dados de template) residem em `Core` para respeitar AR-02 | C02, C05, C06 |
| DA-016 | `Providers.*` e `Catalog` podem referenciar `SqlTooling` | 05 AR-04 |
| DA-017 | Validação de JSON Schema em runtime por código; validador de referência só em testes | 07 §2 |
| DA-018 | Firebird: um registro por INSERT em script; `COMMIT` a cada 5.000 | 18 §4–5 |
| DA-019 | Paralelismo apenas dentro de tabela no V1 | 24 §4 |
| DA-020 | Unicidade composta bijetiva usa componentes uniformes (perde pesos) | 15 §8.4 |
| DA-021 | Template de e-mail usa somente domínios fictícios/reservados | 23 §10 |
| DA-022 | MVP com SQLite+PostgreSQL+Firebird; MySQL no M5 | 27 §2 |

## 2. Premissas (PA)

| ID | Premissa | Impacto se falsa |
|---|---|---|
| PA-001 | Uso mono-usuário local; sem compartilhamento simultâneo do catálogo | Exigiria servidor/concorrência multiusuário |
| PA-002 | Público principal usa pt-BR | Priorizar tradução en-US (strings já externalizadas) |
| PA-003 | Volumes típicos ≤ 10 M linhas por execução; máximo suportado 100 M/tabela | Reavaliar paralelismo entre tabelas |
| PA-004 | Destinos são majoritariamente bancos de dev/test | Proteções já cobrem produção |
| PA-005 | Firebird somente dialeto 3 | Dialeto 1 exigiria regras de quoting/tipos diferentes |
| PA-006 | Somente x64 no V1 | ARM64 no V2 (drivers nativos FB) |
| PA-007 | Firebird 6 manterá objetos existentes no schema `PUBLIC` | Ajuste de mapeamento de namespace no provider FB6 (sem impacto em projetos existentes graças ao `virtual`) |
| PA-008 | Firebird embedded não distribuído no V1 | Usuário instala fbclient; documentar |
| PA-009 | Nomes de municípios/UFs (fatos públicos) podem ser usados; demais listas são autorais | Revisão jurídica do pack |
| PA-010 | Valores de sequence > 2^53 serializados como string no digest | — |
| PA-011 | `numeric` sem precisão no PG: geração assume escala 2 e faixa até 10^9 | Usuário ajusta parâmetros |
| PA-012 | Runners Windows hospedados executam FlaUI | Máquina dedicada |
| PA-013 | `ReferenceInstant` fixado na criação do perfil é o comportamento desejado | Alternar padrão para `Now` |
| PA-014 | Fuso horário do perfil = fuso do Windows na criação | — |

## 3. Decisões de produto (DP) — requerem aprovação

| ID | Questão | Padrão provisório (não bloqueia) | Impacto |
|---|---|---|---|
| DP-001 | Nome comercial definitivo | "Mock Data Studio" | Namespaces/instalador (renomear antes do V1) |
| DP-002 | Modelo de licenciamento do produto (proprietário, open-source, freemium) | Dependências todas permissivas — compatível com qualquer escolha | Licença do repositório, EULA |
| DP-003 | Empacotamento: MSIX, instalador (WiX/Inno) ou portátil | Instalador self-contained + zip portátil | TASK-19.04 |
| DP-004 | Plataforma de repositório/CI | GitHub Actions (scripts locais equivalentes) | TASK-00.03 |
| DP-005 | Idiomas da UI no V1 | Somente pt-BR | Esforço de tradução |
| DP-006 | Incluir anonimização no V2 e com que escopo | Escopo restrito (27 §5 item 23) | Riscos LGPD |
| DP-007 | Telemetria opt-in no futuro | Nenhuma | NFR-PLAT-002 |
| DP-008 | Certificado de assinatura de código | Necessário para V1 | SmartScreen |
| DP-009 | Metas de usabilidade (NFR-USA-001) e quem participa dos testes | 3 usuários internos | Validação de UX |

## 4. Bloqueios técnicos (BT)

Nenhum bloqueio técnico identificado que impeça iniciar a implementação. Itens com incerteza estão como PV com fallback definido.

## 5. Verificações pendentes (PV)

| ID | Verificação | Como verificar | Fallback se negativo | Tarefa |
|---|---|---|---|---|
| PV-001 | Licença IDPL do FirebirdClient permite redistribuição no instalador | Ler licença no repositório oficial | Download sob demanda / instrução de instalação | TASK-19.04 |
| PV-002 | Existência/nome da API de batch do FirebirdClient para FB4+ | Documentação/código-fonte do NETProvider | Prepared por linha | TASK-15.02 |
| PV-003 | Licença e versão atual do Bogus | nuget.org/GitHub | Não usar | V2 |
| PV-004 | FB4/5 aceita `OVERRIDING SYSTEM VALUE` em identity ALWAYS | T-PRV-FB-KEY-002 | Captured | TASK-08.03 |
| PV-005 | Semântica de `ALTER SEQUENCE ... RESTART WITH` por versão FB | T-PRV-FB-KEY-003 | Ajustar valor calculado por versão | TASK-08.03 |
| PV-006 | COPY binário PG com identity ALWAYS | T-PRV-PG-COPY-001 | INSERT multi-linha com OVERRIDING | TASK-15.02 |
| PV-007 | Autorreferência NOT NULL na mesma linha por SGBD | T-REL-SELF-NN-<X> | Bloqueio `PLN-SELF-NOTNULL` | TASK-10.04/16.01 |
| PV-008 | AvalonEdit funciona em net10.0-windows sem problemas | Protótipo na TASK-13.03 | AvalonEditB | TASK-13.03 |
| PV-009 | Imagem Docker oficial do Firebird (nome/tags) | Docker Hub | Imagem comunitária | TASK-00.05 |
| PV-010 | Formato de `COLUMN_DEFAULT` no MySQL 8.4/9.x | T-PRV-MYSQL-002 | Normalização ajustada | TASK-10.01 |
| PV-011 | Norma vigente do CNPJ alfanumérico (algoritmo de DV) | Publicação oficial da Receita Federal | Adiar `br.cnpj` v2 | TASK-17.05 |
| PV-012 | Disponibilidade de `MON$DATABASE.MON$GUID` (FB4+) para fingerprint | Teste | Usar caminho/alias + servidor | TASK-08.03 |
| PV-013 | Palavra-chave `Foreign Keys` na connection string do Microsoft.Data.Sqlite | Documentação/teste | `PRAGMA foreign_keys=ON` após abrir | TASK-02.02 |
| PV-014 | `DbCommand.Cancel` efetivo em MySqlConnector e FirebirdClient | T-PRV-<X>-009 | Fechar conexão | TASK-08.03/10.03 |
| PV-015 | Pacote JSON1 presente no `SQLitePCLRaw.bundle_e_sqlite3` atual | Teste em 04.01 | Validação de JSON em C# | TASK-04.01 |

## 6. Histórico de alterações de decisões

| Data | Item | Alteração | Motivo |
|---|---|---|---|
| 2026-10-11 | — | Criação do registro | Especificação inicial |
| 2026-10-11 | DA-015/016 | Movidos tipos puros para Core; Providers/Catalog → SqlTooling | Auditoria (36, achados A-01, A-02) |
