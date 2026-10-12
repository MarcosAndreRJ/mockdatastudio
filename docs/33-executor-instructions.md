# 33 — Instruções para a IA Executora

| Campo | Valor |
|---|---|
| Objetivo | Regras operacionais para a IA responsável pela implementação do Mock Data Studio. |
| Escopo | Todo o ciclo de implementação (MVP→V1; V2 após detalhamento). |
| Leitura obrigatória | Este documento inteiro antes da primeira tarefa. |

## 1. Como compreender o repositório
- `docs/` é a **fonte de verdade**; `src/` implementa; `tests/` prova. Estrutura em [07](07-solution-structure.md).
- Mapa mental mínimo: [01](01-product-vision.md) (o quê/por quê) → [05](05-system-architecture.md) (como se organiza) → [08](08-domain-model.md) (vocabulário) → [contracts/](contracts/README.md) (assinaturas) → documento técnico do tema da tarefa → [28](28-implementation-backlog.md) (o que fazer agora).

## 2. Documentos a consultar antes de cada tarefa
1. A própria tarefa em [28](28-implementation-backlog.md) (campos "Contexto" e "Contratos").
2. Os requisitos (FR/NFR) ligados à tarefa na [matriz 35](35-traceability-matrix.md).
3. Os contratos citados.
4. As decisões/premissas relevantes em [32](32-decisions-and-assumptions.md) e ADRs citados.
5. [26](26-testing-strategy.md) para os IDs de teste exigidos.

## 3. Ordem de precedência da documentação (em caso de conflito)
1. Requisitos obrigatórios do produto (briefing: Windows, C#/.NET, offline, sem IA obrigatória, 4 SGBDs, script + inserção direta, projetos persistentes, respeito a estrutura/relacionamentos).
2. ADRs aceitos (`adr/`).
3. Contratos (`contracts/`, incluindo JSON Schemas).
4. Documentos técnicos específicos (09–25).
5. Requisitos (02, 04) e casos de uso (03).
6. Backlog (28) e roadmap (27).
7. Exemplos (`examples/`) — ilustrativos; se divergirem de schema/contrato, o schema/contrato prevalece e o exemplo deve ser corrigido.

Conflito entre itens do mesmo nível → registrar em 32 e escolher a opção mais conservadora (segurança > determinismo > desempenho > conveniência).

## 4. Como interpretar os contratos
- Nomes/tipos/membros são normativos (ver [contracts/README](contracts/README.md)).
- É permitido **acrescentar** membros e tipos internos; **renomear/remover** exige ADR ou registro DA em 32 + atualização do contrato na mesma alteração.
- Blocos marcados `// PSEUDOCÓDIGO` descrevem algoritmo, não assinatura.
- Ao encontrar contrato insuficiente (ex.: falta parâmetro), complete de forma mínima e compatível, e registre.

## 5. Como executar testes
```bash
dotnet test -c Release --filter "Category=Unit|Category=Architecture"
```
```bash
dotnet test -c Release --filter "Category=Contract|Category=Integration"
```
```bash
dotnet test -c Release --filter "Category=UI"
```
- Integração requer Docker (Linux containers). Sem Docker disponível: executar somente SQLite e **declarar** no relatório que os demais não foram executados — nunca afirmar que passaram.
- Filtrar por ID: `--filter "Id=T-GEN-RNG-001"`.

## 6. Como validar builds
```bash
dotnet build -c Release -warnaserror
```
Build com warnings = falha. Não suprimir analisadores sem justificativa registrada (`#pragma` exige comentário com motivo e ID de tarefa).

## 7. Dúvidas e inconsistências
| Situação | Ação |
|---|---|
| Detalhe de baixo impacto não especificado | Decidir (DA), registrar em 32, seguir |
| Hipótese necessária para avançar | Registrar PA com impacto se falsa, seguir |
| Questão de produto (escopo, UX relevante, licença) | Registrar DP com padrão provisório; seguir com o padrão se não bloquear; sinalizar no relatório |
| Contradição entre documentos | Aplicar §3; registrar; corrigir documentação |
| Impossibilidade técnica | Registrar BT, parar a tarefa afetada, propor alternativas |

## 8. Quando registrar decisões técnicas
- **ADR**: escolha de tecnologia/biblioteca, mudança de padrão arquitetural, mudança de contrato público, mudança de algoritmo que afeta determinismo ou formato persistido.
- **DA em 32**: escolhas locais com efeito em mais de um módulo ou que um revisor questionaria.
- Nada a registrar: nomes de variáveis, refatorações internas sem efeito externo.

## 9. Dependências
Antes de adicionar pacote: verificar licença (permissiva), manutenção (release nos últimos 24 meses ou justificativa), compatibilidade com `net10.0(-windows)`, ausência de alternativa na BCL. Registrar em `docs/dependencies.md` (pacote, versão, licença, data, motivo) e em [06](06-technology-decisions.md) se relevante. **Nunca** introduzir dependência GPL/AGPL/licença comercial.

## 10. Evidências
Para cada tarefa, produzir relatório (no PR/commit ou `docs/progress/TASK-xx.yy.md`) com:
1. Tarefa e objetivo.
2. Arquivos alterados (lista).
3. Comandos executados e resultado resumido (build, testes — com contagem de testes passados/falhos/ignorados).
4. IDs de teste cobertos.
5. Snapshots criados/alterados e justificativa.
6. Decisões registradas (IDs).
7. O que **não** foi feito/verificado e por quê.
8. Riscos observados.

## 11. Migrações (catálogo e formatos)
- Catálogo: nova migração `VNNNN__desc.sql` — nunca editar migração existente após entrar no `main`; teste T-CAT-MIG-NNN obrigatório; atualizar [09](09-internal-database.md).
- Digest/Projeto: mudança compatível → MINOR + migrador; incompatível → MAJOR + ADR; fixtures da versão anterior permanecem em `tests/fixtures` para sempre.
- Geradores: mudança de saída → nova versão do gerador ([15 §4.4](15-generation-engine.md)).

## 12. Alterações de escopo
- Não adicionar funcionalidades fora da tarefa. Melhorias percebidas → registrar como sugestão no relatório (ou item V2), não implementar.
- Não mover itens entre MVP/V1/V2 sem DP aprovada.

## 13. Evitar regressões
- Rodar a suíte rápida completa antes de cada commit.
- Nunca alterar snapshot (`.verified.*`) para "fazer passar" sem entender a causa; alteração de snapshot de gerador existente = bug ou nova versão de gerador.
- Testes desabilitados (`Skip`) exigem ID de tarefa/risco e prazo.

## 14. Commits pequenos e documentação sincronizada
- Um commit por passo lógico verificável (ex.: "TASK-06.01: DeterministicRandom + vetores"), mensagem com ID da tarefa.
- Código e documentação mudam **juntos**: se a implementação revela que o documento está errado, corrigir o documento no mesmo commit e registrar em 32 §6.
- Atualizar a [matriz 35](35-traceability-matrix.md) quando testes/tarefas mudarem.

## 15. Condições para declarar uma tarefa concluída
Exatamente a Definition of Done de [29 §1](29-acceptance-criteria.md). Em particular:
- Não declarar sucesso sem executar build e testes e anexar a evidência.
- Não substituir implementações reais por mocks permanentes; fakes só em `tests/`.
- Não ignorar falhas de testes (nem em testes "não relacionados" — investigar ou registrar).
- Não inventar APIs de bibliotecas: verificar na documentação/código do pacote; se não for possível verificar, registrar PV.
- Não executar operações destrutivas em bancos reais: testes usam somente containers/arquivos temporários criados pelo próprio teste.
- Não fazer refatorações fora do escopo sem motivo demonstrável (ex.: bug bloqueante), registrado no relatório.
- Não transformar teste que não executou em teste aprovado: sem Docker/sessão interativa → `Skip` com motivo (DA-055).
- "Build sem warnings" inclui os avisos de auditoria NuGet (NU190x): informe-os no relatório, nunca os omita.

## 16. Código de teste × protótipo × produção
| Tipo | Local | Regras |
|---|---|---|
| Produção | `src/` | Segue todos os documentos; sem atalhos |
| Teste | `tests/` | Pode usar fakes/stubs; nunca referenciado por `src/` |
| Protótipo/spike | `spikes/<tema>/` (fora da solução principal) | Descartável; conclusões registradas em 32 (PV resolvida); **nunca** copiado para `src/` sem reescrita conforme padrões |

## 17. Primeira sessão recomendada
> Histórico: as tarefas do M0–M3 iniciais já foram executadas (ver [37](37-implementation-audit.md)). Uma IA que entra agora segue o §18 e começa pela primeira sprint disponível da sua trilha no [Plano de Ação](../PLANO-DE-ACAO.md).

1. Ler 00, 01, 05, 07, 08, contracts/README, 37, este documento e a seção da sua trilha no Plano de Ação.
2. Confirmar o build e a suíte rápida na `main` antes de criar a branch.

## 18. Execução paralela em branches (DA-054)

O trabalho restante é executado por **2 ou 3 IAs** em paralelo, cada uma numa **trilha** (A — Motor/Planejamento/Saída; B — Providers/SQL/Integração; C — Aplicação/Projeto/Interface). O [Plano de Ação](../PLANO-DE-ACAO.md) é a fonte do protocolo. Resumo obrigatório:

1. **Uma branch por sprint**: `sprint/<ID>-<slug>` criada a partir da `main` atualizada (`git pull`). Nunca trabalhar direto na `main`.
2. **Dependências**: só iniciar a sprint quando todas as sprints listadas em "Depende de" estiverem com status ✅ **na `main`**.
3. **Posse de diretórios**: alterar somente os diretórios da sua trilha. Arquivos compartilhados (lista no plano) só recebem **acréscimos** na seção da trilha. Se precisar alterar código de outra trilha, registre a necessidade no relatório e no PV da sua faixa; não altere.
4. **Numeração**: DA/PV/PA na faixa da trilha ([32](32-decisions-and-assumptions.md), tabela de numeração), acrescentados só na subseção da trilha.
5. **Marcação**: marcar `[x]` em cada etapa concluída, com data e evidência, e atualizar a linha **Status** da sprint. Editar somente a seção da própria sprint no plano.
6. **Fim de sprint**: commit final → `git fetch` → `git merge origin/main` na branch → resolver conflitos (regras no plano) → build + testes → merge `--no-ff` na `main` → build + testes na `main` → `git push origin main` → status ✅ com o hash do merge.
7. **Push rejeitado** (outra IA mesclou antes): `git pull --no-rebase` na `main`, refazer build/testes e repetir o push. Nunca usar `push --force` na `main`.
8. **Conflito que exige decisão de outra trilha**: não resolver por conta própria. Abortar o merge (`git merge --abort`), registrar `⚠ BLOQUEADO` na sprint e descrever o conflito no relatório.
