# 03 — Análise técnica e impacto

## 1. Princípio

Refatorar a **apresentação** sempre que possível. O modelo canônico (`TemplateDraft`, `TemplateDataSet`, schema V0001) **não muda**. As únicas mudanças fora da UI são correções de defeitos de preservação de dados (ver §4), sem alteração de schema.

## 2. Componentes propostos (justificados pelo código)

| Componente | Justificativa no código atual | Decisão |
|---|---|---|
| `TemplatesViewModel` → fatiar | 704 linhas misturando 6 responsabilidades | **Manter como orquestrador fino** (`TemplatesShellViewModel`, mantém nome público para DI/DataTemplate) + VMs filhos |
| `TemplateCatalogViewModel` | Lista/busca/seleção/exclusão/duplicar | Criar |
| `TemplateDetailsViewModel` (+ `TemplateDetailsView`) | Detalhes via `TemplateBrowser.GetDetails`, ações | Criar |
| `TemplateWizardViewModel` (+ `TemplateWizardView`) | Fluxo novo; usa `TemplateDraft` em memória | Criar |
| `ScalarValuesEditor` (UserControl + `ScalarValuesEditorViewModel`) | Reutilizado no passo 3 e na edição | Criar |
| `CompositeRecordsEditor` (+ VM) | Substitui texto `a\|b\|c`; reutilizado no passo 3 e edição | Criar |
| `TemplateImportExportViewModel` (+ View) | Import e export já são separados em serviços (`TemplateImportService`, `TemplatePackExporter`) | Criar |
| `GeneratorPresetsViewModel` (+ View) | `PresetRow` + `MappingLearningService` | Criar |
| `LearnedRulesViewModel` (+ View) | `LearnedRuleRow` | Criar |
| `TemplateDraftValidator` | Validação hoje espalhada em editores; UI precisa de resultado estruturado | Criar em `MockDataStudio.Application` (reusa regex/regras de `TemplateEditor.NormalizeCode`) |
| `ChipListInput`, `StepperControl`, `HelpCard`, `LineNumberedTextBox` | Reutilizados em várias telas; `Controls/` já segue padrão | Criar (Controls) |
| `INavigationGuard` | `ShellViewModel` não tem guarda | Extensão mínima em arquivo **compartilhado** (coordenar) |

Tipo lógico (lista fechada): usar o enum `LogicalType` do Core (16 valores no schema do pack: boolean, integer, decimal, float, string, text, binary, date, time, datetime, datetimetz, uuid, json, enum, interval, other; espelha o `CHECK` de `V0001__init.sql` l. 26); a UI apresenta rótulos amigáveis via resx (sem novo tipo).

## 3. Contratos consumidos / expostos

Consumidos (não alterar assinatura): `TemplateBrowser`, `TemplateEditor`, `CompositeTemplateEditor`, `TemplateImportService`, `TemplatePackExporter`, `MappingLearningService`, `MappingService`, `TemplateRepository`, `GeneratorPresetRepository`, `MappingRuleRepository`, `StagingImporter`, `GeneratorRegistry`.
Expostos para outros módulos: `catalog.template`/`catalog.template.field` via `CatalogTemplateDataSource`, `core.preset` (Mapping), `TemplateDataSet.ContentHash` (projetos `.mdsproj` `CatalogDependency`). **Nenhum muda.**

## 4. Alterações em domínio/persistência (justificadas)

| ID | Problema | Mudança mínima | Migração | Teste |
|---|---|---|---|---|
| TC-001 | `SaveAsync` perde pesos de valores/registros, tags, peso de alias, alias por campo | `TemplateDraft` **já** transporta `Tags`, `TemplateValueRow.Weight`, `TemplateRecordRow.Weight`, `DraftAlias(FieldCode, Weight, ContextTablePattern)` (`Contracts.cs` l. 18–31). O defeito está nos editores: `TemplateEditor`/`CompositeTemplateEditor` montam o draft só com texto e descartam esses dados. Corrigir `Load*` para devolver o draft completo e `Save*` para reaproveitar o que a UI não editou (merge sobre o draft carregado); sem DDL e sem alterar `SaveAsync` | Nenhuma | Round-trip em `CatalogTests`/`EditingTests` |
| TC-002 | `ListAsync` conta só valores | Somar `template_record` para compostos | Nenhuma | Teste de contagem |
| TC-003 | Exportação perde `required`, peso de registro; `BuildScript` força `required=1` | Corrigir `TemplatePackExporter` e `BuiltInPackInstaller.BuildScript`: o schema `template-pack-1.0` **já prevê** `required`, `weight` de registro, `presets`, `rules`; exigir `pack.license`/`pack.version` na exportação | Nenhuma; packs antigos continuam válidos | T-CAT-008 estendido + importação de pack legado |
| TC-004 | Análise sem lista de conflitos | Novo DTO `StagingTemplateSummary` em `StagingAnalysis` (adição aditiva) | Nenhuma | Teste de análise |
| TC-005 | Regras aprendidas com 2 convenções | Normalizar `ColumnPattern` no escritor de `MappingService.LearnAsync` — **toca Mapping (coordenar)** ou apenas documentar (D-07) | Regras cruas existentes continuam válidas (Glob) | Teste de casamento |
| TC-006 | `GeneratorPresetRepository.UpsertAsync` Ok no-op | Retornar falha/`0 linhas` | Nenhuma | Unit |

Alterações de schema: **nenhuma planejada**. Se TC-003 exigir coluna (ex.: licença por pack), parar e abrir decisão D-03 (nova migração V0002 + compatibilidade).

## 5. Compatibilidade com motor de geração

- Registros continuam `template_record` com `data` JSON; `catalog.template.field` seleciona o registro e distribui campos → coerência garantida pelo armazenamento atômico por registro.
- Remover campo exige apagar a chave `data[field]` de todos os registros na mesma transação (já ocorre em `SaveAsync` por delete/insert).
- Pesos: `selection=weighted` passa a ter efeito real ao preservar `weight`.
- `ContentHash` muda quando o conteúdo muda (esperado); projetos antigos com hash divergente continuam mostrando aviso de dependência existente.

## 6. Posse de arquivos e risco de conflito

| Classe | Arquivos | Risco |
|---|---|---|
| **Exclusivos Template Catalog** | `Views/TemplatesView.xaml(.cs)`, `ViewModels/TemplatesViewModel.cs`, novos `Views/Templates/*`, `ViewModels/Templates/*`, `Controls/*` novos, `Catalog/Editing/*`, `Catalog/Repositories/TemplateRepository.cs`, `Catalog/Staging/*`, `Catalog/Packs/*`, `Application/TemplateBrowser.cs`, `MappingLearningService.cs`, testes de Catalog | Baixo |
| **Compartilhados** | `ViewModels/ShellViewModel.cs`, `Composition/ServiceRegistration.cs`, `App.xaml`, `Themes/Styles.xaml`, `Themes/Colors.xaml`, `Resources/Strings.resx`, `Catalog/Contracts.cs`, `Application/MappingService.cs`, `App.UiTests/*` | **Alto** — editar de forma aditiva, commits pequenos, rebase frequente |
| **Dependências externas** | Generation (registry de geradores para combo), Mapping (regras), Connections/Explorer (nenhuma), C1-r2 (resx, nomes de nav), C5/C6-r2 (App) | Coordenar |

Pontos que exigem coordenação: `INavigationGuard` no Shell; chaves resx (usar prefixo `Templates.*`); estilos novos em arquivo próprio `Themes/TemplateCatalog.xaml` mesclado (evita editar `Styles.xaml`); registro DI em arquivo parcial novo (`ServiceRegistration.Templates.cs`) para reduzir conflito.

Branch proposta: `feature/template-catalog-refactor`, **não criada**; partir de `main` atualizada; PRs por fase.

## 7. MVVM/WPF: diretrizes

- O App **não usa CommunityToolkit**: os VMs usam `RelayCommand`/`AsyncRelayCommand` próprios (ex.: `TemplatesViewModel.cs` l. 42–62) e a base de notificação existente. Manter o padrão; não adicionar pacote.
- `INotifyDataErrorInfo` nos VMs de formulário para validação inline.
- Todo I/O via `async` com `CancellationToken`; comandos assíncronos com estado `IsBusy`.
- Sem acesso a repositório na View; code-behind vazio.
- Grade composta: `DataGrid` com colunas geradas em código-atrás mínimo de behavior (attached property) ou `DataTable`-like `ObservableCollection<CompositeRow>` com indexador (`CompositeRow[fieldCode]`); decisão em D-02.
- Singleton de VM atual mantém seleção; wizard é transient por abertura.
