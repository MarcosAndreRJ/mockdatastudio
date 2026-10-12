# 34 — Glossário

| Termo | Definição | Ver |
|---|---|---|
| **Alias** | Nome alternativo de coluna que aponta para um template ou campo de template | 13 §3 |
| **Ambiente (EnvironmentTag)** | Classificação do perfil de conexão: Development, Test, Staging, Production, Unknown | 23 §5 |
| **Autorização de execução** | Token emitido após confirmação, vinculado ao fingerprint do destino e ao PlanHash, válido 10 min | 19 §2 |
| **Binding (ColumnBinding)** | Vínculo de uma coluna a um gerador com parâmetros, taxa de nulos e origem | 08 §5 |
| **Binding estrutural** | Binding determinado pela estrutura (PK identity, FK, coluna gerada) | 14 §3 |
| **Captured** | Estratégia de chave em que o banco gera a chave e o app a captura após o INSERT | 16 §5 |
| **Cardinalidade ChildDriven / ParentDriven** | Filho escolhe pai / cada pai determina quantos filhos | 16 §6 |
| **Catálogo** | Banco interno SQLite com templates, aliases, valores, presets, regras, perfis e histórico | 09 |
| **Confiança** | Faixa heurística (Confirmed/High/Medium/Low/None) de uma sugestão; não é probabilidade | 14 §7 |
| **ContentHash** | Hash SHA-256 do conteúdo ordenado de um template | 13 §2 |
| **Digest** | Representação JSON compacta e versionada do modelo canônico | 12 |
| **Explicit** | Estratégia de chave em que o app atribui valores explícitos acima do máximo atual | 16 §5 |
| **Fingerprint (servidor)** | Hash que identifica o banco físico (engine, host, porta, banco, id do servidor) | 10 §8 |
| **Fingerprint (coluna)** | Hash curto das características de tipo/restrição de uma coluna | 12 §6 |
| **GenerationPlan** | Saída do planner: passos ordenados, planos de tabela, chaves, diagnósticos, PlanHash | C06 |
| **GenerationProfile** | Seed, instante de referência, quantidades, relações, saída e validação | 08 §6 |
| **GenerationRun** | Uma execução de geração com status e relatório | 08 §6 |
| **KeyOracle** | Componente que fornece a chave da linha i de uma tabela sem armazená-la (quando possível) | 16 §5.3 |
| **KeyPool** | Conjunto de chaves existentes carregadas de um banco de referência | C05 §6 |
| **MDS-Expr** | Linguagem de expressão do MDS | 15 §9 |
| **Modelo canônico (SchemaModel)** | Representação em memória, independente de SGBD, da estrutura do banco | 08 §2 |
| **Namespace virtual** | Namespace sintético (Firebird 3–5: `PUBLIC`) não emitido em SQL | 08 §2.1 |
| **NullThenUpdate** | Quebra de ciclo inserindo NULL e atualizando a FK depois | 16 §3 |
| **Pack** | Pacote distribuível de dados de catálogo | 13 §8 |
| **PlanHash** | Hash que identifica unicamente um plano (entradas que afetam a saída) | 15 §4.5 |
| **Preset** | Configuração nomeada de gerador, mapeável como template | 13 §1 |
| **ReferenceInstant** | Instante fixo usado como "agora" na geração | 15 §4.3 |
| **Reprodutibilidade (níveis)** | Exact, SourceBound, TimeBound, VersionBound | 15 §4.5 |
| **SCC** | Componente fortemente conexo do grafo de dependências | 16 §2 |
| **Slot** | Instância de seleção de registro de template composto por linha | 13 §5.2 |
| **Snapshot** | Digest armazenado no projeto com rótulo, hash e fingerprint de origem | 12 §1 |
| **Staging** | Banco SQLite temporário isolado onde scripts de catálogo são executados antes do merge | ADR-016 |
| **StreamKey** | Chave derivada de (seed, escopo, propósito) para o RNG por contador | 15 §4.1 |
| **Template (escalar/composto)** | Tipo semântico com valores (escalar) ou registros de campos correlacionados (composto) | 13 |
| **TemplateValue / TemplateRecord** | Item de template escalar / composto | 08 §4 |
| **tsq** | Flag de coluna Firebird preenchida por trigger + generator | 11 §4.4 |
| **Unicidade bijetiva** | Garantia de unicidade por permutação de índices (Feistel) sobre domínio enumerável | 15 §8.3 |
