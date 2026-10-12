# 02 — Requisitos Funcionais

| Campo | Valor |
|---|---|
| Objetivo | Catálogo rastreável de requisitos funcionais (FR) com regras, fluxos, erros, critérios de aceite e testes. |
| Escopo | MVP, V1 e V2. Requisitos não funcionais em [04](04-non-functional-requirements.md); casos de uso em [03](03-functional-specification.md). |
| Rastreabilidade | [35-traceability-matrix](35-traceability-matrix.md) |

## Convenções

- **Prioridade**: `MVP` (prova técnica), `V1` (profissional), `V2` (avançado) + MoSCoW (`M` must, `S` should, `C` could).
- **AC** = critério de aceite, numerado `AC-<FR>.<n>`; todo AC é verificável por teste listado.
- Fluxos detalhados de UI estão nas telas `Sxx` de [21](21-user-interface-ux.md); aqui ficam regras e comportamento.
- Erros referenciam códigos de [25 §3](25-error-handling-observability.md).

---

## A. Conexões (FR-001 – FR-008)

### FR-001 — Gerenciar perfis de conexão
| Atributo | Especificação |
|---|---|
| Descrição | Criar, editar, duplicar, excluir e listar perfis de conexão para SQLite, MySQL, PostgreSQL e Firebird. |
| Justificativa | Base de todas as operações; reutilização entre projetos. |
| Prioridade | MVP-M (SQLite/PG/FB), V1-M (MySQL) |
| Dependências | FR-003, FR-004 |
| Regras | Nome único (case-insensitive); ambiente obrigatório (padrão Unknown); campos por SGBD conforme [10](10-database-providers.md); `Extra` restrito à allow-list do provider. |
| Entradas / Saídas | `ConnectionProfile` + senha opcional / perfil persistido em `connection_profile`. |
| Fluxo principal | S02: Novo → escolher SGBD → preencher → (Testar) → Salvar. |
| Alternativos | Duplicar (copia sem senha); Excluir (confirmação; bloqueado se em uso por execução ativa). |
| Erros | Nome duplicado; porta inválida; arquivo SQLite inexistente sem "criar novo" (`PRV-SQLITE-NOFILE`). |
| Aceite | AC-001.1 perfil salvo reaparece após reiniciar o app. AC-001.2 nome duplicado é rejeitado com mensagem. AC-001.3 settings persistidos não contêm senha (inspeção do banco). |
| Testes | T-CAT-001, T-SEC-002, T-UI-FLOW-001 |

### FR-002 — Testar conexão
| Atributo | Especificação |
|---|---|
| Descrição | Abrir conexão com os dados do formulário e exibir versão do servidor e latência. |
| Prioridade | MVP-M |
| Dependências | FR-001 |
| Regras | Timeout = `ConnectTimeoutSec`; cancelável; não persiste nada. Versão abaixo da mínima suportada → aviso `PRV-UNSUPPORTED-VERSION` (não bloqueia o teste). |
| Saídas | `ServerInfo` (versão, produto, banco, fingerprint). |
| Erros | `PRV-CONN-FAILED` com mensagem do driver e dica (ex.: "verifique se o Firebird está aceitando conexões na porta 3050"). |
| Aceite | AC-002.1 conexão válida mostra versão em ≤ timeout. AC-002.2 servidor inexistente mostra erro sem travar a UI. AC-002.3 cancelar interrompe em ≤ 1 s. |
| Testes | T-PRV-<X>-001, T-UI-FLOW-001 |

### FR-003 — Armazenamento seguro de credenciais
| Atributo | Especificação |
|---|---|
| Descrição | Senhas protegidas por DPAPI (CurrentUser) ou solicitadas a cada uso. |
| Prioridade | MVP-M |
| Regras | [23 §3](23-security.md); senha nunca em logs, projetos, histórico ou mensagens. |
| Aceite | AC-003.1 coluna `secret` não contém a senha em claro. AC-003.2 outro usuário Windows não decifra. AC-003.3 "Sempre perguntar" solicita a senha ao abrir conexão. |
| Testes | T-SEC-002, T-SEC-004, T-SEC-012 |

### FR-004 — Ambientes e proteção
| Atributo | Especificação |
|---|---|
| Descrição | Classificar perfis em Development/Test/Staging/Production/Unknown com cor e somente leitura. |
| Prioridade | MVP-M (tag + ReadOnly), V1-M (matriz completa de guarda) |
| Regras | Unknown ≡ Production para proteções; Production ⇒ ReadOnly padrão; matriz [23 §5.2](23-security.md). |
| Aceite | AC-004.1 perfil ReadOnly bloqueia DML/DDL no editor e inserção direta. AC-004.2 chip de ambiente visível em todas as superfícies de execução. |
| Testes | T-SEC-005, T-SQL-GUARD-001, T-UI-FLOW-004 |

### FR-005 — Configurações avançadas
| Atributo | Especificação |
|---|---|
| Descrição | Timeouts, SSL, charset, role/dialeto (FB), search_path (PG), `tinyint1IsBool` (MySQL), `dateStorage`/`foreignKeys` (SQLite). |
| Prioridade | V1-S |
| Regras | Somente chaves da allow-list (`ExtraSettings`); validação de tipo por definição. |
| Aceite | AC-005.1 chave fora da allow-list não é aceita. AC-005.2 timeout de comando configurado é aplicado (teste com `pg_sleep`). |
| Testes | T-PRV-<X>-009, T-PRV-SQLITE-DATE-001 |

### FR-006 — Resiliência de conexão
| Atributo | Especificação |
|---|---|
| Descrição | Tratar desconexões, timeouts e retentativas de abertura. |
| Prioridade | V1-M |
| Regras | [05 §9](05-system-architecture.md), [10 §7](10-database-providers.md): retry apenas na abertura (2×); nunca em escrita. |
| Aceite | AC-006.1 queda durante consulta marca sessão desconectada e oferece reconectar. AC-006.2 nenhum comando de escrita é reenviado automaticamente. |
| Testes | T-SQL-EXEC-002, T-DIR-002 |

### FR-007 — Seleção explícita de origem, destino e referência
| Atributo | Especificação |
|---|---|
| Descrição | O projeto distingue conexão de origem (introspecção), destino (escrita/estado de chaves) e referência (dados existentes). |
| Prioridade | V1-M |
| Regras | Inserção direta exige destino escolhido **na operação** (não herdado silenciosamente); fingerprints registrados; divergência → aviso. |
| Aceite | AC-007.1 não é possível iniciar inserção direta sem selecionar destino no diálogo de execução. AC-007.2 trocar o perfil de destino para outro servidor gera alerta de fingerprint. |
| Testes | T-SEC-003, T-REL-011 |

### FR-008 — Arquitetura extensível de providers
| Atributo | Especificação |
|---|---|
| Descrição | Novos SGBDs adicionáveis implementando contratos C02 sem alterar núcleo. |
| Prioridade | MVP-M |
| Aceite | AC-008.1 nenhum `switch` sobre `DatabaseEngine` fora de Providers/Composition. AC-008.2 todos os providers passam na suíte de contrato. |
| Testes | T-ARC-007, T-PRV-<X>-* |

## B. Explorer (FR-010 – FR-015)

### FR-010 — Navegar objetos
| Atributo | Especificação |
|---|---|
| Descrição | Árvore de namespaces, tabelas, views, sequences, rotinas e triggers. |
| Prioridade | MVP-M (tabelas/colunas), V1-M (demais) |
| Regras | Carga lazy por nó; objetos de sistema excluídos ([11 §1](11-schema-introspection.md)). |
| Aceite | AC-010.1 schema `loja` exibe todos os objetos esperados nos 4 SGBDs. AC-010.2 10.000 tabelas navegáveis sem bloqueio. |
| Testes | T-PRV-<X>-002, T-UI-PERF-001 |

### FR-011 — Inspecionar tabela
| Atributo | Especificação |
|---|---|
| Descrição | Colunas (tipo nativo/lógico, nulo, default, identity/gerada), PK, FKs, UNIQUEs, índices, CHECKs, triggers (sem corpo). |
| Prioridade | V1-M |
| Aceite | AC-011.1 informações batem com o digest da tabela. |
| Testes | T-PRV-<X>-002/004/005 |

### FR-012 — Navegar referências
| Atributo | Especificação |
|---|---|
| Descrição | Listar tabelas que referenciam e que são referenciadas, com navegação. |
| Prioridade | V1-S |
| Aceite | AC-012.1 `pedido` mostra `cliente` (referencia) e `item_pedido` (referenciada por). |
| Testes | T-UI-FLOW-003 |

### FR-013 — Ações rápidas
| Atributo | Especificação |
|---|---|
| Descrição | Abrir SELECT (limite por dialeto), contar linhas, gerar INSERT modelo, copiar nome qualificado, adicionar ao projeto. |
| Prioridade | V1-S |
| Regras | `SELECT` gerado usa sintaxe de limite do dialeto (`LIMIT`, `FIRST`/`ROWS`/`FETCH FIRST`). |
| Aceite | AC-013.1 o SELECT gerado executa sem erro nos 4 SGBDs. |
| Testes | T-SQL-EXEC-001 |

### FR-014 — Atualizar metadados
| Atributo | Especificação |
|---|---|
| Descrição | Refresh por nó/tabela sem recarregar tudo; não altera snapshots do projeto. |
| Prioridade | V1-M |
| Aceite | AC-014.1 coluna adicionada no banco aparece após refresh do nó. AC-014.2 snapshot ativo do projeto permanece inalterado. |
| Testes | T-PRV-<X>-002 |

### FR-015 — Filtro de objetos
| Atributo | Especificação |
|---|---|
| Descrição | Filtro incremental por nome na árvore. |
| Prioridade | V1-C |
| Aceite | AC-015.1 filtrar "PED" mostra apenas objetos com "PED" (case-insensitive). |
| Testes | T-UI-PERF-001 |

## C. Editor SQL (FR-020 – FR-031)

### FR-020 — Editor com abas e realce
| Atributo | Especificação |
|---|---|
| Descrição | Múltiplas abas, cada uma ligada a um perfil, com realce por dialeto e recuperação de abas. |
| Prioridade | V1-M |
| Regras | [20 §2–3](20-sql-editor.md). |
| Aceite | AC-020.1 duas abas em perfis diferentes executam independentemente. AC-020.2 após encerramento abrupto, abas não salvas são recuperadas. |
| Testes | T-UI-FLOW-003 |

### FR-021 — Autocomplete de palavras-chave
| Prioridade | V1-M |
|---|---|
| Descrição | Sugestões de palavras-chave e funções do dialeto. |
| Aceite | AC-021.1 digitar `SEL` + `Ctrl+Espaço` sugere `SELECT`. |
| Testes | T-SQL-COMP-001 |

### FR-022 — Autocomplete de objetos e aliases
| Atributo | Especificação |
|---|---|
| Descrição | Tabelas após FROM/JOIN, colunas após `alias.`/`tabela.` e em cláusulas, com resolução de aliases do comando corrente. |
| Prioridade | V1-M |
| Regras | [20 §6](20-sql-editor.md); latência p95 ≤ 50 ms. |
| Aceite | AC-022.1 `SELECT c. FROM cliente c` sugere colunas de `cliente` após `c.`. AC-022.2 identificadores que exigem aspas são inseridos citados. |
| Testes | T-SQL-COMP-001, B-SQL-001 |

### FR-023 — Divisão de scripts por dialeto
| Atributo | Especificação |
|---|---|
| Descrição | Separar comandos respeitando strings, comentários, dollar-quoting, `SET TERM`, `DELIMITER`, blocos BEGIN/END. |
| Prioridade | V1-M (também usado no MVP pelo importador de catálogo, dialeto SQLite) |
| Aceite | AC-023.1 corpus `sql-split` de cada dialeto passa 100%. |
| Testes | T-SQL-SPLIT-<X> |

### FR-024 — Executar comando, seleção ou script
| Prioridade | V1-M |
|---|---|
| Descrição | `Ctrl+Enter` (atual/seleção), `F5` (script), `Shift+F5` (até o cursor); parar no primeiro erro (configurável). |
| Aceite | AC-024.1 com cursor no 2º de 3 comandos, `Ctrl+Enter` executa só o 2º. AC-024.2 script com erro no 2º comando para e destaca o erro. |
| Testes | T-SQL-EXEC-001 |

### FR-025 — Grade de resultados paginada
| Prioridade | V1-M |
|---|---|
| Descrição | Primeira página 500 linhas, carregar mais/tudo (limite), múltiplos result sets, visualizadores de BLOB/texto. |
| Aceite | AC-025.1 consulta de 1 M linhas mostra a 1ª página em ≤ 2 s (PG local) sem carregar tudo. |
| Testes | T-UI-PERF-003 |

### FR-026 — Cancelar execução
| Prioridade | V1-M |
|---|---|
| Descrição | Cancelar consulta em andamento. |
| Regras | `DbCommand.Cancel`; fallback fechar conexão em 5 s. |
| Aceite | AC-026.1 `pg_sleep(60)`/equivalente cancelado em ≤ 6 s nos 4 SGBDs (SQLite: consulta recursiva longa). |
| Testes | T-PRV-<X>-009, T-SQL-EXEC-002 |

### FR-027 — Controle de transação (manual/autocommit)
| Prioridade | V1-M |
|---|---|
| Descrição | Modo Manual com barra de transação pendente, Commit/Rollback; padrão por ambiente. |
| Aceite | AC-027.1 em Manual, DELETE seguido de Rollback não altera dados. AC-027.2 fechar aba com transação pendente exige escolha. |
| Testes | T-SQL-EXEC-001, T-UI-FLOW-003 |

### FR-028 — Proteção de operações destrutivas
| Prioridade | V1-M |
|---|---|
| Descrição | Classificação de comandos destrutivos e confirmação conforme matriz. |
| Regras | [20 §5, §8](20-sql-editor.md). |
| Aceite | AC-028.1 `DELETE FROM t` (sem WHERE) em Test pede confirmação. AC-028.2 em Production RW exige digitar o nome do banco. AC-028.3 em ReadOnly é bloqueado e o servidor também está em modo somente leitura. |
| Testes | T-SQL-CLASS-001, T-SQL-GUARD-001, T-SEC-005, T-SEC-006 |

### FR-029 — Histórico de comandos
| Prioridade | V1-S |
|---|---|
| Descrição | Registro, busca e reutilização; retenção configurável; Production grava só metadados por padrão. |
| Aceite | AC-029.1 comando executado aparece no histórico com duração e resultado. |
| Testes | T-SQL-HIST-001 |

### FR-030 — Exportar resultados
| Prioridade | V1-S |
|---|---|
| Descrição | CSV, JSON Lines, INSERTs (dialeto escolhido), copiar TSV. |
| Aceite | AC-030.1 INSERTs exportados reexecutam no mesmo SGBD produzindo os mesmos dados. |
| Testes | T-OUT-ROUNDTRIP-<X>, T-OUT-005 |

### FR-031 — Erros com posição
| Prioridade | V1-S |
|---|---|
| Descrição | Mensagem do SGBD, código, posição sublinhada quando disponível. |
| Aceite | AC-031.1 erro de sintaxe em PG sublinha a posição informada. |
| Testes | T-PRV-<X>-008 |

## D. Schema Digest (FR-040 – FR-047)

### FR-040 — Extrair modelo canônico
| Atributo | Especificação |
|---|---|
| Descrição | Introspecção completa para `SchemaModel` ([11](11-schema-introspection.md)). |
| Prioridade | MVP-M (SQLite/PG/FB), V1-M (MySQL) |
| Regras | Somente leitura, transação snapshot, consultas em lote, progresso, cancelamento, fases opcionais. |
| Erros | `PRV-INTROSPECT-PARTIAL`, `PRV-CONN-LOST`. |
| Aceite | AC-040.1 schema `loja` extraído igual ao snapshot de referência. AC-040.2 1.000 tabelas em ≤ 30 s. |
| Testes | T-PRV-<X>-002..005/011, B-INT-001 |

### FR-041 — Exportar digest
| Prioridade | MVP-M |
|---|---|
| Descrição | Serializar no formato `mds.digest` 1.0 (minificado/indentado/gzip). |
| Aceite | AC-041.1 arquivo válido pelo JSON Schema. AC-041.2 média ≤ 90 bytes/coluna no corpus. |
| Testes | T-DIG-001, B-DIG-SIZE |

### FR-042 — Importar e validar digest
| Prioridade | MVP-M |
|---|---|
| Descrição | Carregar digest de arquivo, validar ([12 §7](12-schema-digest.md)) e usar sem conexão. |
| Aceite | AC-042.1 todos os erros são listados com JSON Path. AC-042.2 projeto funciona offline com digest importado (geração em script com `PLN-KEY-OFFLINE`). |
| Testes | T-DIG-007, T-DIG-010 |

### FR-043 — Hash estrutural
| Prioridade | MVP-M |
|---|---|
| Descrição | Hash SHA-256 da forma canônica ([12 §6](12-schema-digest.md)). |
| Aceite | AC-043.1 mesmo schema em dois bancos diferentes → mesmo hash. AC-043.2 qualquer mudança estrutural listada → hash diferente. |
| Testes | T-DIG-003, T-DIG-004 |

### FR-044 — Snapshots no projeto
| Prioridade | V1-M |
|---|---|
| Descrição | Armazenar até 10 snapshots com rótulo; ativar snapshot. |
| Aceite | AC-044.1 projeto com 3 snapshots reabre com os 3 e o ativo correto. |
| Testes | T-PRJ-001 |

### FR-045 — Comparar digests
| Prioridade | V1-M |
|---|---|
| Descrição | Diff com severidade e candidatos de renomeação. |
| Aceite | AC-045.1 diff entre fixtures v1/v2 do schema `loja` produz exatamente as mudanças esperadas. |
| Testes | T-DIG-005, T-DIG-006 |

### FR-046 — Preservar mapeamentos após mudanças
| Prioridade | V1-M |
|---|---|
| Descrição | Migração de bindings conforme [12 §9](12-schema-digest.md). |
| Aceite | AC-046.1 coluna renomeada vira `Orphaned` + proposta de renomeação; aceitar a proposta reatribui o binding. AC-046.2 nenhum binding é descartado sem ação do usuário. |
| Testes | T-DIG-030, T-E2E-004 |

### FR-047 — Opções de captura
| Prioridade | V1-S |
|---|---|
| Descrição | Incluir/excluir views, rotinas, triggers, comentários; filtros por namespace e glob. |
| Aceite | AC-047.1 filtro `CAD_*` captura só tabelas com o prefixo; FKs externas viram aviso `DIG-FK-EXTERNAL`. |
| Testes | T-PRV-<X>-002 |

## E. Catálogo (FR-050 – FR-059)

### FR-050 — Templates escalares
| Atributo | Especificação |
|---|---|
| Descrição | CRUD de templates com tipo lógico, valores ponderados, tags, locale. |
| Prioridade | MVP-M |
| Regras | Código `^[A-Z][A-Z0-9_]{1,63}$`; valores únicos; peso > 0. |
| Aceite | AC-050.1 template criado e 1.000 valores colados aparecem na grade. AC-050.2 valor duplicado é ignorado com aviso. |
| Testes | T-CAT-004, T-UI-FLOW-002 |

### FR-051 — Aliases
| Prioridade | MVP-M |
|---|---|
| Descrição | N aliases por template ou campo, com peso e contexto de tabela. |
| Aceite | AC-051.1 alias `ZAP` em `TELEFONE` faz a coluna `ZAP` ser sugerida como TELEFONE com confiança High. |
| Testes | T-MAP-003, T-MAP-005 |

### FR-052 — Valores ponderados
| Prioridade | MVP-M |
|---|---|
| Descrição | Seleção ponderada determinística. |
| Aceite | AC-052.1 pesos 3:1 produzem proporção 75%±1% em 10^5 linhas. |
| Testes | T-GEN-LIB-catalog.template |

### FR-053 — Templates compostos
| Prioridade | V1-M (MVP-S: estrutura e geração; UI simplificada) |
|---|---|
| Descrição | Campos correlacionados; registros JSON; slots por linha. |
| Aceite | AC-053.1 cep/cidade/uf da mesma linha vêm sempre do mesmo registro. AC-053.2 dois slots na mesma linha usam registros independentes. |
| Testes | T-GEN-COMP-001, T-MAP-006 |

### FR-054 — Importação por script SQL (staging)
| Atributo | Especificação |
|---|---|
| Descrição | Executar script em staging isolado, analisar, pré-visualizar e mesclar. |
| Prioridade | MVP-M |
| Regras | [13 §7](13-template-catalog.md), ADR-016. |
| Erros | `CAT-STG-*`, `CAT-IMP-*`. |
| Aceite | AC-054.1 script de [examples/catalog-import-example.sql](examples/catalog-import-example.sql) importa sem erros. AC-054.2 script contendo `DROP TABLE template` é rejeitado sem alterar o catálogo. AC-054.3 merge falho deixa o catálogo idêntico ao anterior. |
| Testes | T-CAT-003, T-CAT-004, T-CAT-005, T-SEC-009 |

### FR-055 — Packs JSON
| Prioridade | V1-S |
|---|---|
| Descrição | Exportar/importar packs `template-pack-1.0`. |
| Aceite | AC-055.1 exportar e reimportar em catálogo vazio reproduz `content_hash` idênticos. |
| Testes | T-CAT-008 |

### FR-056 — Pack embutido pt-BR
| Prioridade | MVP-S (subconjunto), V1-M (completo) |
|---|---|
| Descrição | `CORE_PTBR` instalado na primeira execução via staging. |
| Aceite | AC-056.1 catálogo novo contém os templates de [09 §6](09-internal-database.md). AC-056.2 nenhum dado do pack é cópia de dataset com licença incompatível (revisão registrada em PA-009). |
| Testes | T-CAT-010 |

### FR-057 — Backup e restauração do catálogo
| Prioridade | V1-M |
|---|---|
| Descrição | Backup manual/automático, restauração, verificação de integridade. |
| Aceite | AC-057.1 restaurar backup retorna o catálogo ao estado do backup e o app continua funcional. |
| Testes | T-CAT-006, T-CAT-007 |

### FR-058 — Proteção de itens embutidos
| Prioridade | MVP-M |
|---|---|
| Descrição | Embutidos não editáveis estruturalmente/excluíveis; clonar para personalizar. |
| Aceite | AC-058.1 excluir template embutido falha com `CAT-BUILTIN-READONLY`. |
| Testes | T-CAT-002 |

### FR-059 — Presets de gerador
| Prioridade | V1-M |
|---|---|
| Descrição | Configurações nomeadas de geradores mapeáveis como templates (ex.: `CPF`, `DATA_NASC_ADULTO`). |
| Aceite | AC-059.1 coluna `cpf` é sugerida com o preset `CPF` (br.cpf). |
| Testes | T-MAP-003 |

## F. Mapeamento (FR-060 – FR-066)

### FR-060 — Binding estrutural
| Prioridade | MVP-M |
|---|---|
| Descrição | PK/identity, FKs, colunas geradas e UUIDs recebem binding estrutural automático. |
| Aceite | AC-060.1 no schema `loja`, 100% das colunas PK/FK/geradas recebem binding Confirmed correto. |
| Testes | T-MAP-002 |

### FR-061 — Sugestão semântica
| Prioridade | MVP-M |
|---|---|
| Descrição | Pontuação determinística ([14](14-semantic-mapping.md)). |
| Aceite | AC-061.1 corpus: ≥ 70% High, precisão High ≥ 95%. AC-061.2 mesma entrada → mesma ordem de candidatos. |
| Testes | T-MAP-CORPUS, T-MAP-004 |

### FR-062 — Confiança e ambiguidade
| Prioridade | MVP-M |
|---|---|
| Descrição | Faixas de confiança heurísticas; candidatos próximos marcados ambíguos. |
| Regras | Nunca apresentar score como probabilidade. |
| Aceite | AC-062.1 coluna `doc` com aliases em CPF e CNPJ é marcada ambígua e não é aplicada automaticamente. |
| Testes | T-MAP-005 |

### FR-063 — Ajuste manual
| Prioridade | MVP-M |
|---|---|
| Descrição | Escolher gerador/template/parâmetros por coluna; desfazer/refazer. |
| Aceite | AC-063.1 binding manual nunca é sobrescrito por "Reaplicar sugestões". |
| Testes | T-MAP-007, T-UI-FLOW-001 |

### FR-064 — Persistir e reutilizar regras
| Prioridade | V1-M |
|---|---|
| Descrição | Feedback de aceite/rejeição e regras aprendidas (opt-in). |
| Aceite | AC-064.1 após "Lembrar para outros projetos" em `fone_res → TELEFONE`, novo projeto sugere com origem Rule. |
| Testes | T-MAP-007 |

### FR-065 — Diagnóstico de mapeamentos
| Prioridade | V1-M |
|---|---|
| Descrição | Lista `MAP-*` por projeto com navegação. |
| Aceite | AC-065.1 coluna NOT NULL sem binding aparece como `MAP-UNMAPPED` e bloqueia a geração da tabela. |
| Testes | T-VAL-001..040 |

### FR-066 — Mapeamento em lote
| Prioridade | MVP-S |
|---|---|
| Descrição | "Mapear tudo automaticamente" no projeto. |
| Aceite | AC-066.1 aplicado ao schema `loja`, deixa apenas colunas Medium/Low/None pendentes. |
| Testes | T-MAP-CORPUS, T-E2E-<X>-001 |

## G. Geradores (FR-070 – FR-079)

### FR-070 — Biblioteca de geradores
| Prioridade | MVP-M (core.*, catalog.*, rel.*), V1-M (br.*, lorem, uuid v7, switch) |
|---|---|
| Descrição | Geradores da tabela [15 §6](15-generation-engine.md). |
| Aceite | AC-070.1 cada gerador tem snapshot de 1.000 valores verificado. |
| Testes | T-GEN-LIB-<id>, T-GEN-DET-001 |

### FR-071 — Parâmetros validados e UI gerada
| Prioridade | MVP-M |
|---|---|
| Descrição | Descritores de parâmetros geram o formulário; validação `GEN-PARAM-*`; preview ao vivo. |
| Aceite | AC-071.1 `min > max` mostra erro inline e impede salvar. |
| Testes | T-GEN-LIB-<id> |

### FR-072 — Nulos controlados
| Prioridade | MVP-M |
|---|---|
| Descrição | `NullRate` por coluna e por FK. |
| Aceite | AC-072.1 taxa 0,2 resulta em 20%±1% nulos em 10^5 linhas. AC-072.2 NullRate>0 em NOT NULL é bloqueado (`VAL-NULL-NOTNULL`). |
| Testes | T-GEN-NULL-001, T-VAL-001..040 |

### FR-073 — Composição
| Prioridade | V1-S |
|---|---|
| Descrição | `core.weighted` e `core.switch` com filhos. |
| Aceite | AC-073.1 switch por `@tipo='PJ'` gera CNPJ e senão CPF. |
| Testes | T-GEN-LIB-core.switch |

### FR-074 — Expressões e colunas derivadas
| Prioridade | MVP-S (core.format), V1-M (core.expr completo) |
|---|---|
| Descrição | MDS-Expr ([15 §9](15-generation-engine.md)). |
| Aceite | AC-074.1 e-mail derivado do nome da mesma linha. AC-074.2 erro de sintaxe indica posição. AC-074.3 dependência circular bloqueada (`PLN-EXPR-CYCLE`). |
| Testes | T-GEN-EXPR-001..003 |

### FR-075 — Unicidade
| Prioridade | MVP-M |
|---|---|
| Descrição | Estratégias [15 §8](15-generation-engine.md) sem tentativas ilimitadas. |
| Aceite | AC-075.1 1 M CPFs únicos sem colisão e memória O(1). AC-075.2 capacidade insuficiente bloqueia no planejamento. AC-075.3 UNIQUE composta respeitada. |
| Testes | T-GEN-UQ-001..004 |

### FR-076 — Geradores brasileiros
| Prioridade | V1-M |
|---|---|
| Descrição | CPF, CNPJ, telefone por DDD, placa. |
| Aceite | AC-076.1 100% dos CPF/CNPJ com DVs válidos. |
| Testes | T-GEN-BR-001 |

### FR-077 — Campos de template composto
| Prioridade | V1-M |
|---|---|
| Descrição | `catalog.template.field` com slots. |
| Aceite | Ver AC-053.1/053.2. |
| Testes | T-GEN-COMP-001 |

### FR-078 — Lookup em outra tabela / consulta
| Prioridade | V1-S |
|---|---|
| Descrição | `rel.lookup` (gerada ou existente) e fonte `Query` para FKs. |
| Aceite | AC-078.1 lookup `Existing` usa apenas valores lidos do banco de referência, em ordem determinística. |
| Testes | T-REL-010 |

### FR-079 — Versionamento de geradores
| Prioridade | MVP-M |
|---|---|
| Descrição | Versão gravada no binding; versões antigas mantidas; "Atualizar geradores" explícito. |
| Aceite | AC-079.1 projeto criado com versão 1 continua gerando saída idêntica após o app incluir versão 2 do gerador. |
| Testes | T-GEN-LIB-<id>, T-GEN-DET-002 |

## H. Geração relacional (FR-080 – FR-087)

### FR-080 — Vínculo automático de FKs
| Prioridade | MVP-M |
|---|---|
| Descrição | Toda FK vira `rel.fk` com fonte `Generated` (pai incluído) ou pedido de decisão (pai excluído). |
| Aceite | AC-080.1 todas as FKs do schema `loja` vinculadas sem ação manual. |
| Testes | T-MAP-002, T-REL-001 |

### FR-081 — Fontes de referência
| Prioridade | MVP-M (Generated, Fixed), V1-M (Existing, Query, Dataset) |
|---|---|
| Descrição | [16 §8](16-relational-generation.md). |
| Aceite | AC-081.1 cada fonte produz FKs válidas no destino de teste. |
| Testes | T-REL-010, T-E2E-<X>-001 |

### FR-082 — Cardinalidade e distribuição
| Prioridade | MVP-S (ChildDriven Uniform), V1-M (Zipf, ParentDriven, 1:1) |
|---|---|
| Descrição | [16 §6](16-relational-generation.md). |
| Aceite | AC-082.1 ParentDriven Range(1..5): todo pai tem 1–5 filhos. AC-082.2 1:1 nunca repete pai. |
| Testes | T-REL-008, T-REL-009 |

### FR-083 — FKs compostas
| Prioridade | MVP-M |
|---|---|
| Aceite | AC-083.1 `entrega(pedido_id,item_seq)` sempre referencia tupla existente de `item_pedido`. |
| Testes | T-REL-007 |

### FR-084 — Autorreferência e ciclos
| Prioridade | MVP-M (NullThenUpdate, self nullable), V1-M (Deferred, DisableChecks, self NOT NULL) |
|---|---|
| Aceite | AC-084.1 ciclos anuláveis geram dados válidos nos 4 SGBDs. AC-084.2 ciclo não quebrável é bloqueado com explicação e sugestões. |
| Testes | T-REL-002..006, T-REL-SELF-NN-<X> |

### FR-085 — Estratégia de chaves
| Prioridade | MVP-M (Explicit), V1-M (Captured, NaturalKeyLookup) |
|---|---|
| Descrição | ADR-008, [16 §5](16-relational-generation.md). |
| Aceite | AC-085.1 chaves explícitas acima de MAX; sequence ressincronizada (INSERT posterior sem chave funciona). AC-085.2 overflow bloqueado. |
| Testes | T-REL-012, T-REL-013, T-PRV-<X>-010 |

### FR-086 — Plano de dependências e bloqueios
| Prioridade | MVP-M |
|---|---|
| Descrição | `GenerationPlan` com passos e diagnósticos `PLN-*`. |
| Aceite | AC-086.1 plano do schema `loja` igual ao snapshot. AC-086.2 todos os `PLN-*` Error impedem execução. |
| Testes | T-REL-PLAN-001, T-REL-005 |

### FR-087 — Integridade entre origem e destino
| Prioridade | V1-M |
|---|---|
| Descrição | [16 §9](16-relational-generation.md). |
| Aceite | AC-087.1 chaves `Existing` ausentes no destino bloqueiam a execução direta. |
| Testes | T-REL-011 |

## I. Geração e saída (FR-090 – FR-101)

### FR-090 — Quantidades por tabela e estimativas
| Prioridade | MVP-M |
|---|---|
| Descrição | Linhas por tabela no perfil; estimativas de linhas/bytes/tempo. |
| Aceite | AC-090.1 estimativa de linhas é exata; bytes com erro ≤ 15% em B-OUT-001. |
| Testes | B-OUT-001 |

### FR-091 — Preview
| Prioridade | MVP-M |
|---|---|
| Descrição | N primeiras linhas por tabela, idênticas às da geração. |
| Aceite | AC-091.1 linhas do preview iguais às primeiras linhas do script. |
| Testes | T-GEN-DET-002 |

### FR-092 — Validação pré-voo
| Prioridade | MVP-M (L1–L3), V1-M (L4) |
|---|---|
| Descrição | [17](17-constraint-validation.md). |
| Aceite | AC-092.1 ≥ 36/40 casos detectados sem L4; 40/40 com L4. AC-092.2 schema `loja` sem falso positivo. |
| Testes | T-VAL-001..040, T-VAL-LOJA, T-VAL-TRIAL-001/002 |

### FR-093 — Exportar script SQL
| Prioridade | MVP-M (SQLite, PG, FB), V1-M (MySQL) |
|---|---|
| Descrição | [18](18-sql-generation.md). |
| Aceite | AC-093.1 script executado em banco limpo (via cliente oficial: `sqlite3`, `psql`, `mysql`, `isql`) insere todas as linhas sem erro. |
| Testes | T-E2E-<X>-001, T-OUT-001, T-OUT-ROUNDTRIP-<X> |

### FR-094 — Segmentação e manifesto
| Prioridade | V1-M |
|---|---|
| Aceite | AC-094.1 nenhum segmento corta um comando; executar segmentos em ordem equivale ao script único. |
| Testes | T-OUT-002, T-OUT-003 |

### FR-095 — Inserção direta
| Prioridade | V1-M (no MVP apenas o `IBulkInserter` existe e é testado por contrato; sem fluxo de UI) |
|---|---|
| Descrição | [19](19-direct-database-execution.md). |
| Aceite | AC-095.1 sem autorização válida não executa. AC-095.2 contagens no destino = plano. |
| Testes | T-DIR-<X>-001, T-SEC-001, T-E2E-<X>-002 |

### FR-096 — Progresso e cancelamento
| Prioridade | MVP-M |
|---|---|
| Aceite | AC-096.1 progresso atualiza ≥ 1×/s; cancelar finaliza em ≤ 2 s após o batch corrente com estado consistente. |
| Testes | T-DIR-003, T-UI-PERF-002 |

### FR-097 — Determinismo por seed
| Prioridade | MVP-M |
|---|---|
| Aceite | AC-097.1 duas execuções com a mesma seed geram scripts com hash idêntico (ignorando linha de data). AC-097.2 relatório informa nível de reprodutibilidade. |
| Testes | T-GEN-DET-001..003, T-E2E-003 |

### FR-098 — Relatório de execução
| Prioridade | MVP-S, V1-M |
|---|---|
| Aceite | AC-098.1 relatório contém todas as seções de [19 §8](19-direct-database-execution.md). |
| Testes | T-DIR-002 |

### FR-099 — Script de limpeza
| Prioridade | V1-M |
|---|---|
| Aceite | AC-099.1 executar limpeza após execução completa deixa as tabelas com as mesmas contagens de antes. |
| Testes | T-OUT-004, T-E2E-<X>-002 |

### FR-100 — CSV e JSON Lines
| Prioridade | V1-S |
|---|---|
| Aceite | AC-100.1 CSV carregado via `COPY`/`LOAD DATA` na ordem do manifesto reproduz os dados. |
| Testes | T-OUT-005 |

### FR-101 — Ressincronização de sequences
| Prioridade | MVP-M |
|---|---|
| Aceite | Ver AC-085.1. |
| Testes | T-OUT-006, T-PRV-<X>-010 |

## J. Projetos (FR-110 – FR-116)

### FR-110 — Criar/abrir/salvar projeto
| Prioridade | MVP-M |
|---|---|
| Descrição | `.mdsproj` ([22](22-project-file-format.md)). |
| Aceite | AC-110.1 salvar/reabrir preserva tudo (round-trip). AC-110.2 falha durante gravação não corrompe o arquivo anterior. |
| Testes | T-PRJ-001, T-PRJ-006 |

### FR-111 — Perfis de geração
| Prioridade | MVP-M (1 perfil), V1-M (N perfis) |
|---|---|
| Descrição | Seed, instante de referência, quantidades, relações, saída, validação por perfil. |
| Aceite | AC-111.1 alternar perfil altera quantidades sem tocar bindings. |
| Testes | T-PRJ-001 |

### FR-112 — Portabilidade sem segredos
| Prioridade | MVP-M |
|---|---|
| Aceite | AC-112.1 nenhum segredo/host/usuário no arquivo. AC-112.2 exportação sanitizada remove IDs locais e histórico. |
| Testes | T-PRJ-003, T-SEC-002 |

### FR-113 — Versionamento do formato
| Prioridade | V1-M |
|---|---|
| Aceite | AC-113.1 fixture 1.0 abre em versão 1.1 com migração e backup. |
| Testes | T-PRJ-004 |

### FR-114 — Histórico de execuções
| Prioridade | V1-M |
|---|---|
| Aceite | AC-114.1 cada execução aparece em S17 e no `runs` do projeto. AC-114.2 execução interrompida é marcada `interrupted` na próxima abertura. |
| Testes | T-DIR-002 |

### FR-115 — Recentes, autosave e recuperação
| Prioridade | V1-S |
|---|---|
| Aceite | AC-115.1 alterações não salvas recuperáveis após encerramento abrupto. |
| Testes | T-PRJ-006 |

### FR-116 — Dependências de catálogo
| Prioridade | V1-M |
|---|---|
| Aceite | AC-116.1 abrir projeto com template ausente marca bindings `NeedsReview` e informa qual pack importar. |
| Testes | T-PRJ-007 |

## K. Aplicação (FR-120 – FR-122)

### FR-120 — Configurações
| Prioridade | V1-S |
|---|---|
| Descrição | Seções de [21 S18](21-user-interface-ux.md). |
| Aceite | AC-120.1 alterações persistem após reinício. |
| Testes | T-UI-FLOW-001 |

### FR-121 — Logs e pacote de diagnóstico
| Prioridade | V1-S |
|---|---|
| Aceite | AC-121.1 pacote de diagnóstico não contém segredos. |
| Testes | T-SEC-004 |

### FR-122 — Atalhos e paleta de comandos
| Prioridade | V1-S |
|---|---|
| Aceite | AC-122.1 todos os comandos de [21 §6](21-user-interface-ux.md) funcionam. |
| Testes | T-UI-A11Y-003 |

## L. Requisitos V2 (visão; especificação detalhada a produzir antes do início do V2)

| ID | Requisito | Origem (lista premium do briefing) |
|---|---|---|
| FR-200 | Designer visual de cenários (fluxo de tabelas/volumes/regras) | Premium 1 |
| FR-201 | Regras condicionais declarativas entre colunas (além de `core.switch`) | Premium 4 |
| FR-202 | Grafo de relacionamentos interativo (S19) | Premium 13 |
| FR-203 | Plugins externos (ADR-009) | Premium 20 |
| FR-204 | CLI `mds` (generate, digest, validate) reutilizando Application | Premium 21 |
| FR-205 | Perfil estatístico de dados existentes (agregados apenas) | Premium 22 |
| FR-206 | Anonimização/pseudonimização determinística de cópias de dados (escopo restrito, ver 27) | Premium 23 |
| FR-207 | Perfis de carga para testes de desempenho (volumes por fase, presets) | Premium 24 |
| FR-208 | Relatório de cobertura de cenários (distribuições geradas vs. alvo, enums cobertos, nulos) | Premium 25 |
| FR-209 | Pacotes temáticos adicionais (varejo, saúde fictícia, educação) e `.mdspkg` | Premium 12 |
| FR-210 | Coerência de slot entre tabelas (herança de endereço do pai) | 13 §5.3 |
| FR-211 | MariaDB como SGBD oficial | 10 §4 |
| FR-212 | Geração de arrays/JSON estruturado | 10 §5 |
| FR-213 | Paralelismo entre tabelas independentes | 24 §4 |
| FR-214 | Ferramentas de auditoria (registro assinado de execuções) | Premium 19 |
