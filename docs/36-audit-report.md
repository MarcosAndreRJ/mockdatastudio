# 36 — Relatório de Auditoria Independente da Documentação

| Campo | Valor |
|---|---|
| Objetivo | Revisão crítica da especificação "como se não tivéssemos participado da sua criação": contradições, lacunas, riscos; correções aplicadas e pendências residuais. |
| Escopo | Todos os arquivos de `docs/` na revisão 1.0 (2026-10-11). |
| Natureza | **Revisão conceitual e verificações automatizadas de artefatos**. Nenhum componente do produto foi implementado nem validado empiricamente. |

## 1. Método

1. Leitura cruzada dos documentos contra os contratos e as regras de dependência (AR-*).
2. Verificações automatizadas executadas nesta sessão (scripts descartáveis, fora do repositório):
   - **JSON Schema**: os 4 digests de exemplo, o projeto de exemplo e o pack de exemplo validados contra `digest-1.0`, `project-1.0` e `template-pack-1.0` por um validador do subconjunto draft 2020-12 usado nos schemas — todos **válidos**; um digest propositalmente inválido foi **rejeitado** com os 4 erros esperados (prova de que o validador não é permissivo).
   - **SQL em SQLite real** (Node.js `node:sqlite`): DDL do catálogo (09 §3) criado sem erro (15 tabelas); `json_valid` disponível; CHECK de código, trigger de proteção de embutidos e índice único de alias funcionam; DDL de staging (09 §4) + `catalog-import-example.sql` executados (3 templates, 23 aliases, 10 valores, 5 registros, todos os campos obrigatórios presentes); `schema-loja-sqlite.sql` + `output-sqlite.sql` executados com contagens corretas, `PRAGMA foreign_key_check` vazio e trigger de totais aplicada.
   - **Vetores do RNG** (15 §4.1) calculados por implementação de referência em BigInt.
   - **Referências cruzadas**: links Markdown relativos, IDs FR/TASK/ADR, prefixos de testes e códigos PLN/VAL verificados por script.
3. Não verificados empiricamente: comportamento de PostgreSQL, MySQL e Firebird (scripts `output-postgresql.sql` e `output-firebird.sql` são ilustrativos e **não** foram executados); desempenho; UI.

## 2. Achados e correções

| ID | Severidade | Achado | Correção aplicada |
|---|---|---|---|
| A-01 | **Crítico** | `Generation`, `Projects` e `Catalog` precisavam de tipos (`ProviderCapabilities`, `ConnectionProfile`, `GenerationProfile`, `ColumnBinding`, `TemplateDataSet`) definidos em assemblies que eles não podem referenciar (violação AR-02 → dependência circular/proibida) | Tipos de dados puros movidos para `Core.Connections`, `Core.Profiles`, `Core.Bindings`, `Core.Templates` (C02, C03, C05, C06; DA-015) |
| A-02 | **Crítico** | `IGenerationEngine` dependia de `IOutputWriter` (assembly Output, que depende de Generation) → ciclo | Introduzido `IRowSink` em Generation; `IOutputWriter : IRowSink` em Output (C06, C08) |
| A-03 | Alto | `MappingKnowledge` era construído a partir de repositórios do Catalog, mas Mapping não pode referenciá-lo | DTOs `KnowledgeTemplate/Alias/Rule` em Mapping; Application faz a ponte (C04) |
| A-04 | Alto | Providers (parse de DDL SQLite/triggers FB) e Catalog (staging) precisavam do lexer SQL, não previsto nas dependências | AR-04 ampliada: `Providers.*` e `Catalog` → `SqlTooling` (05, 07; DA-016) |
| A-05 | Alto | Hash estrutural dependia do lexer de SqlTooling, mas Core não pode referenciá-lo | `ExpressionNormalizer` próprio em Core com regras exatas (12 §6) |
| A-06 | Alto | Semântica de `RESTART WITH` do Firebird tratada como única; exemplo FB5 geraria colisão de chave | Regra por versão (FB3: `last`; FB4+: `last+increment`) em 10 §6; exemplo corrigido; PV-005 mantida para confirmação |
| A-07 | Alto | Autorreferência anulável com chaves `Captured`: filhos do mesmo batch não conhecem a chave do pai | Regra NullThenUpdate para esse caso (16 §4) |
| A-08 | Médio | `UNIQUE` com expressões em tabela SQLite (`mapping_rule`) é inválido | Substituído por índice único de expressão; verificado em SQLite |
| A-09 | Médio | ParentDriven sem regra para múltiplas FKs ParentDriven e para `rowCount` informado | Regra "uma FK ParentDriven por tabela", `rowCount` ignorado, diagnóstico `PLN-PARENTDRIVEN-MULTI` (16 §6.2) |
| A-10 | Médio | MVP dizia incluir inserção direta SQLite, mas o backlog do MVP não tinha fluxo de UI para isso | Escopo esclarecido: no MVP só o `IBulkInserter` com testes de contrato (27 §2, FR-095) |
| A-11 | Médio | Durações com anos/meses (`-P80Y`) incompatíveis com `TimeSpan` | `IsoDuration` com aritmética de calendário especificada (15 §6) |
| A-12 | Médio | `LengthTooSmall` usava percentil 95 sem fonte do dado | `P95ValueLength`/`MaxValueLength` adicionados a `KnowledgeTemplate/Field` (C04) |
| A-13 | Médio | Paralelismo descrito de forma diferente em 05 e 15 | 05 §6 alinhado a 15 §3 |
| A-14 | Médio | `GenerationProfile` sem fuso horário e modo de instante de referência, exigidos por 15 §4.3 | `ReferenceInstantMode`, `TimeZoneId` adicionados (C06, schema do projeto) |
| A-15 | Médio | `OutputSettings` sem opções usadas por 16/18 (guardas, DisableChecks opt-in, quoting, BOM, separador CSV) | Campos adicionados ao contrato e ao schema do projeto |
| A-16 | Baixo | Convenção de nomes divergente entre JSON (camelCase) e SQL (snake_case) não declarada | Convenção documentada (13 §8); pack usa camelCase |
| A-17 | Baixo | Referências a tarefas inexistentes (TASK-20.05, TASK-12.01 para PVs) | Corrigidas para TASK-15.02, TASK-19.04, TASK-00.05 |
| A-18 | Baixo | `ConnectionProfile.SecretRef` no domínio contradizia "segredo só no catálogo" | Removido do domínio (08 §3) |
| A-19 | Baixo | `ScriptOptions` (C02) redundante com `OutputSettings` | Documentado como subconjunto derivado, não persistido |
| A-20 | Baixo | `ISecretStore` citado sem contrato | Contrato adicionado (C09) |

## 3. Verificações de consistência (estado final)

| Verificação | Resultado |
|---|---|
| Links relativos entre documentos | Todos resolvem (após a criação deste arquivo) |
| FRs referenciados × definidos | Sem referências órfãs |
| Tarefas referenciadas × definidas (inclui pré-requisitos abreviados) | Sem referências órfãs |
| ADRs referenciados × arquivos | Sem referências órfãs |
| Prefixos de IDs de teste × catálogo de testes (26) | Todos catalogados |
| Códigos `PLN-*`/`VAL-*` usados × catalogados (16/17) | Todos catalogados (falsos positivos de prefixos de teste ignorados) |
| Exemplos JSON × schemas | Válidos |
| SQL do catálogo/staging/SQLite × SQLite real | Executam |

## 4. Lacunas e riscos residuais (não resolvíveis apenas por análise)

| ID | Tipo | Descrição | Encaminhamento |
|---|---|---|---|
| RR-01 | Verificação empírica | Comportamentos de SGBD marcados [K] (override de identity no FB4/5, COPY PG com identity ALWAYS, autorreferência NOT NULL por linha, `Cancel` nos drivers) | PV-004..PV-007, PV-014 com testes definidos e fallbacks |
| RR-02 | Métrica | Metas de desempenho e de mapeamento são de projeto, não medidas | Benchmarks B-* e T-MAP-CORPUS; ajustes registrados em 32 |
| RR-03 | Dados | Conteúdo do pack pt-BR ainda não existe; risco de licença | TASK-04.06/17.06 com `SOURCES.md` (PA-009) |
| RR-04 | Produto | Decisões DP-001..DP-009 pendentes (nome, licença, empacotamento, CI) | Padrões provisórios não bloqueiam |
| RR-05 | Ferramenta | AvalonEdit com upstream pouco ativo | R-01; plano B AvalonEditB |
| RR-06 | Especificação | V2 especificado apenas em nível de visão/épico | Detalhar antes de iniciar o V2 (27 §4) |
| RR-07 | Validação | Validador JSON usado na auditoria implementa só o subconjunto necessário do draft 2020-12 | TASK-01.04 usa validador de referência completo nos testes (T-DIG-010) |
| RR-08 | Firebird 6 | Introdução de schemas pode exigir ajuste de namespace | PA-007 |

## 5. Complexidade avaliada como justificada × cortada

- **Mantida** (benefício demonstrável): RNG por contador (paralelismo + reprodutibilidade), permutação bijetiva (unicidade O(1)), SCC + quebra de ciclos (schemas reais), staging isolado (segurança do catálogo), autorização vinculada a fingerprint (risco crítico T1).
- **Cortada/adiada**: plugins, parser SQL completo, docking, paralelismo entre tabelas, motor de regras genérico, formato binário de digest, anonimização ampla (27 §7).

## 6. Conclusão

A documentação atende aos critérios de sucesso do briefing (§18) **em nível de especificação**: visão, requisitos, módulos, modelo interno, contratos, diferenças entre SGBDs, algoritmos do motor, relacionamentos e exceções, UX, NFRs mensuráveis, testes e critérios de aceite, plano ordenado, riscos e limitações. A validação empírica dos comportamentos de SGBD e das metas é responsabilidade das tarefas indicadas e não deve ser presumida.
