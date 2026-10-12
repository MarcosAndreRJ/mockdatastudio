# 30 — Problemas Técnicos Profundos, Riscos e Trade-offs

| Campo | Valor |
|---|---|
| Objetivo | Analisar em profundidade os problemas A–J do briefing e manter o registro de riscos. |
| Escopo | Produto inteiro. |
| Relacionados | Todos os documentos técnicos; [32](32-decisions-and-assumptions.md) |

Formato por problema: **Cenário · Riscos · Alternativas · Trade-offs · Solução recomendada · Limitações · Testes · Aceite**.

---

## A. Compatibilidade de metadados entre SGBDs

- **Cenário**: o mesmo conceito tem representações diferentes: namespaces (PG schemas, MySQL databases, nenhum no SQLite/FB≤5); identidade (AUTO_INCREMENT, IDENTITY ALWAYS/BY DEFAULT, serial, generator+trigger); tipos (afinidade SQLite, `tinyint(1)`, domínios FB, enums PG/MySQL); CHECK (FB guarda como trigger de sistema); comprimento em bytes vs. caracteres.
- **Riscos**: perder informação necessária à geração (ex.: não saber que a PK é preenchida por trigger) ou forçar um denominador comum pobre.
- **Alternativas**: (1) modelo mínimo comum; (2) modelo por SGBD; (3) **modelo canônico rico + extras tipados por chave registrada**.
- **Trade-offs**: (1) perde nuance; (2) multiplica lógica no motor; (3) exige disciplina de registro de extras e um `ITypeMapper` por provider.
- **Solução**: (3) — ADR-006; `LogicalType` + tipo nativo preservado; `IdentityKind` enumera todas as formas; namespace virtual `PUBLIC` no FB; heurística determinística `tsq` ([11 §4.4](11-schema-introspection.md)); `LengthUnit`/charset para validação em bytes.
- **Limitações**: semântica de triggers/procedures não é interpretada; tipos exóticos (arrays PG, geometria, `set` MySQL) mapeados como `Other` com aviso; FB6 schemas pendentes (PA-007).
- **Testes**: T-PRV-<X>-002..005, fixtures `types-*`, `firebird-legacy`; T-DIG-002.
- **Aceite**: digest `loja` dos 4 SGBDs gera o mesmo **plano lógico** (mesma ordem de tabelas, mesmas estratégias, salvo diferenças documentadas de capacidade) — verificado por T-REL-PLAN-001 por SGBD.

## B. Geração de chaves

- **Cenário**: filhos precisam das chaves dos pais; chaves geradas pelo banco só existem após o INSERT; scripts offline não podem capturá-las; MySQL multi-linha não garante IDs consecutivos em modo interleaved [V].
- **Riscos**: FKs inválidas; dependência de round-trips lentos; scripts não portáveis; colisão com dados inseridos concorrentemente.
- **Alternativas**: Explicit; Captured (RETURNING/LAST_INSERT_ID); lookup por chave natural; variáveis de sessão; desativar identity temporariamente.
- **Trade-offs**: Explicit é rápido/determinístico mas exige leitura de MAX, override de identity e resync; Captured é exato mas lento (linha a linha) e só para inserção direta; lookup exige unicidade e subconsultas (lento em volume); variáveis de sessão são frágeis e ilegíveis.
- **Solução**: ADR-008 — Explicit padrão; Captured quando inevitável (ALWAYS sem override, trigger que sobrescreve); NaturalKeyLookup opcional; reverificação da faixa imediatamente antes da execução; resync.
- **Limitações**: concorrência entre o planejamento e a execução de **script** não é detectável pelo app (guardas só no PG; comentário nos demais); Captured em MySQL é linha a linha.
- **Testes**: T-REL-012/013, T-PRV-<X>-010, T-PRV-FB-KEY-*, T-DIR-004.
- **Aceite**: AC-085.1/2; nenhuma FK inválida em T-E2E-*.

## C. Integridade referencial (FKs compostas, ciclos, obrigatórias)

- **Cenário**: FKs compostas e sobrepostas; ciclos entre tabelas; autorreferências NOT NULL; pais fora do escopo.
- **Riscos**: plano impossível descoberto tarde; violações em massa; UPDATEs adiados gigantes.
- **Alternativas**: ordenação topológica pura; desativar todas as FKs; **SCC + feedback edge set com estratégias por aresta**; inserir pais "sentinela".
- **Trade-offs**: desativar FKs é simples mas perigoso e nem sempre possível (FB, PG sem superusuário); SCC exige mais lógica mas é seguro e explicável; sentinelas poluem dados.
- **Solução**: [16 §2–7](16-relational-generation.md): SCC, NullThenUpdate/Deferred/DisableChecks (opt-in), bloqueio explícito quando não quebrável, binding por FK (tupla), resolução de sobreposição.
- **Limitações**: busca exaustiva limitada a 12 arestas por componente (heurística acima); autorreferência NOT NULL depende do SGBD (a verificar); UPDATEs adiados dobram o I/O das tabelas do ciclo.
- **Testes**: T-REL-001..009, T-REL-SELF-NN-<X>.
- **Aceite**: AC-083.1, AC-084.1/2.

## D. Restrições de unicidade sem memória excessiva

- **Cenário**: CPF/e-mail/SKU únicos; UNIQUE compostas; milhões de linhas; valores existentes no destino.
- **Riscos**: laços de tentativa infinitos; memória O(n) com HashSet; falha após horas.
- **Alternativas**: tentativas + HashSet; Bloom filter; **bijeções (permutação de índice)**; sufixos.
- **Trade-offs**: HashSet é genérico mas O(n) e não paralelo; Bloom tem falsos positivos (rejeições desnecessárias) e não garante; bijeção é O(1) e paralela, mas só para domínios enumeráveis; sufixo altera o valor.
- **Solução**: hierarquia de 15 §8.2 com **verificação de capacidade no planejamento** e limite rígido de 16 tentativas por linha na estratégia residual.
- **Limitações**: unicidade composta bijetiva força distribuição uniforme dos componentes; valores existentes no destino só excluídos se carregados (≤ 5 M).
- **Testes**: T-GEN-UQ-001..004.
- **Aceite**: AC-075.1..3.

## E. Validação realista

- **Cenário**: CHECKs arbitrários, triggers que rejeitam/alteram linhas, limites por SGBD (bytes, datas), `sql_mode`.
- **Riscos**: falha na execução após gerar grande volume; falsa sensação de segurança.
- **Alternativas**: só validação estática; só tentativa real; **camadas L1–L4**.
- **Trade-offs**: estática é rápida e segura mas incompleta; real é completa mas tem efeitos colaterais (sequences, transações autônomas) e exige ambiente não-produtivo.
- **Solução**: [17](17-constraint-validation.md) — interpretação conservadora de CHECK, amostragem L3, L4 opt-in fora de Production com avisos de efeitos.
- **Limitações**: CHECKs com funções de usuário e triggers com lógica dependente de dados não são previstos sem L4; L4 avança sequences.
- **Testes**: T-VAL-001..040, T-VAL-TRIAL-*.
- **Aceite**: AC-092.1/2.

## F. Grandes volumes

- **Cenário**: milhões de linhas, scripts de GB, UI responsiva.
- **Riscos**: OOM; UI travada; arquivos impossíveis de abrir; transações gigantes.
- **Alternativas**: materializar tudo; **streaming com contrapressão**; geração por arquivos temporários por tabela.
- **Trade-offs**: streaming exige writer único e ordenação de chunks; ganho de paralelismo limitado pelo writer.
- **Solução**: [24](24-performance-scalability.md), KeyOracle O(1), segmentação, COMMIT periódico, COPY no PG.
- **Limitações**: unicidade por tentativas e Captured não são O(1) de memória; tabelas sequenciais.
- **Testes**: B-MEM-001, B-OUT-002, T-GEN-VOL-001, T-UI-PERF-002.
- **Aceite**: NFR-PERF-001/002/004/009.

## G. Segurança do editor SQL

- **Cenário**: comandos poderosos (DDL/DML) com várias abas e perfis abertos.
- **Riscos**: executar no banco errado; DELETE sem WHERE; transação esquecida aberta segurando locks em produção.
- **Alternativas**: bloquear DDL; confirmar tudo; **matriz por ambiente + somente leitura no servidor + transação manual**.
- **Trade-offs**: confirmar tudo gera fadiga e cliques automáticos; matriz exige configuração correta de ambiente (padrão Unknown=Production mitiga).
- **Solução**: [20 §7–8](20-sql-editor.md), [23 §5](23-security.md).
- **Limitações**: classificação heurística pode não reconhecer destrutividade em procedures (tratadas como destrutivas por padrão); usuário pode marcar produção como Development.
- **Testes**: T-SQL-CLASS-001, T-SQL-GUARD-001, T-SEC-005/006.
- **Aceite**: AC-028.1..3.

## H. Determinismo

- **Cenário**: reproduzir exatamente um conjunto de dados meses depois, em outra máquina, com paralelismo.
- **Riscos**: dependência de `System.Random`, ordem de threads, "agora", datasets mutáveis, versões de geradores, dados externos.
- **Alternativas**: RNG global sequencial; **RNG por contador** + versionamento; gravar os dados gerados.
- **Trade-offs**: gravar dados é exato mas enorme; RNG por contador exige disciplina (pureza) e congelamento de algoritmos.
- **Solução**: ADR-013, [15 §4](15-generation-engine.md) — seeds derivadas por escopo, `ReferenceInstant` fixo, versões de gerador no binding, `ContentHash` de templates, `KeyPool.Fingerprint`, níveis de reprodutibilidade no relatório.
- **Limitações**: fontes externas e "agora" quebram exatidão (informado); mudança de cultura não afeta (Invariant); fuso horário fixado no perfil.
- **Testes**: T-GEN-RNG-001, T-GEN-DET-001..003, T-E2E-003.
- **Aceite**: AC-097.1/2.

## I. Evolução de projetos

- **Cenário**: o schema muda (colunas renomeadas, tipos alterados, tabelas novas) após o usuário investir horas em mapeamentos.
- **Riscos**: perda silenciosa de configuração; aplicação de gerador incompatível.
- **Alternativas**: descartar e remapear; casar por posição; **identidade canônica + fingerprint + migração assistida**.
- **Trade-offs**: renomeações não são detectáveis com certeza → propostas, nunca automáticas.
- **Solução**: [12 §8–9](12-schema-digest.md) com estados `Active/NeedsReview/Orphaned` e propostas de renomeação.
- **Limitações**: renomeação + mudança de tipo simultâneas podem não gerar proposta.
- **Testes**: T-DIG-030, T-E2E-004.
- **Aceite**: AC-046.1/2.

## J. Origem e destino distintos

- **Cenário**: introspecção em homologação, chaves existentes lidas de um banco de referência, inserção em outro.
- **Riscos**: integridade falsa (FKs apontando para registros inexistentes no destino), sobretudo com FKs não aplicadas.
- **Alternativas**: proibir; avisar; **verificar chaves no destino**.
- **Trade-offs**: verificação custa consultas (lotes de 1.000) mas é a única garantia.
- **Solução**: [16 §9](16-relational-generation.md) — fingerprints, verificação padrão ligada, desligamento explícito registrado.
- **Limitações**: em script (destino desconhecido) só é possível emitir verificações/comentários.
- **Testes**: T-REL-010/011.
- **Aceite**: AC-087.1.

---

## Registro de riscos (vivo)

| ID | Risco | Prob. | Impacto | Mitigação | Responsável/Tarefa | Status |
|---|---|---|---|---|---|---|
| R-01 | AvalonEdit sem manutenção ativa / incompatibilidade futura | M | M | Encapsular; fork AvalonEditB | TASK-13.03 | Aberto |
| R-02 | Semânticas de FB (`RESTART WITH`, `OVERRIDING`, batch API) diferentes do esperado | M | M | Testes T-PRV-FB-KEY-*, PV-002 | TASK-08.03, 15.02 | Aberto |
| R-03 | Autorreferência NOT NULL não suportada por linha em MySQL/FB | M | B | Teste SELF-NN; fallback documentado | TASK-10.04, 16.01 | Aberto |
| R-04 | Metas de mapeamento não atingidas com o corpus real | M | M | Ajustar aliases/abreviações; registrar | TASK-05.04 | Aberto |
| R-05 | Licença dos dados do pack pt-BR | B | A | Dados autorais; `SOURCES.md` | TASK-04.06/17.06 | Aberto |
| R-06 | Runners de CI não suportam FlaUI | M | B | Máquina dedicada (PA-012) | TASK-00.03 | Aberto |
| R-07 | Desempenho de inserção direta FB abaixo da meta | A | B | Meta ajustável; batch API | TASK-15.02 | Aberto |
| R-08 | Usuário rotula produção como Development | M | A | Padrão Unknown; fingerprint; alerta de servidor conhecido como Production em outro perfil | TASK-11.04 | Aberto |
| R-09 | Escopo do MDS-Expr crescer | M | M | Lista fechada de funções; ADR-014 | TASK-06.05 | Aberto |
| R-10 | `Microsoft.Data.Sqlite`/bundle sem JSON1 | B | M | Teste em 04.01 | TASK-04.01 | Aberto |
| R-11 | Dependência de terceiros abandonada (Dapper, Serilog) | B | M | Uso restrito, substituível | — | Aceito |
| R-12 | COPY binário PG e identity ALWAYS | M | B | Fallback INSERT | TASK-15.02 | Aberto |
| R-13 | Comportamentos de MySQL/PostgreSQL/Firebird nunca exercitados: sem Docker local, smoke de integração falso (docs/37 A-01/A-03) | A | A | Testcontainers real + CI de integração bloqueante; máquina com Docker para as IAs da trilha B | Sprint B1 | Aberto |
| R-14 | Vulnerabilidades em dependências transitivas (SQLitePCLRaw alta; SSH.NET alta; System.Drawing.Common crítica) | M | A | Atualizar/forçar versões corrigidas; bloquear release se em `src/` | Sprint B1, Gate V1 | Aberto |
| R-15 | Conflitos de merge e decisões divergentes entre IAs em paralelo | A | M | Posse de diretórios por trilha, faixas de numeração, merge frequente da `main`, sprints de gate | DA-054, PLANO-DE-ACAO.md | Aberto |
| R-16 | Duas trilhas mudam o mesmo contrato público (C01–C09) sem coordenação | M | A | Contrato só muda pela trilha dona (tabela de posse do plano); mudança cross-trilha vira PV "requer coordenação" e é resolvida no gate | PLANO-DE-ACAO.md | Aberto |
