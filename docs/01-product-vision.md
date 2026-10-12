# 01 — Visão do Produto e Diagnóstico Arquitetural

| Campo | Valor |
|---|---|
| Objetivo | Definir a visão do Mock Data Studio (MDS), registrar o diagnóstico crítico da proposta original e as principais decisões de engenharia derivadas dele. |
| Escopo | Produto completo (MVP, V1, V2). Não especifica implementação; aponta para os documentos técnicos. |
| Status | Aprovado para implementação (revisão 1.0 — 2026-10-11) |
| Documentos relacionados | [02-requirements](02-requirements.md), [06-technology-decisions](06-technology-decisions.md), [27-development-roadmap](27-development-roadmap.md), [32-decisions-and-assumptions](32-decisions-and-assumptions.md) |

---

## 1. Declaração de visão

> **Mock Data Studio é um ambiente desktop offline para engenharia de dados sintéticos com consciência estrutural, semântica e relacional.** Ele lê a estrutura real de bancos SQLite, MySQL, PostgreSQL e Firebird, entende o significado provável de cada coluna, respeita chaves, unicidades e restrições, e produz dados fictícios coerentes, reproduzíveis e seguros — como script SQL ou inserção direta.

### 1.1 Público-alvo

| Persona | Necessidade principal | Uso típico |
|---|---|---|
| **Dev backend** (persona primária) | Popular base local/teste com dados realistas e relacionados | Conecta ao banco de dev, mapeia, gera 10k registros, insere direto |
| **QA / Tester** | Cenários reproduzíveis (mesma seed → mesmos dados) | Salva projeto, versiona no Git, regenera em cada ciclo |
| **DBA / Eng. de dados** | Volume para testes de desempenho; scripts auditáveis | Gera milhões de linhas em scripts segmentados |
| **Consultor / Integrador** (comum no mercado brasileiro com Firebird) | Base demo para clientes, sem dados reais | Usa pacotes de templates pt-BR (CPF, CNPJ, CEP, endereços) |

### 1.2 Proposta de valor e diferenciação

| Diferencial | Por que importa | Onde é especificado |
|---|---|---|
| Firebird como cidadão de primeira classe | Ferramentas de mock data raramente suportam Firebird; base instalada grande no Brasil | [10](10-database-providers.md) |
| Catálogo de templates editável via SQL | O usuário pede à IA "gere 500 INSERTs de endereços" e importa; sem lock-in em formato proprietário | [13](13-template-catalog.md) |
| Mapeamento semântico determinístico com aliases | `ZAP` → `TELEFONE` sem IA, explicável e corrigível | [14](14-semantic-mapping.md) |
| Templates compostos coerentes | CEP, cidade e UF do mesmo registro sempre consistentes | [13 §5](13-template-catalog.md), [15 §7](15-generation-engine.md) |
| Geração relacional formal (SCC, ciclos, FKs compostas) | Não quebra com esquemas reais | [16](16-relational-generation.md) |
| Reprodutibilidade forte (RNG baseado em contador) | Mesma seed + mesma versão de gerador = mesmos bytes, mesmo com paralelismo | [15 §4](15-generation-engine.md) |
| Digest compacto e versionado | Esquema portátil, comparável, útil inclusive como contexto para IAs externas | [12](12-schema-digest.md) |
| Segurança operacional (ambientes, alvo explícito, scripts de limpeza) | Evita o pior cenário: inserir lixo em produção | [23](23-security.md) |

### 1.3 Não-objetivos (explícitos)

1. **Não** é um cliente SQL completo concorrendo com DBeaver/DataGrip (não há designer de tabelas, debugger de procedures, editor de DDL visual).
2. **Não** é ferramenta de migração de schema nem de replicação.
3. **Não** faz subsetting/cópia de dados reais de produção (o recurso de anonimização é V2 e restrito — ver [27](27-development-roadmap.md)).
4. **Não** depende de IA, nuvem ou conta online. Nenhuma telemetria.
5. **Não** garante a execução bem-sucedida de triggers/procedures arbitrárias do usuário: detecta e alerta (ver [17](17-constraint-validation.md)).

---

## 2. Diagnóstico da proposta original

A proposta original é sólida no **objetivo** (geração de mock data guiada por estrutura e semântica) e acerta em vários pontos. Abaixo, a análise sem complacência.

### 2.1 Forças

| # | Força | Comentário |
|---|---|---|
| F1 | Separação entre *template* (tipo semântico) e *valores* (dataset) | Modelo correto; equivale ao padrão "dictionary + dataset" de ferramentas profissionais. |
| F2 | Aliases para casar nomes de colunas | Simples, explicável, extensível pelo usuário. |
| F3 | Digest compacto em vez de metadata bruto | Reduz acoplamento ao SGBD e permite trabalho offline. |
| F4 | Inserção de dados no catálogo via SQL | Excelente ergonomia combinada com IAs externas. |
| F5 | FK automaticamente vinculada à tabela referenciada | Base da geração relacional. |
| F6 | Geradores funcionais além de templates | Necessário: nem tudo é dicionário (datas, sequências, máscaras). |

### 2.2 Fraquezas, lacunas e riscos identificados

| # | Problema | Severidade | Correção adotada |
|---|---|---|---|
| D1 | "Procurar no template campos com o nome igual" — casamento literal falha em `NRO_CEP_ENTREGA`, `ClienteTelefone`, `dsEmail` | Alta | Normalização + tokenização + pontuação determinística com faixas de confiança ([14](14-semantic-mapping.md)). |
| D2 | Template de "endereço" como lista de strings perde coerência entre CEP/cidade/UF | Alta | **Templates compostos** com campos e *slots* de entidade por linha ([13 §5](13-template-catalog.md)). |
| D3 | Usuário executar SQL livre no banco interno pode corromper o catálogo | Crítica | **Importação via banco de staging isolado** + validação + merge transacional ([13 §7](13-template-catalog.md)). |
| D4 | "Pegar aleatoriamente um id da tabela cliente" ignora: chaves geradas pelo banco, FKs compostas, ciclos, FKs autorreferenciadas, unicidade da FK (1:1) | Crítica | Planejador formal com SCC, estratégias de quebra de ciclo, *KeyOracle* e estratégias de chave ([16](16-relational-generation.md)). |
| D5 | Digest apenas como exportação JSON: não define identidade de objetos, hash, versão nem comparação | Alta | Modelo canônico + digest como projeção + snapshot + hash estrutural + diff ([12](12-schema-digest.md)). |
| D6 | Sem estratégia de unicidade: geração aleatória colide (CPF, e-mail, combinações UNIQUE) | Alta | Unicidade **construtiva** via permutações bijetivas, verificação de capacidade prévia ([15 §8](15-generation-engine.md)). |
| D7 | Sem determinismo: impossível reproduzir bug encontrado com dados gerados | Média | Seed hierárquica + RNG por contador + `referenceInstant` + versão de gerador ([15 §4](15-generation-engine.md)). |
| D8 | Editor SQL "executar comandos" sem proteção contra banco errado | Crítica | Tags de ambiente, modo transacional manual, guarda de comandos destrutivos, confirmação tipada em PRODUÇÃO ([20 §8](20-sql-editor.md), [23](23-security.md)). |
| D9 | Mesma conexão para ler referências e inserir: integridade falsa se origem ≠ destino | Alta | Origem e destino explícitos, *fingerprint* de banco e verificação de existência das chaves no destino ([16 §9](16-relational-generation.md)). |
| D10 | Volume não endereçado (milhões de linhas) | Alta | Pipeline em streaming, memória O(1) por tabela para chaves calculáveis, scripts segmentados ([24](24-performance-scalability.md)). |
| D11 | Separação de scripts por `;` | Média | Lexer por dialeto com suporte a `SET TERM` (Firebird) e `DELIMITER` (MySQL), *dollar quoting* (PostgreSQL) ([20 §4](20-sql-editor.md)). |
| D12 | Evolução de schema: mapeamentos se perdem quando uma coluna muda | Alta | Identificadores canônicos + *fingerprint* de coluna + migração assistida de mapeamentos ([12 §9](12-schema-digest.md)). |
| D13 | Credenciais sem tratamento | Alta | DPAPI (escopo usuário Windows); projetos nunca contêm segredos ([23](23-security.md)). |
| D14 | Templates sem tipo lógico: `CEP` mapeado para coluna `INTEGER` gera erro | Média | Tipo lógico e compatibilidade de tipo no score e na validação ([14 §6](14-semantic-mapping.md)). |
| D15 | Ausência de pré-validação: CHECKs, tamanhos, faixas de data por SGBD | Média | Validador pré-voo + inserção de teste em transação revertida (opcional) ([17](17-constraint-validation.md)). |

### 2.3 Oportunidades não previstas originalmente (incorporadas)

| # | Oportunidade | Versão |
|---|---|---|
| O1 | **Script de limpeza** gerado automaticamente (DELETE por faixas de chave da execução) | V1 |
| O2 | **Modo de cardinalidade "por pai"** (cada cliente tem 0–5 pedidos) além de "por filho" | V1 |
| O3 | **Ressincronização de sequences/generators** após inserir chaves explícitas | V1 |
| O4 | **Pacote de templates pt-BR** embutido (nomes, sobrenomes, logradouros, cidades/UF, DDDs, produtos) com dados autorais | V1 |
| O5 | **Geradores BR algorítmicos** (CPF, CNPJ com dígitos verificadores, telefone por DDD) | V1 |
| O6 | **Aprendizado de regras**: confirmações manuais viram regras globais reutilizáveis | V1 |
| O7 | **Digest como contexto para IA externa** (compacto, documentado com JSON Schema) | V1 (efeito colateral do formato) |
| O8 | **CLI** para CI/CD (`mds generate projeto.mdsproj --profile ci`) | V2 (contratos preparados desde o MVP) |
| O9 | **Perfil estatístico de dados existentes** (distribuições, cardinalidades) sem copiar valores | V2 |
| O10 | **Grafo de relacionamentos** interativo | V2 |

---

## 3. Principais decisões de engenharia (resumo)

Detalhes e alternativas em [06-technology-decisions](06-technology-decisions.md) e nos ADRs.

| # | Decisão | ADR |
|---|---|---|
| 1 | **.NET 10 (LTS)** como plataforma, C# 14 | [ADR-015](adr/ADR-015-dotnet-version.md) |
| 2 | **WPF + MVVM (CommunityToolkit.Mvvm)** para UI | [ADR-001](adr/ADR-001-ui-framework.md) |
| 3 | **Monólito modular** com assemblies por responsabilidade e regras de dependência verificadas por teste | [ADR-002](adr/ADR-002-modular-monolith.md) |
| 4 | **ADO.NET puro** (+ Dapper apenas no catálogo interno) para bancos externos; nenhum ORM | [ADR-003](adr/ADR-003-external-data-access.md) |
| 5 | **SQLite + Dapper + migrações SQL versionadas** para o banco interno (não EF Core) | [ADR-004](adr/ADR-004-internal-database.md) |
| 6 | **Motor de geração próprio**; Bogus não faz parte do núcleo (avaliado como pacote opcional V2) | [ADR-005](adr/ADR-005-generation-engine.md) |
| 7 | **Modelo canônico** interno; digest é projeção serializada dele | [ADR-006](adr/ADR-006-canonical-model.md) |
| 8 | **Lexer por dialeto** para divisão de scripts; sem parser SQL completo | [ADR-007](adr/ADR-007-sql-execution.md) |
| 9 | **Chaves explícitas pré-atribuídas** como estratégia padrão; captura via RETURNING como alternativa | [ADR-008](adr/ADR-008-key-strategy.md) |
| 10 | Extensibilidade **interna** no V1; plugins externos via `AssemblyLoadContext` no V2 | [ADR-009](adr/ADR-009-plugin-strategy.md) |
| 11 | Projeto como **arquivo JSON versionado** (`.mdsproj`) | [ADR-010](adr/ADR-010-project-persistence.md) |
| 12 | Segredos via **DPAPI (CurrentUser)** | [ADR-011](adr/ADR-011-credential-storage.md) |
| 13 | **MySqlConnector (MIT)** em vez de MySql.Data (GPL) | [ADR-012](adr/ADR-012-mysql-driver.md) |
| 14 | **RNG baseado em contador** (hash de seed + objeto + índice de linha) | [ADR-013](adr/ADR-013-deterministic-rng.md) |
| 15 | **Linguagem de expressão própria e mínima (MDS-Expr)** | [ADR-014](adr/ADR-014-expression-language.md) |

---

## 4. Princípios de produto

1. **Segurança antes de conveniência**: toda escrita em banco externo exige destino explícito e confirmação proporcional ao ambiente.
2. **Explicabilidade**: toda sugestão automática mostra *por que* foi feita (evidências do score).
3. **Determinismo**: mesma entrada → mesma saída; quando impossível, o sistema informa.
4. **Bloquear cedo**: impossibilidades (capacidade de unicidade, ciclos não quebráveis) são detectadas no planejamento, não após 2 milhões de linhas.
5. **Offline e local**: nenhuma dependência de rede além dos bancos configurados pelo usuário.
6. **Tudo editável, nada perdido**: alterações de schema nunca descartam mapeamentos silenciosamente.

## 5. Métricas de sucesso do produto (V1)

| Métrica | Meta | Como medir |
|---|---|---|
| Colunas auto-mapeadas com confiança ≥ Alta em schema típico de ERP/e-commerce pt-BR | ≥ 70% | Corpus de testes `T-MAP-CORPUS` ([26](26-testing-strategy.md)) |
| Precisão das sugestões de confiança Alta | ≥ 95% | Mesmo corpus, rótulos manuais |
| Tempo até primeiro script gerado para schema de 30 tabelas (usuário experiente) | ≤ 5 min | Teste de usabilidade roteirizado |
| Throughput de geração em script (PostgreSQL, 10 colunas mistas) | ≥ 200k linhas/s | Benchmark `B-GEN-001` |
| Falhas por violação de constraint em execuções com validação pré-voo verde | 0 em suíte de schemas de referência | `T-E2E-*` |

## 6. Decisões pendentes neste documento

Nenhuma decisão de produto bloqueante. Premissas listadas em [32-decisions-and-assumptions](32-decisions-and-assumptions.md) (PA-001 a PA-012).
