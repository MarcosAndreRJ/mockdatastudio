# 20 — Editor SQL Integrado

| Campo | Valor |
|---|---|
| Objetivo | Especificar o editor SQL: highlight, autocomplete, abas, divisão de scripts, execução, resultados, histórico, exportação, cancelamento e proteções. |
| Escopo | `MockDataStudio.SqlTooling` (lexer, splitter, classificador, completion) + `QueryExecutionService` + UI (AvalonEdit). |
| Relacionados | ADR-007, [C09 §2](contracts/C09-projects-and-application.md), [21 §S04](21-user-interface-ux.md), [23](23-security.md) |
| Requisitos | FR-020..FR-031 |

## 1. Escopo de comandos

| Classe | Suporte | Proteção |
|---|---|---|
| SELECT (e CTEs de leitura) | Completo, com paginação | Nenhuma |
| INSERT/UPDATE/DELETE/MERGE | Completo | Guarda de destrutivos (§8) |
| DDL (CREATE/ALTER/DROP/TRUNCATE/RECREATE/COMMENT) | **Executável**, sem assistência específica | Sempre exige confirmação; bloqueado em Production+ReadOnly |
| Transação (BEGIN/COMMIT/ROLLBACK/SAVEPOINT) | Interpretado pela sessão (sincroniza o estado da barra de transação) | — |
| Sessão (SET, USE, PRAGMA) | Executável | `PRAGMA foreign_keys`/`SET FOREIGN_KEY_CHECKS` → aviso |
| Procedimentos (CALL, EXECUTE PROCEDURE, EXECUTE BLOCK, DO) | Executável | Tratado como potencialmente destrutivo |
| Diretivas de cliente (`SET TERM`, `DELIMITER`) | Interpretadas pelo splitter, não enviadas | — |

Justificativa para permitir DDL: o usuário frequentemente precisa criar/ajustar tabelas de teste; bloquear seria artificial. A segurança vem de ambiente + confirmação, não de restrição sintática.

## 2. Abas e sessões

- Cada aba = documento + `QuerySession` (conexão própria) vinculada a **um perfil** exibido na barra da aba com cor do ambiente.
- Trocar o perfil de uma aba com transação pendente exige commit/rollback primeiro.
- Abas não salvas são preservadas em `%LOCALAPPDATA%\MockDataStudio\editor-recovery\` a cada 30 s (recuperação após falha), sem senha/connection string.
- Arquivos `.sql` abertos/salvos com detecção de encoding (UTF-8 com/sem BOM, Windows-1252 fallback com aviso).

## 3. Realce de sintaxe

- AvalonEdit com definições `.xshd` por dialeto (`Sql.Common.xshd` + palavras-chave específicas: PG `$$`, `RETURNING`, `ILIKE`; MySQL crases, `LIMIT`; FB `SET TERM`, `EXECUTE BLOCK`, `RDB$`; SQLite `PRAGMA`, `GLOB`).
- Destaque de terminadores de comando e do **comando corrente** (sob o cursor) com faixa lateral.
- Marcação de erro (sublinhado) na posição retornada pelo SGBD quando disponível (PG `Position`, FB linha/coluna da mensagem, MySQL "near '...' at line n").

## 4. Lexer e divisão de scripts (ADR-007)

Tokens: `Whitespace, LineComment(--, # MySQL), BlockComment(/* */; MySQL /*! */ tratado como código), String('..'), NationalString(N'..'), EscapeString(E'..' PG), DollarQuoted($tag$..$tag$ PG), QuotedIdentifier("..", `..`), BracketIdentifier — não aplicável, Number, Word, Operator, Terminator, ClientDirective`.

Regras do splitter:
1. Terminador atual (padrão `;`) fora de strings/comentários/dollar-quotes encerra o comando.
2. `SET TERM <novo> <antigo>` (Firebird, na própria linha, terminado pelo terminador atual) altera o terminador; não é enviado ao servidor.
3. `DELIMITER <novo>` (MySQL, linha inteira) altera o terminador; não é enviado.
4. Firebird sem `SET TERM`: se o comando começa com `CREATE [OR ALTER] {PROCEDURE|TRIGGER|FUNCTION|PACKAGE [BODY]}`, `RECREATE ...`, `ALTER {PROCEDURE|TRIGGER|FUNCTION}` ou `EXECUTE BLOCK`, o splitter rastreia `BEGIN`/`END` (e `CASE ... END`) e só aceita `;` como terminador quando a profundidade volta a 0 após o primeiro `BEGIN`.
5. MySQL `CREATE PROCEDURE/FUNCTION/TRIGGER/EVENT` sem `DELIMITER`: mesma regra de `BEGIN/END` (com `IF..END IF`, `LOOP..END LOOP`, `WHILE..END WHILE`, `REPEAT..END REPEAT`, `CASE..END CASE`).
6. PostgreSQL: corpos em `$$` já protegidos pelo lexer; `BEGIN ATOMIC ... END` (PG14+ SQL-standard bodies) rastreado.
7. Comandos vazios (só comentários/espaços) são descartados.
8. Cada comando mantém `StartOffset`/`Length` (para destacar e reportar erros).

Corpus de testes `tests/fixtures/sql-split/<dialeto>/*.sql` com saída esperada (T-SQL-SPLIT-*), incluindo os casos que quebram split ingênuo.

## 5. Classificação de comandos

`StatementKind` pela primeira palavra-chave significativa; regras especiais:
- `WITH ...` → procura após o CTE a palavra principal (`SELECT/INSERT/UPDATE/DELETE/MERGE`); PG permite DML dentro de CTEs (`WITH x AS (DELETE ...)`) → se qualquer `INSERT|UPDATE|DELETE|MERGE` aparecer em nível de token fora de strings → classificado pelo **mais destrutivo**.
- `SELECT ... INTO` (PG cria tabela; FB PSQL) → `Ddl`/destrutivo.
- `SELECT ... FOR UPDATE` → `Select` com aviso de bloqueio.
- `HasWhereClause`: existe `WHERE` no nível de parênteses 0 do comando principal.
- `IsDestructive`: UPDATE/DELETE sem WHERE, TRUNCATE, DROP, ALTER, RECREATE, `DELETE`/`UPDATE` com `WHERE` trivialmente verdadeiro (`1=1`, `true`, `0=0`), MERGE, EXECUTE/CALL, `SET FOREIGN_KEY_CHECKS=0`, `PRAGMA foreign_keys=OFF`.

## 6. Autocomplete

### 6.1 Fontes
- Palavras-chave do dialeto + funções comuns (listas embutidas por dialeto).
- Objetos: cache de metadados por perfil (`ICompletionSchemaSource`) carregado **lazy**: nomes de tabelas na abertura da aba (consulta leve), colunas por tabela sob demanda (cache 10 min, invalidado por "Atualizar").
- Snippets: `sel` → `SELECT * FROM |`, `ins <tabela>` → INSERT com todas as colunas inseríveis, `upd`, `del` (com `WHERE` obrigatório no snippet).

### 6.2 Contexto (heurística por tokens do comando corrente)

| Contexto | Sugere |
|---|---|
| Após `FROM`, `JOIN`, `INTO`, `UPDATE`, `TABLE` | Namespaces, tabelas, views |
| `ns.` | Tabelas do namespace |
| `alias.` ou `tabela.` | Colunas da tabela resolvida |
| Após `SELECT`, `WHERE`, `AND`, `OR`, `ON`, `SET`, `ORDER BY`, `GROUP BY` | Colunas das tabelas em escopo (resolvidas a partir de `FROM/JOIN` do comando corrente, incluindo aliases `t`, `AS t`) + funções + palavras-chave |
| Dentro de lista de colunas de `INSERT INTO t (` | Colunas de `t` |
| Outros | Palavras-chave |

Resolução de aliases: varredura dos tokens do comando corrente por padrões `FROM|JOIN <nome>[ [AS] <alias>]` e `UPDATE <nome> [alias]`, com suporte a vírgulas em `FROM a x, b y`. Subconsultas: escopo simplificado (aliases de todos os níveis visíveis) — limitação documentada.

Disparo: `Ctrl+Espaço`, digitação após `.`, ou após 2 caracteres de identificador (configurável). Ordenação: prefixo exato > contém > fuzzy; prioridade por tipo de contexto. Latência alvo ≤ 50 ms (NFR-PERF-007).

Inserção respeita quoting: identificadores que exigem aspas são inseridos citados.

## 7. Execução e transações

| Comando | Atalho | Efeito |
|---|---|---|
| Executar comando atual / seleção | `Ctrl+Enter` | Seleção, se houver; senão comando sob o cursor |
| Executar script | `F5` | Todos os comandos em ordem; para no primeiro erro (configurável "continuar em erro") |
| Executar até o cursor | `Shift+F5` | — |
| Cancelar | `Esc` (com foco no painel) / botão | `DbCommand.Cancel` + timeout 5 s → fecha conexão |
| Commit / Rollback | `Ctrl+Shift+C` / `Ctrl+Shift+R` | Somente no modo Manual |

**Modo de transação** (`TransactionMode`): `Manual` padrão para Test/Staging/Production/Unknown; `AutoCommit` padrão para Development. Em Manual, o primeiro comando de escrita abre transação; a barra de status mostra "Transação pendente: N comandos" em destaque; fechar a aba/app com transação pendente exige escolher Commit/Rollback (padrão do botão: Rollback).

Execução em background (`async`), UI responsiva; cronômetro ao vivo.

## 8. Proteções (guarda de execução)

`IExecutionGuard.EvaluateQuery(profile, statement, mode)`:

| Ambiente / Perfil | SELECT | DML com WHERE | Destrutivo (§5) | DDL |
|---|---|---|---|---|
| Development | Allow | Allow | Confirm | Confirm |
| Test / Staging | Allow | Allow (Manual) | Confirm | Confirm |
| Production / Unknown (`IsReadOnly=false`) | Allow | Confirm | TypedConfirm | TypedConfirm |
| Qualquer com `IsReadOnly=true` | Allow | **Block** | **Block** | **Block** |

- Confirm: diálogo com o comando, perfil (cor/ambiente) e estimativa quando possível (`UPDATE/DELETE` → oferece "Contar linhas afetadas" executando `SELECT COUNT(*)` com o mesmo WHERE — derivado por reescrita simples só quando o comando é de tabela única; senão opção indisponível).
- TypedConfirm: digitar o **nome do banco** exibido.
- Somente leitura reforçada no servidor quando possível: PG `SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY`; MySQL `SET SESSION TRANSACTION READ ONLY`; Firebird transação `read`; SQLite `Mode=ReadOnly` → defesa em profundidade além da guarda do app.
- Execução de script com múltiplos destrutivos: confirmação única listando todos (não N diálogos).

## 9. Resultados

- Grade virtualizada (`DataGrid` com `VirtualizingStackPanel.VirtualizationMode=Recycling`), primeira página 500 linhas; "Carregar mais" (+500) e "Carregar tudo" (com limite configurável de 100 k e aviso de memória).
- Múltiplos result sets → sub-abas "Resultado 1..n"; DML → mensagem "N linhas afetadas".
- Tipos: datas na cultura do usuário (ISO opcional), BLOB → "(binário, N bytes)" com visualizador hex/texto, textos longos truncados na célula com visualizador.
- Exportação do resultado (página carregada ou reexecução completa em streaming): CSV, JSON Lines, **INSERTs** (dialeto da conexão ou escolhido), copiar como TSV.
- Filtro e ordenação **locais** sobre linhas carregadas (indicação clara "filtrando apenas linhas carregadas").

## 10. Histórico

Gravado em `query_history` (texto truncado em 64 KB, tipo, duração, linhas, sucesso, erro). Painel com busca e filtro por perfil; duplo clique insere no editor. Perfis Production: por padrão grava só metadados, não o SQL (configurável) — [09 §9](09-internal-database.md).

## 11. Erros

`IErrorTranslator` classifica; painel "Mensagens" mostra código do SGBD, mensagem original, posição e sugestão (ex.: violação de unicidade → mostra constraint). Erros de conexão marcam a sessão como desconectada com botão "Reconectar" (transação pendente é perdida — avisado).
