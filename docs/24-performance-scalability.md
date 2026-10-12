# 24 — Desempenho e Escalabilidade

| Campo | Valor |
|---|---|
| Objetivo | Definir estratégias e metas mensuráveis para grandes volumes sem bloquear a UI e sem carregar tudo em memória. |
| Escopo | Geração, escrita, introspecção, UI. |
| Relacionados | [15 §3, §12](15-generation-engine.md), [18 §6](18-sql-generation.md), [19 §9](19-direct-database-execution.md), [04](04-non-functional-requirements.md), [30 §F](30-risks-and-tradeoffs.md) |

## 1. Arquitetura de streaming

```mermaid
flowchart LR
    G[Gerador de chunks<br/>Parallel.For por sub-faixas] -->|RowBatch ordenado| C[[Channel limitado<br/>8 batches]]
    C --> W[Writer único<br/>script / COPY / prepared]
    W -->|contrapressão| C
```

- Produção e consumo concorrentes; capacidade limitada → memória constante.
- Linhas são `object?[]` reaproveitáveis via `ArrayPool` por batch (devolvidas após `WriteBatchAsync`).
- Writers de script usam buffer de 1 MB e `StringBuilder` reaproveitado.
- **Nada** de lista com todas as linhas de uma tabela em memória — inclusive para FKs (KeyOracle O(1), [16 §5.3](16-relational-generation.md)).

## 2. Metas (NFR-PERF; medidas por BenchmarkDotNet / cenários cronometrados)

| ID | Métrica | Meta | Cenário de medição |
|---|---|---|---|
| NFR-PERF-001 | Throughput de geração em script PG | ≥ 200 k linhas/s | B-GEN-001: tabela 10 colunas (2 int, 2 decimal, 3 string de template, 1 data, 1 bool, 1 FK), 8 núcleos, SSD |
| NFR-PERF-002 | Memória de trabalho | ≤ 500 MB | B-MEM-001: 10 M linhas em 20 tabelas, sem unicidade por tentativas |
| NFR-PERF-003 | Introspecção | ≤ 30 s para 1.000 tabelas / 15.000 colunas (LAN) | B-INT-001 (PG e FB) |
| NFR-PERF-004 | Responsividade da UI | Thread de UI nunca bloqueada > 100 ms durante geração/execução | T-UI-PERF-002 (monitor de dispatcher) |
| NFR-PERF-005 | Inicialização a frio | ≤ 3 s até a janela utilizável (SSD) | B-START-001 (ReadyToRun habilitado) |
| NFR-PERF-006 | Sugestão de mapeamento | 10.000 colunas em ≤ 2 s | B-MAP-001 |
| NFR-PERF-007 | Autocomplete | ≤ 50 ms (p95) com 5.000 tabelas em cache | B-SQL-001 |
| NFR-PERF-008 | Grade de resultados | 100 k linhas carregadas com rolagem fluida (≥ 30 fps) | T-UI-PERF-003 |
| NFR-PERF-009 | Script grande | 10 M linhas → arquivos segmentados sem exceder 600 MB de RAM | B-OUT-002 |

## 3. Estimativa prévia (FR-090/S14)
- `EstimatedRows` exato (inclui ParentDriven calculado).
- `EstimatedBytes` = Σ por tabela (linhas × média de bytes por linha medida em amostra de 200 linhas formatadas no dialeto).
- Tempo estimado = linhas / throughput medido localmente numa calibração de 2 s (cacheada por máquina/dialeto).
- Avisos: > 2 GB de script → sugerir segmentação/CSV; > 50 M linhas → aviso de duração; inserção direta `Single` > 1 M linhas → aviso.

## 4. Paralelismo
- Por tabela: chunks paralelos (determinístico por ADR-013).
- Entre tabelas: **sequencial** (ordem de dependência; simplifica writers e transações). Paralelismo entre tabelas independentes foi avaliado e rejeitado no V1 (ganho limitado pelo writer único; complexidade de saída intercalada).
- Tabelas com unicidade por tentativas → sequenciais.

## 5. Introspecção eficiente
Consultas em lote ([11 §1](11-schema-introspection.md)); SQLite com pragmas por tabela (rápido local); cache de explorer por perfil invalidado manualmente.

## 6. UI com grandes volumes
- Explorer e grades virtualizados (`VirtualizingPanel.IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel` desabilitado para grades grandes).
- Progresso emitido no máx. 10×/s (throttle).
- Logs ao vivo limitados às últimas 1.000 linhas na UI (arquivo completo em disco).

## 7. Limites configuráveis (padrões)

| Limite | Padrão |
|---|---|
| Linhas por tabela | 100 M (acima: confirmação) |
| `ExistingLimit` | 100 k |
| Unicidade por tentativas | 5 M linhas/coluna |
| Resultados carregados no editor ("Carregar tudo") | 100 k linhas |
| Preview | 1.000 linhas |
| Segmento de script | 50 MB |

## 8. Benchmarks
Projeto `MockDataStudio.Benchmarks` executado manualmente em marcos (não em cada PR); resultados registrados em `docs/benchmarks/<data>.md` (criado pela IA Executora na primeira medição). Regressão > 20% em métrica com meta → abre item de risco.
