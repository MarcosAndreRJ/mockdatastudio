# 06 — Decisões pendentes, riscos e pressupostos

## 1. Decisões pendentes

| ID | Decisão | Alternativas | Recomendação | Bloqueia |
|---|---|---|---|---|
| D-01 | Coluna "Parâmetros" da imagem 6 | (a) ignorar; (b) coluna "Peso"; (c) novo campo JSON por registro (muda domínio) | **(b)** — usa `weight` existente | TC-503 |
| D-02 | Estrutura da grade composta | (a) `DataGrid` com `CompositeRow` indexado; (b) `DataTable`; (c) controle próprio | **(a)** | TC-501 |
| D-03 | Exportar presets/regras no pack | (a) adiar; (b) exportar nos campos `presets`/`rules` **já previstos no schema** | **(b)** como melhoria opcional; sem migração | TC-604 |
| D-11 | Onde testar ViewModels | (a) novo `MockDataStudio.App.Tests`; (b) `App.UiTests` | **(a)** | TC-101 |
| D-12 | Licença/versão do pack na exportação (schema exige) | (a) padrão "Personal-Use"/1.0.0 editável; (b) obrigatório sem padrão | **(a)** | TC-603 |
| D-04 | Exportar templates embutidos (imagem 7) | (a) desabilitar; (b) permitir exportar cópia como pack do usuário | **(a)** | TC-603 |
| D-05 | Modo de mesclagem na UI | (a) só InsertOnly; (b) InsertOnly + Upsert confirmado; (c) + Replace tipado | **(c)** conforme docs/13 | TC-601 |
| D-06 | Limiar para exclusão com confirmação digitada | 100 valores, 1000, sempre | 100 | TC-202 |
| D-07 | Convenção de `ColumnPattern` das regras aprendidas (cru × compacto) | (a) normalizar no escritor de `MappingService`; (b) documentar; (c) migração dos dados | **(a)** com coordenação (toca Mapping) | TC-006 |
| D-08 | Renomear código | (a) proibir; (b) renomeação transacional com atualização de referências | **(a)** (usar Duplicar+Excluir) | TC-205 |
| D-09 | Idioma/strings | Seguir C1-r2 (resx pt-BR) | Seguir | TC-105 |
| D-10 | Fatiar `TemplatesViewModel` mantendo o nome público | Sim/Não | Sim | TC-101 |

## 2. Riscos

| ID | Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|---|
| R-01 | Perda de pesos/tags/aliases ao editar (defeito atual) | Alta | Alto | TC-001 antes de liberar editor |
| R-02 | Quebra de `TemplatesFlowTests` | Alta | Médio | TC-305/801, nomes de automação preservados |
| R-03 | Conflito em Shell/Styles/ServiceRegistration/resx com outras IAs | Média | Médio | Arquivos novos próprios, edições aditivas, rebase diário |
| R-04 | Importação SQL insegura | Baixa | Alto | Só staging; testes de rejeição de DDL/PRAGMA/ATTACH |
| R-05 | Mapping/Generation quebrados por mudança em exportação/editores | Baixa | Alto | Contratos intocados; TC-803 |
| R-06 | Grade composta permitir mistura de campos | Baixa | Alto | Registro atômico; TC-507 |
| R-07 | Tamanho da tela/rolagem em resoluções baixas | Média | Baixo | Testes 1366×768 |
| R-08 | Imagens induzirem campos inexistentes (Parâmetros, br.telefone) | Média | Médio | `02` §1 |
| R-09 | Pack legado incompatível após TC-003 | Baixa | Alto | Campos opcionais; teste de pack legado |
| R-10 | Merge InsertOnly acrescenta dados a templates existentes (comportamento atual) | Média | Médio | Corrigir/testar em TC-601 (verificar antes) |

## 3. Pressupostos

1. Schema `V0001` permanece; nenhuma migração planejada.
2. `template-pack-1.0.schema.json` **foi lido na revisão**: já suporta `required`, `weight` de registro, `presets`, `rules`, `locale`, e exige `pack.license` (minLength 1) e `pack.version` `x.y.z`. Logo TC-003/TC-604 são correções de exportador/instalador, **sem mudança de contrato**; a UI de exportação precisa de campos **Versão** e **Licença** (ausentes na imagem 7).
3. O registry de geradores permite listar ids (para combo de presets); a confirmar em TC-701.
4. Não existe projeto de teste de ViewModels do App (`tests/` tem apenas `App.UiTests`, `Application.Tests`, etc.): TC-101 cria `tests/MockDataStudio.App.Tests` **ou** testa VMs em `App.UiTests` (decisão D-11).
5. C1-r2, C5-r2 e C6-r2 (trilha C) não mexem na pasta `Views/Templates/*`.

## 4. Itens não examinados (honestidade de escopo)

Internals de `TemplateLoader`; trechos de `StagingImporter` fora de análise/merge; `docs/contracts/C03` linha a linha para presets de projeto. Esses pontos viram **verificações iniciais** das tarefas correspondentes.

## 5. Bloqueios

Nenhum bloqueio impeditivo ao início da Fase 0/1. **Condição:** TC-001 concluído antes de liberar edição visual (Fases 2 e 5).
