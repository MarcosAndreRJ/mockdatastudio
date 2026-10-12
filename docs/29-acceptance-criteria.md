# 29 — Critérios de Aceite

| Campo | Valor |
|---|---|
| Objetivo | Definir Definition of Done (tarefa), gates de marco e cenários de aceite ponta a ponta. |
| Escopo | Complementa os ACs por requisito em [02](02-requirements.md). |
| Relacionados | [26](26-testing-strategy.md), [28](28-implementation-backlog.md), [33](33-executor-instructions.md) |

## 1. Definition of Done (tarefa)

Uma tarefa só é concluída quando **todos** os itens são verdadeiros:

1. Código implementa os passos e contratos citados, sem divergência não registrada.
2. `dotnet build -c Release` sem warnings.
3. Testes da tarefa existem, referenciam seus IDs (`[Trait("Id", ...)]`) e passam; suíte rápida inteira passa.
4. Testes de integração da área tocada passam (quando a tarefa toca Providers/Output/Application).
5. Snapshots novos/alterados revisados e justificados no commit.
6. Documentação afetada atualizada na mesma alteração (regra de sincronização, [33 §14](33-executor-instructions.md)).
7. Nenhum mock/stub substitui implementação real fora de testes; nenhum `NotImplementedException` em caminho alcançável por produção.
8. Evidências anexadas ao relatório da tarefa ([33 §10](33-executor-instructions.md)).
9. Decisões novas registradas em [32](32-decisions-and-assumptions.md) ou ADR.

## 2. Gate do MVP técnico

| # | Critério | Verificação |
|---|---|---|
| G-MVP-1 | Schema `loja` em SQLite, PostgreSQL 17 e Firebird 5: introspecção → digest válido → mapeamento automático → plano → script → execução do script via cliente oficial (`sqlite3`/`psql`/`isql`) sem erros | T-E2E-SQLITE/PG/FB-001 |
| G-MVP-2 | Contagens, FKs (incl. composta e autorreferência), UNIQUEs e CHECKs verificados por SQL após execução | T-E2E-*-001 (consultas de verificação) |
| G-MVP-3 | Duas gerações com a mesma seed produzem scripts idênticos (exceto data), com MaxDOP 1 e 8 | T-E2E-003, T-GEN-DET-002 |
| G-MVP-4 | Ciclo anulável (`cycles`) gera dados válidos em PG e FB; ciclo NOT NULL é bloqueado com diagnóstico | T-REL-002, T-REL-005 |
| G-MVP-5 | Importação de catálogo por script (exemplo) funciona e script com `DROP` é rejeitado | T-CAT-003/004 |
| G-MVP-6 | Mapeamento: corpus atinge metas | T-MAP-CORPUS |
| G-MVP-7 | 1 M linhas de `cliente` (com CPF único) em script PG com memória ≤ 300 MB e ≥ 100 k linhas/s | B-GEN-001 (preliminar), T-GEN-VOL-001 |
| G-MVP-8 | Fluxo MVP na UI executável só por teclado | T-UI-FLOW-001 |
| G-MVP-9 | Testes de arquitetura e segurança MVP verdes | T-ARC-*, T-SEC-002/004/009/012 |
| G-MVP-10 | Relatório do marco (33 §10) com lista de riscos residuais atualizada em [30](30-risks-and-tradeoffs.md) | Revisão |

## 3. Gate do V1

| # | Critério | Verificação |
|---|---|---|
| G-V1-1 | Todos os FR com prioridade MVP/V1 e MoSCoW M atendidos (ACs verdes) | Matriz [35](35-traceability-matrix.md) 100% verde |
| G-V1-2 | Matriz oficial de versões (06 §7) verde | CI nightly + marcos |
| G-V1-3 | Inserção direta + limpeza nos 4 SGBDs devolve o banco ao estado anterior | T-E2E-<X>-002 |
| G-V1-4 | Editor SQL: guarda completa, transação manual, cancelamento nos 4 SGBDs | T-SQL-GUARD-001, T-SQL-EXEC-*, T-PRV-<X>-009 |
| G-V1-5 | Evolução de schema preserva bindings | T-E2E-004 |
| G-V1-6 | NFRs medidos e dentro das metas (ou desvio aprovado em 32) | B-*, relatórios |
| G-V1-7 | Acessibilidade e DPI | T-UI-A11Y-*, T-UI-DPI-001 |
| G-V1-8 | Instalação em Win10/Win11 limpas e execução offline | Checklist + T-SEC-NET-001 |
| G-V1-9 | `docs/licenses.md` completo; dados do pack com fontes registradas | Revisão |
| G-V1-10 | Auditoria final da documentação × implementação sem divergências críticas/altas abertas | Relatório no formato de [36](36-audit-report.md) |

## 4. Cenários de aceite ponta a ponta (Gherkin)

```gherkin
Cenário: Telefone mapeado por alias
  Dado o template TELEFONE com alias "ZAP"
  E uma tabela "cliente" com coluna "zap varchar(20)"
  Quando o usuário aplica o mapeamento automático
  Então a coluna "zap" recebe TELEFONE com confiança Alta
  E a evidência "Alias exato 'ZAP'" é exibida

Cenário: Endereço coerente
  Dado o template composto ENDERECO com registros coerentes
  E colunas nro_cep, cidade e uf mapeadas para o slot ENDERECO
  Quando 10.000 linhas são geradas
  Então para toda linha (nro_cep, cidade, uf) existe como registro do template

Cenário: Bloqueio por capacidade de unicidade
  Dado a coluna cpf UNIQUE mapeada para um template com 800 valores
  E 1.000 linhas pedidas
  Quando o usuário planeja
  Então o diagnóstico PLN-UNIQUE-CAPACITY é exibido como erro
  E a execução fica desabilitada

Cenário: Proteção de produção
  Dado um perfil Production com IsReadOnly=false
  Quando o usuário executa "DELETE FROM pedido" no editor
  Então o sistema exige digitar o nome do banco
  E sem o texto correto nada é executado

Cenário: Banco errado após autorização
  Dado uma autorização emitida para o servidor A
  Quando o perfil é alterado para o servidor B antes da execução
  Então a execução é recusada com SEC-FINGERPRINT-MISMATCH

Cenário: Reprodutibilidade
  Dado um projeto com seed 42 e ReferenceInstant fixo
  Quando o script é gerado duas vezes em máquinas com 4 e 16 núcleos
  Então os arquivos têm o mesmo SHA-256 (excluída a linha "Gerado em")
```
