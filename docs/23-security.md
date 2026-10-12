# 23 — Segurança

| Campo | Valor |
|---|---|
| Objetivo | Definir modelo de ameaças, proteção de credenciais, proteção contra operações destrutivas, isolamento do catálogo, privacidade e segurança de extensões. |
| Escopo | Aplicação desktop local, mono-usuário. |
| Relacionados | ADR-011, ADR-016, [19 §2](19-direct-database-execution.md), [20 §8](20-sql-editor.md), [22 §3](22-project-file-format.md) |
| Requisitos | FR-003, FR-004, FR-028, FR-095, FR-112; NFR-SEC-* |

## 1. Modelo de ameaças (STRIDE resumido)

| # | Ameaça | Ativo | Probabilidade | Impacto | Controle |
|---|---|---|---|---|---|
| T1 | Execução acidental de DML/DDL no banco errado (ex.: produção) | Dados do usuário | **Alta** | **Crítico** | Ambientes, ReadOnly, guarda, confirmação digitada, autorização vinculada a fingerprint (§5) |
| T2 | Vazamento de senhas via projeto compartilhado/Git | Credenciais | Média | Alto | Projeto nunca contém segredos (§4) |
| T3 | Vazamento de senhas via logs/crash dumps/histórico | Credenciais | Média | Alto | Redação de logs (§6), connection string nunca logada |
| T4 | Leitura de segredos por outro usuário da máquina | Credenciais | Baixa | Alto | DPAPI CurrentUser |
| T5 | Script de catálogo malicioso/defeituoso (de IA ou terceiros) | Catálogo | Média | Médio | Staging isolado (ADR-016) |
| T6 | SQL injection via valores gerados/templates | Banco destino | Média | Alto | Parametrização (direta) e escaping testado (script) (§7) |
| T7 | Query de referência maliciosa em projeto de terceiros | Banco de referência | Baixa | Alto | Validação SELECT-único + execução em transação READ ONLY + confirmação ao abrir projeto de terceiros (§8) |
| T8 | Pack/plugin malicioso | Máquina | Baixa (V2) | Crítico | Packs são dados (sem código); plugins V2 com aviso de confiança (§9) |
| T9 | Dados reais copiados para scripts/relatórios | Privacidade (LGPD) | Média | Alto | Fontes Existing leem apenas **chaves**; relatórios não incluem valores de linhas reais; análise estatística V2 com agregação apenas (§10) |
| T10 | Path traversal em nomes de arquivo de saída | Sistema de arquivos | Baixa | Médio | Sanitização de nomes (§11) |
| T11 | Projeto adulterado apontando para outro banco | Dados | Baixa | Alto | Fingerprint de destino verificado; alerta de divergência |

## 2. Princípios
1. **Destino explícito**: nenhuma escrita sem o usuário escolher o perfil de destino na própria operação.
2. **Defesa em profundidade**: guarda no app **e** modo somente leitura no servidor quando suportado.
3. **Mínimo de dados**: o app só lê do banco o necessário (metadados; chaves para FKs; valores apenas em consultas do usuário).
4. **Sem rede além dos bancos**: nenhuma telemetria, atualização automática silenciosa ou chamada externa (verificável: T-SEC-NET-001 executa fluxo completo com firewall bloqueando tudo exceto o banco de teste).

## 3. Credenciais
- Senha armazenada como `ProtectedData.Protect(UTF8(senha), entropy, DataProtectionScope.CurrentUser)`; `entropy` = 32 bytes aleatórios por instalação em `%LOCALAPPDATA%\MockDataStudio\install.key` (protegido por ACL do perfil do usuário).
- Em memória: senha mantida como `string` apenas durante a construção da connection string (limitação do ADO.NET); nunca em propriedades de ViewModel após salvar; `PasswordBox` não é bindado.
- Perfil "Sempre perguntar": senha solicitada a cada abertura de conexão, mantida em cache somente durante a sessão do app (opção "lembrar até fechar").
- Exportar perfis de conexão (V1.1): exporta sem senha.

## 4. Projetos e arquivos
Ver [22 §3](22-project-file-format.md). Verificação automatizada T-SEC-002: serializa projetos de fixture com perfis contendo senha e garante ausência de qualquer substring da senha/host/usuário no JSON.

## 5. Proteção contra operações destrutivas e banco errado

### 5.1 Ambientes
`ConnectionEnvironment` obrigatório; padrão `Unknown` (tratado como Production). Production marca `IsReadOnly=true` por padrão; desmarcar exige confirmação.

### 5.2 Matriz de decisão do `ExecutionGuard`

| Operação | Development | Test/Staging | Production/Unknown (RW) | Qualquer ReadOnly |
|---|---|---|---|---|
| Editor: SELECT | Allow | Allow | Allow | Allow |
| Editor: DML com WHERE | Allow | Allow | Confirm | Block |
| Editor: destrutivo/DDL | Confirm | Confirm | TypedConfirm | Block |
| Geração: inserção direta | Confirm | Confirm (resumo) | TypedConfirm | Block |
| Inserção de teste (L4) | Allow | Allow | **Block** | Block |
| Limpeza pós-execução | Confirm | Confirm | TypedConfirm | Block |

### 5.3 Vinculação da autorização
`ExecutionAuthorization` vincula **fingerprint do servidor real** (lido na conexão aberta), `PlanHash` e validade de 10 min. Se o perfil for editado para apontar para outro host entre a confirmação e a execução, o fingerprint diverge e a execução é recusada (T-SEC-003).

### 5.4 Sinalização visual
Chip de ambiente em abas do editor, barra de status, tela de execução e diálogos; borda superior colorida do documento (3 px) na cor do ambiente.

## 6. Logs e diagnósticos
- Serilog com *destructuring* bloqueado para tipos `ConnectionSettings`/`ConnectionProfile` (logados por `Id` e `Name` apenas).
- Filtro de redação: regex para `password=`, `pwd=`, `Password=` em qualquer mensagem → `***` (T-SEC-004 injeta mensagens com segredos).
- Exceções de drivers podem conter connection string em mensagens? (alguns drivers incluem `Data Source`) → mensagens de exceção passam pelo mesmo filtro.
- Valores de linhas geradas não são logados (exceto no nível `Trace`, desligado por padrão e com aviso ao ligar).
- Crash: sem minidump por padrão.

## 7. Injeção de SQL
- Inserção direta: **sempre** parâmetros tipados; nenhum valor concatenado.
- Script: literais pelo `ISqlLiteralFormatter` do dialeto com corpus adversarial (T-OUT-LIT-*), incluindo executar o script resultante em banco real e reler os valores (round-trip, T-OUT-ROUNDTRIP-*).
- Identificadores sempre citados com escape do caractere de aspas.
- Valores de templates/expressões são dados, nunca SQL.
- `Query` de referência: validada pelo splitter (exatamente 1 comando, `StatementKind.Select`, sem `INTO`/`FOR UPDATE`) e executada em transação somente leitura no servidor.

## 8. Projetos de terceiros
Ao abrir um projeto cujo `id` nunca foi aberto nesta máquina e que contém `Query`/`Dataset`: diálogo listando as consultas e caminhos de arquivo, com opção "Confiar neste projeto" (registro em `app_setting`). Sem confiança, relações `Query` ficam desativadas.

## 9. Packs e plugins
- Packs (`.mdspack.json`) são somente dados e passam pelo staging.
- Plugins (V2): carregados apenas de `%LOCALAPPDATA%\MockDataStudio\plugins`, com manifesto, lista de permitidos e aviso "plugins executam código com suas permissões"; desativáveis individualmente; modo de segurança (`--safe-mode`) inicia sem plugins.

## 10. Privacidade / LGPD
- O MDS gera dados **fictícios**; templates embutidos são autorais/sintéticos. CPFs/CNPJs gerados são válidos em formato e DV mas não correspondem intencionalmente a pessoas — limitação: um número gerado pode coincidir com um real (inevitável no espaço de 10^9); documentado no Ajuda.
- Domínios de e-mail embutidos são fictícios/reservados (ex.: `exemplo.com.br`, `example.com` conforme RFC 2606) para evitar envio acidental a terceiros.
- Fonte `Existing` lê apenas colunas de chave.
- Histórico de SQL de Production desabilitado por padrão.
- V2 (perfil estatístico/anonimização): somente agregados (cardinalidade, distribuição de comprimentos, histogramas) — nunca valores — salvo anonimização explícita com aviso.

## 11. Sistema de arquivos
- Nomes de arquivo derivados de projeto/perfil sanitizados (remove `\/:*?"<>|`, `..`, nomes reservados Windows `CON`, `NUL`...).
- Escrita apenas no diretório escolhido; caminhos relativos resolvidos a partir do arquivo do projeto e verificados para não escapar dele quando a opção "portável" está ativa.

## 12. Testes de segurança
T-SEC-001..012 em [26 §9](26-testing-strategy.md).
