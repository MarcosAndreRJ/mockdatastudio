# 01 — Auditoria do estado atual do Template Catalog

Base: `main` @ `c3cccec`. Hierarquia: código > docs/contratos > imagens > objetivo do produto.
Classificações: **IT** implementada e testada · **IS** implementada, testes insuficientes · **P** parcial · **V** apenas visual · **A** ausente · **D** com defeito.

## 1. Mapa do módulo (arquivos reais)

| Camada | Arquivo | Papel |
|---|---|---|
| UI | `src/MockDataStudio.App/Views/TemplatesView.xaml` (338 linhas) / `.xaml.cs` (vazio) | Tela única: lista 340 px + ScrollViewer central com cards empilhados + coluna direita 360 px de valores |
| UI | `src/MockDataStudio.App/ViewModels/TemplatesViewModel.cs` (704 linhas) | VM monolítico: lista, busca, detalhes, formulário escalar e composto (em **texto**), importação, exportação, presets, regras aprendidas. Records `TemplateRowItem`, `PresetRow`, `LearnedRuleRow` |
| UI | `ViewModels/ShellViewModel.cs`, `Composition/ServiceRegistration.cs` (l. 64–96), `App.xaml` (l. 21) | Navegação por `NavEntry`; registro singleton dos serviços e do VM; DataTemplate VM→View |
| UI | `Controls/*` (ConfidenceBadge, ConfirmDialog, DiagnosticsList, EnvironmentChip, ProgressPanel, SearchField, SeverityBadge, TypedConfirmDialog…), `Themes/Styles.xaml`, `Themes/Colors.xaml` | Estilos reutilizáveis: Card, Chip, Pill, TabText, UnderlineTabControl, PrimaryButton, etc. |
| Aplicação | `src/MockDataStudio.Application/TemplateBrowser.cs`, `MappingLearningService.cs`, `MappingService.cs` | Consulta, presets, regras aprendidas, resolução `core.preset` |
| Catálogo | `src/MockDataStudio.Catalog/Editing/{TemplateEditor,CompositeTemplateEditor,TemplateImportService,TemplatePackExporter,TemplateExamples}.cs` | Escrita, importação, exportação |
| Catálogo | `Repositories/{TemplateRepository,TemplateLoader,OtherRepositories}.cs`, `Staging/StagingImporter.cs` (701 l.), `Packs/BuiltInPackInstaller.cs`, `Migrations/V0001__init.sql`, `Contracts.cs`, `Data/CatalogTemplateDataSource.cs` | Persistência, staging, schema |
| Core | `src/MockDataStudio.Core/Templates/TemplateDataSet.cs` | Conjunto imutável + `ContentHash` |
| Geração | `Generation/Generators/BuiltInGenerators.cs` | Consumo: `catalog.template`, `catalog.template.field`, `core.preset` |
| Testes | `tests/MockDataStudio.Catalog.Tests/*`, `Application.Tests/MappingLearningTests.cs`, `App.UiTests/TemplatesFlowTests.cs`, `SmokeTests.cs` | Ver §3 |

## 2. Matriz de funcionalidades

| # | Funcionalidade | Estado | Evidência / observação |
|---|---|---|---|
| 1 | Listar templates com busca por código/nome | IT | `TemplateBrowser`; LIKE em `code`/`name` (spec pede também alias/tag → **P**) |
| 2 | Contagem de itens na lista | **D** | `ListAsync` conta só `template_value`; compostos mostram **0** (a imagem 1 também mostra 0) |
| 3 | Detalhes (campos, aliases, amostra 40) | IT | `TemplateBrowser.GetDetails`; funciona para embutidos |
| 4 | Criar template escalar | IT | `TemplateEditor.SaveAsync`, `EditingTests` |
| 5 | Editar escalar | **D** | `TemplateRepository.SaveAsync` apaga e reinsere: pesos de valores → 1.0, tags → `[]`, peso de alias e alias por campo perdidos |
| 6 | Duplicar (Clone) / Excluir | IS | Existe e é testado no catálogo; **exclusão sem confirmação** na UI |
| 7 | Criar/editar composto (campos `codigo\|tipo\|tamanho\|obrigatório` e registros `a\|b\|c` em texto) | **D** | `CompositeTemplateEditor`; registros perdem peso; `record_key` renumerado por posição; NULL = vazio |
| 8 | Editor visual em grade | A | Só TextBox multilinha |
| 9 | Aliases de template | P | Editor escalar/composto só trata alias de template inteiro; alias por campo (`FieldCode`) é perdido ao salvar |
| 10 | Validação inline / tipo por valor | A/P | Tipo lógico é TextBox livre; validação de tipo só no staging (Warning) |
| 11 | Estado "alterações não salvas" / Cancelar | A | Nenhuma guarda no `ShellViewModel` |
| 12 | Renomear código | P | Salvar com outro código cria novo, sem aviso |
| 13 | Importação SQL/pack (análise + importar) | IS | `TemplateImportService` + `StagingImporter`; UI sempre `MergeMode.Upsert` (spec: padrão InsertOnly) |
| 14 | Conflitos por template na análise | A | `StagingAnalysis` só tem contagens; sem lista de issues `CAT-*` na UI |
| 15 | Desfazer importação (backup premerge) | A (UI) | Backup existe; sem ação de restauração na UI (docs/13 §9, S09) |
| 16 | Exportação de pack | IT/**P** | T-CAT-008; só não-embutidos; não exporta `required`, peso de registro, presets, regras, licença |
| 17 | Round-trip pack | **D** | `BuiltInPackInstaller.BuildScript` força `required=1`, ignora peso de registro |
| 18 | Presets de gerador (listar/salvar/excluir) | IS | `MappingLearningService` valida `^[A-Z0-9_]{2,64}$`, versão inteira ≥1, params objeto JSON; embutidos somente leitura |
| 19 | Repositório de presets | **D** (menor) | `GeneratorPresetRepository.UpsertAsync` retorna Ok em no-op para `is_builtin=1` (serviço já bloqueia antes) |
| 20 | Regras aprendidas (listar/esquecer) | IS | Dois escritores: `MappingService.LearnAsync` grava nome **cru**; `MappingLearningService.RememberAsync` grava nome **compacto normalizado**; o motor casa contra `norm.Compact` → inconsistência |
| 21 | Estados vazio/carregando/erro | P | Status único em texto |
| 22 | Localização | **D** | Strings pt-BR hard-coded no XAML; "Refresh" em inglês; `Strings.resx` com 11 chaves (C1-r2 trata) |
| 23 | Exibição de licença/pack na importação | A | spec docs/13 §8 |
| 24 | Impacto de exclusão em projetos recentes | A | `CatalogDependency` e `recent_project` existem; não há serviço |

## 3. Cobertura de testes

- `CatalogTests`, `EditingTests`, `CompositeEditingTests`, `TemplatePackExportTests` (T-CAT-008), `TemplateExamplesTests`, `ImportSizeLimitTests`: cobrem caminho feliz e limites.
- **Lacuna crítica:** nenhum teste de round-trip de pesos, tags, alias por campo, `required`, peso de registro.
- `MappingLearningTests`: presets e regras; sem teste da divergência cru × compacto.
- `TemplatesFlowTests` (T-UI-FLOW-002) depende de `AutomationProperties.Name`: "Código do template", "Nome do template", "Valores do template", "Salvar template", "Script ou pack a importar"; botões "Analisar", "Importar", "Exemplo escalar (lista de valores)"; textos "Template 'X' salvo", "Análise ok", "Importado:". **Será quebrado pela refatoração e deve ser migrado, nunca removido.**
- `SmokeTests` espera o título "Template Catalog".

## 4. Restrições do schema (V0001__init.sql)

`template.code` `^[A-Z][A-Z0-9_]{1,63}$`; `template_field.field_code` minúsculo, `UNIQUE(template_id, ordinal)` e `(template_id, field_code)`; `template_value UNIQUE(template_id, value)`, peso > 0; `template_record UNIQUE(template_id, record_key)`, `data` JSON objeto; alias: peso (0,2], índice único `(template_id, ifnull(field_id,0), normalized_alias)`; `generator_preset.code` maiúsculo 2–64, `generator_version` INTEGER; `mapping_rule.scope IN ('global','engine')`; triggers protegem embutidos.

## 5. Divergências doc × código registradas

1. `ContentHash`: docs/13 §2 descreve `value\u001Fweight\u001E`; o código usa SHA-256 de JSON canônico (`TemplateDataSet.Create`). **Código prevalece**; corrigir doc depois.
2. docs/13 §8 exige exportar presets/regras/licença; o exportador não faz.
3. docs/21 S07 prevê abas "Templates | Presets | Regras de Mapeamento | Packs"; a UI atual não tem abas.
4. Merge: spec padrão InsertOnly; UI usa Upsert.
5. `br.cpf.validOnly` é declarado e **ignorado** no factory (`BuiltInGenerators.cs`).
6. `MergeInto` em InsertOnly acrescenta valores/aliases a templates existentes (INSERT OR IGNORE nos loops); Upsert nunca remove valores obsoletos; campos de composto usam INSERT OR IGNORE (mudança estrutural ignorada).

## 6. Escopo não examinado

Não foram lidos linha a linha: internals do `TemplateLoader` (cache) e partes de `StagingImporter` além do fluxo de análise/merge. O `template-pack-1.0.schema.json` foi lido na revisão: já prevê `required`, peso de registro, `presets`, `rules` e exige `pack.license`; portanto os defeitos 16/17 são do exportador/instalador, não do contrato. Pontos não lidos constam como pressupostos em `06`.
