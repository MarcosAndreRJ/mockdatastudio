# 04 — Plano de implementação

Branch: `feature/template-catalog-refactor` (**não criada; só após aprovação**). Um PR por fase. Todo commit mantém `dotnet build` e `dotnet test` verdes. Nenhum teste existente é removido; `TemplatesFlowTests` e `SmokeTests` são **migrados**.

Legenda: **[O]** obrigatória · **[M]** melhoria opcional. Formato de cada tarefa: Objetivo · Arquivos · Implementação · Dep. · Riscos · Testes · Aceite (evidência) · Conclusão.

## Fase 0 — Pré-requisitos de integridade de dados (bloqueante para as demais)

| ID | Tarefa |
|---|---|
| **TC-001 [O]** | **Objetivo:** editar sem perder dados. **Arquivos:** `Catalog/Editing/TemplateEditor.cs`, `CompositeTemplateEditor.cs`; testes `EditingTests`, `CompositeEditingTests`. **Impl.:** `Load*` retorna o `TemplateDraft` completo; `Save*` mescla edição de UI sobre o draft original (pesos, tags, alias por campo, `ContextTablePattern`, peso de alias, `required`). **Dep.:** — **Riscos:** regressão de assinatura (manter overloads atuais). **Testes:** round-trip carrega→salva→recarrega e compara tudo; template composto com alias por campo. **Aceite:** teste vermelho antes, verde depois; hash idêntico após salvar sem alterações. **Conclusão:** testes verdes + evidência do diff de round-trip. |
| **TC-002 [O]** | Contagem de compostos. `Catalog/Repositories/TemplateRepository.cs` (`ListAsync`), `TemplateInfo` se necessário (aditivo). Teste: composto com 3 registros → 3. Aceite: lista mostra 3, não 0. |
| **TC-003 [O]** | Export/import sem perdas: `TemplatePackExporter`, `Packs/BuiltInPackInstaller.cs` (`BuildScript`). Schema `template-pack-1.0` já prevê `required` e peso de registro (lido): exportar e importar respeitando-os; exportar sempre `pack.license` e `pack.version` (x.y.z). Teste: export→import→export idempotente; pack legado ainda importa. Dep.: TC-001. |
| TC-004 [O] | `StagingAnalysis` com lista de templates (código, tipo, itens, existe/novo) e issues. `Staging/StagingImporter.cs`, `Contracts`/DTO aditivo. Teste análise com conflito. |
| TC-005 [O] | `GeneratorPresetRepository.UpsertAsync` não pode devolver Ok em no-op. `Catalog/Repositories/OtherRepositories.cs`. Teste unitário. |
| TC-006 [M→D-07] | Normalização de `ColumnPattern` nas regras aprendidas (toca Mapping): só após decisão. |
| TC-007 [O] | Serviço de impacto de exclusão: contagem de valores/aliases (+ projetos recentes via `recent_project`, se compatível). `Application/TemplateBrowser.cs` (método novo). Teste unitário. |
| TC-008 [O] | `TemplateDraftValidator` (código, nome, tipo, campos, células por tipo, duplicatas, obrigatórios). `Application/Templates/TemplateDraftValidator.cs`. Testes por regra. |
| TC-009 [O] | Busca por alias/tag: `TemplateRepository.ListAsync`/`TemplateQuery` aditivo. |

## Fase 1 — Estrutura de navegação

| ID | Tarefa |
|---|---|
| **TC-101 [O]** | Fatiar `TemplatesViewModel` em orquestrador + VMs filhos (Catalog, Details, ImportExport, Presets, LearnedRules); mover lógica sem mudar comportamento. Arquivos: `ViewModels/TemplatesViewModel.cs`, novos `ViewModels/Templates/*`, `Composition/ServiceRegistration*.cs`. Dep.: —. Risco: quebrar bindings → criar `tests/MockDataStudio.App.Tests` (D-11; não existe hoje) com testes de VM usando serviços reais de `CatalogTestEnv`. Aceite: `TemplatesFlowTests` verde (com ajuste só se nomes mudarem). |
| **TC-102 [O]** | `TemplatesView` com 4 abas (`UnderlineTabControl`), cabeçalho, Atualizar; UserControls por aba. Estilos em `Themes/TemplateCatalog.xaml`. Dep.: TC-101. Aceite: troca de aba preserva seleção/busca (teste de VM). |
| **TC-103 [O]** | Estados (vazio, carregando, sem resultados, erro) com `ProgressPanel`/`DiagnosticsList`. Dep.: TC-102. |
| **TC-104 [O]** | `INavigationGuard` no `ShellViewModel` (aditivo) + diálogo salvar/descartar/continuar. Arquivo compartilhado → coordenar. Teste: navegar com rascunho sujo é bloqueado. |
| TC-105 [O] | Textos em `Strings.resx` (prefixo `Templates.`) — alinhar com C1-r2; `AutomationProperties.Name` em todos os controles. |

## Fase 2 — Visualização e edição

| ID | Tarefa |
|---|---|
| **TC-201 [O]** | `TemplateDetailsView`: cabeçalho, badges, info card, cards Como usar/Importante, exemplos, coluna Valores com busca/cópia. Dados: `TemplateBrowser.GetDetails`. Dep.: TC-102, TC-002. |
| TC-202 [O] | Ações Editar/Duplicar/Mais (Excluir com diálogo de impacto via TC-007; `TypedConfirmDialog` conforme D-06). Embutido: sem Editar; "Clonar para editar". |
| TC-203 [O] | Lista: badge, contagem, selo embutido, busca ampliada (TC-009). |
| TC-204 [M] | Pré-visualização de geração (10 amostras via `catalog.template`) no detalhe. |
| TC-205 [O] | Edição: reutiliza wizard em modo `Edit` (código somente leitura; renomear = Duplicar + Excluir, explicado na UI) — sem criação acidental de novo template. |

## Fase 3 — Assistente

| ID | Tarefa |
|---|---|
| **TC-301 [O]** | `TemplateWizardViewModel`, `StepperControl`, `TemplateDraftModel` (estado em memória; **zero escrita** até Salvar). Dep.: TC-008. |
| TC-302 [O] | Passos 1 e 2 (tipo, informações, `ChipListInput`, sugestão de código, unicidade assíncrona com debounce). |
| TC-303 [O] | Passo 4 (revisão: resumo, contagem, amostra, avisos/erros, voltar a etapas, Salvar / Salvar e criar outro). Salvar via `TemplateEditor`/`CompositeTemplateEditor`. |
| TC-304 [O] | Cancelar/Esc e guarda de alterações; falha ao salvar mantém dados. |
| TC-305 [O] | Preservar nomes de automação usados em `TemplatesFlowTests` ou migrar o teste (e documentar). |

## Fase 4 — Editor escalar

| ID | Tarefa |
|---|---|
| **TC-401 [O]** | `ScalarValuesEditor` com `LineNumberedTextBox`, prévia chips, remoção de vazias/duplicadas com avisos, validação de tipo (Warning por linha). |
| TC-402 [O] | Colar do Excel (TSV 1 ou 2 colunas: valor[,peso]); peso opcional e preservado na edição (TC-001). |
| TC-403 [M] | Modo grade (valor, peso) com ordenação. |

## Fase 5 — Editor composto

| ID | Tarefa |
|---|---|
| **TC-501 [O]** | `CompositeRecordsEditor`: DataGrid sobre `CompositeRow` (indexador por `field_code`), colunas dinâmicas, adicionar/remover registros, ordenação por linha inteira. Decisão técnica D-02. |
| **TC-502 [O]** | Config de campos: adicionar/remover/reordenar, tipo, tamanho, obrigatório; remover campo com confirmação e remoção da chave nos registros; novo campo obrigatório bloqueia salvar até preenchido. |
| TC-503 [O] | Coluna Peso; chave de registro somente leitura avançada. **Não** criar coluna "Parâmetros" (D-01). |
| TC-504 [O] | Importar da planilha (TSV colado) com mapeamento por cabeçalho e relatório de linhas rejeitadas. |
| TC-505 [M] | Reordenar por arrastar; combos para campos enumeráveis (UF). |
| TC-506 [O] | Sub-abas Configuração/Aliases (inclui alias por campo)/Prévia. |
| TC-507 [O] | Teste de integridade: gerar N amostras com `catalog.template.field` e provar coerência CEP/Cidade/UF. |

## Fase 6 — Importar/Exportar

| ID | Tarefa |
|---|---|
| **TC-601 [O]** | `TemplateImportExportView`: seletor de formato único (detecção automática), entrada colar/arquivo, análise com lista de templates e issues (TC-004), modo InsertOnly padrão, Upsert com confirmação, Replace com `TypedConfirmDialog`. Segurança: apenas `StagingImporter`. |
| TC-602 [O] | Relatório da importação e "Desfazer última importação" (expõe backup premerge em `TemplateImportService`). |
| TC-603 [O] | Exportação seletiva (busca, selecionar todos, totais, validação de código/nome/**versão/licença** do pack — schema exige licença, D-12, diálogo salvar). Embutidos desabilitados (D-04). |
| TC-604 [M] | Exportar `presets`/`rules` nos campos já previstos no schema (D-03). |

## Fase 7 — Presets e Regras aprendidas

| ID | Tarefa |
|---|---|
| **TC-701 [O]** | `GeneratorPresetsView`: lista (embutidos RO), formulário com validação (código maiúsculo, versão inteira, JSON objeto), combo de geradores do `GeneratorRegistry`, aviso DA-306. |
| TC-702 [O] | Sugestões apenas de geradores existentes; remover exemplos inexistentes (`br.telefone`, `br.data`). |
| **TC-703 [O]** | `LearnedRulesView`: lista, filtro, esquecer com confirmação, estado vazio + "Como isso funciona?". |
| TC-704 [M] | Schema de parâmetros por gerador (fora do escopo se o registry não expõe). |

## Fase 8 — Testes, acessibilidade, estabilização

| ID | Tarefa |
|---|---|
| **TC-801 [O]** | Migrar `TemplatesFlowTests` (T-UI-FLOW-002) e `SmokeTests`; novos testes UI do fluxo profissões e endereço. |
| TC-802 [O] | Navegação por teclado, foco, AutomationProperties, contraste. |
| TC-803 [O] | Regressão: toda a suíte; verificar `Mapping`/`Generation` com templates editados. |
| TC-804 [O] | Atualizar docs (13, 21, ui-inventory) com divergências corrigidas (hash) e registrar DAs. |
| TC-805 [M] | Telemetria de cliques/tempo para critérios de UX (apenas local). |

## Ordem e dependências

`TC-001 → TC-003`; `TC-008 → TC-301`; `TC-101 → TC-102 → (TC-103, TC-104, TC-201…)`; `TC-004 → TC-601`; `TC-007 → TC-202`; `TC-501 → TC-502/503/504`. Fase 0 e Fase 1 podem ocorrer em paralelo (arquivos disjuntos). Sem ciclos.

## Arquivos que seriam modificados

Existentes: `TemplatesView.xaml(.cs)`, `TemplatesViewModel.cs`, `ShellViewModel.cs`, `ServiceRegistration.cs`, `Strings.resx`, `App.xaml` (se novos DataTemplates), `TemplateEditor.cs`, `CompositeTemplateEditor.cs`, `TemplateRepository.cs`, `OtherRepositories.cs`, `StagingImporter.cs`, `TemplateImportService.cs`, `TemplatePackExporter.cs`, `BuiltInPackInstaller.cs`, `TemplateBrowser.cs`, `MappingLearningService.cs`, `TemplatesFlowTests.cs`, `SmokeTests.cs`, docs 13/21.
Novos: `Views/Templates/*`, `ViewModels/Templates/*`, `Controls/{StepperControl,ChipListInput,HelpCard,LineNumberedTextBox}`, `Themes/TemplateCatalog.xaml`, `Application/Templates/TemplateDraftValidator.cs`, testes correspondentes.
