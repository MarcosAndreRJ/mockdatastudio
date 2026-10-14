# Plano de refatoração — Template Catalog (roteiro para a IA executora)

> Documento consolidado. Detalhes completos em `docs/refactor/template-catalog/00..06`. **Não iniciar sem aprovação expressa do usuário.** Branch: `feature/template-catalog-refactor` (criar só após aprovação, a partir da `main` atualizada).

## Regras de execução

1. Hierarquia: código real > docs/contratos > imagens (`images/template/*`, referência de UX) > objetivo do produto. Registrar divergências; não escolher em silêncio.
2. **Não** alterar schema (`V0001`), contratos de Mapping/Generation, nem apagar funcionalidades ou testes. Testes quebrados pela UI são **migrados**.
3. MVVM com `RelayCommand`/`AsyncRelayCommand` próprios (sem CommunityToolkit), code-behind vazio, DI em `ServiceRegistration`, strings em `Strings.resx` (prefixo `Templates.`), `AutomationProperties.Name` em tudo.
4. Arquivos compartilhados (`ShellViewModel`, `ServiceRegistration`, `Strings.resx`, `Styles.xaml`, `App.xaml`, `Contracts.cs`, `MappingService`): somente edição aditiva, commits pequenos, rebase frequente. Preferir arquivos novos (`Themes/TemplateCatalog.xaml`, `ServiceRegistration.Templates.cs`).
5. Nada é gravado no catálogo durante a navegação do assistente; só em Salvar.
6. Um PR por fase; `dotnet build` e `dotnet test` verdes a cada commit.
7. Importação SQL: exclusivamente via `StagingImporter` (isolado); padrão `InsertOnly`.

## Decisões a confirmar antes (recomendação entre parênteses)

D-01 coluna Peso no lugar de "Parâmetros" (sim) · D-04 embutidos não exportáveis (sim) · D-05 InsertOnly + Upsert confirmado + Replace tipado (sim) · D-11 novo projeto `tests/MockDataStudio.App.Tests` (sim) · D-12 licença/versão do pack com padrão editável (sim) · demais em `06`.

## Defeitos que a refatoração NÃO pode propagar

- Editar perde pesos de valores/registros, tags, alias por campo, `required` (editores descartam o que `TemplateDraft` suporta) → **TC-001**.
- Lista mostra 0 para compostos → TC-002.
- Export/import perdem `required` e peso de registro; schema exige `pack.license` e `pack.version` → TC-003/603.
- `GeneratorPresetRepository.UpsertAsync` Ok em no-op → TC-005.
- `br.cpf.validOnly` é ignorado pelo factory; `br.telefone`/`br.data` não existem — não prometer na UI.

## Tarefas (obrigatórias [O], opcionais [M])

**Fase 0 — Integridade (primeiro)**
TC-001 [O] editores preservam draft completo · TC-002 [O] contagem de compostos · TC-003 [O] export/import sem perdas · TC-004 [O] análise com lista de templates/conflitos · TC-005 [O] preset repo falha em no-op · TC-006 [M] normalizar `ColumnPattern` (D-07, toca Mapping) · TC-007 [O] impacto de exclusão · TC-008 [O] `TemplateDraftValidator` · TC-009 [O] busca por alias/tag.

**Fase 1 — Navegação**
TC-101 [O] fatiar `TemplatesViewModel` (+ projeto `App.Tests`) · TC-102 [O] 4 abas · TC-103 [O] estados · TC-104 [O] `INavigationGuard` · TC-105 [O] resx e automação.

**Fase 2 — Detalhes/edição**
TC-201 [O] `TemplateDetailsView` · TC-202 [O] ações (Editar/Duplicar/Excluir com impacto) · TC-203 [O] lista (badge, contagem, selo) · TC-204 [M] prévia de geração · TC-205 [O] edição via wizard (código não editável).

**Fase 3 — Assistente**
TC-301 [O] VM + stepper + draft em memória · TC-302 [O] passos 1–2 · TC-303 [O] revisão/salvar/salvar e criar outro · TC-304 [O] cancelar e não salvo · TC-305 [O] compat. com `TemplatesFlowTests`.

**Fase 4 — Escalar**
TC-401 [O] editor com linhas numeradas/prévia/validação · TC-402 [O] colar TSV e pesos · TC-403 [M] modo grade.

**Fase 5 — Composto**
TC-501 [O] grade `CompositeRow` · TC-502 [O] campos (add/remover/reordenar com confirmação) · TC-503 [O] coluna Peso · TC-504 [O] importar TSV · TC-505 [M] arrastar/combos · TC-506 [O] sub-abas Configuração/Aliases/Prévia · TC-507 [O] teste de coerência por registro.

**Fase 6 — Importar/Exportar**
TC-601 [O] importação com análise e modos · TC-602 [O] relatório + desfazer · TC-603 [O] exportação seletiva (versão/licença) · TC-604 [M] presets/rules no pack.

**Fase 7 — Presets e Regras**
TC-701 [O] presets (validação, combo de geradores) · TC-702 [O] sugestões reais · TC-703 [O] regras aprendidas (listar/esquecer) · TC-704 [M] schema de parâmetros.

**Fase 8 — Estabilização**
TC-801 [O] migrar `TemplatesFlowTests`/`SmokeTests` + fluxos profissões/endereço · TC-802 [O] teclado/acessibilidade · TC-803 [O] regressão Mapping/Generation · TC-804 [O] atualizar docs 13/21 (hash `ContentHash` = SHA-256 de JSON canônico, não `value\u001Fweight`) · TC-805 [M] telemetria local.

Dependências: `TC-001→TC-003`; `TC-008→TC-301`; `TC-101→TC-102→{103,104,201…}`; `TC-004→TC-601`; `TC-007→TC-202`; `TC-501→{502,503,504}`. Fases 0 e 1 em paralelo.

## Regras de UX essenciais

- Abas: Templates | Importar/Exportar | Presets de gerador | Regras aprendidas; seleção preservada.
- Wizard 4 passos (Tipo, Informações, Dados, Revisão), validação inline, código único assíncrono, cancelar com confirmação.
- Composto: grade com registros atômicos (nunca misturar campos de linhas), remover campo com confirmação.
- Importar e Exportar em colunas separadas; análise não grava; Importar só após análise sem erro; desfazer disponível.
- Presets: código maiúsculo, versão inteira, JSON objeto, embutidos RO, aviso DA-306. Regras aprendidas: listar/filtrar/esquecer (sem criação manual).
- Estados: vazio, carregando, sem resultados, edição, salvando, sucesso, falha, inválido, não salvo, somente leitura.

## Arquivos

Modificar: `TemplatesView.xaml(.cs)`, `TemplatesViewModel.cs`, `ShellViewModel.cs`, `ServiceRegistration.cs`, `Strings.resx`, `TemplateEditor.cs`, `CompositeTemplateEditor.cs`, `TemplateRepository.cs`, `OtherRepositories.cs`, `StagingImporter.cs`, `TemplateImportService.cs`, `TemplatePackExporter.cs`, `BuiltInPackInstaller.cs`, `TemplateBrowser.cs`, `MappingLearningService.cs`, `TemplatesFlowTests.cs`, `SmokeTests.cs`, docs 13/21.
Criar: `Views/Templates/*`, `ViewModels/Templates/*`, `Controls/{StepperControl,ChipListInput,HelpCard,LineNumberedTextBox}`, `Themes/TemplateCatalog.xaml`, `Application/Templates/TemplateDraftValidator.cs`, `tests/MockDataStudio.App.Tests`.

## Critérios de aceite de UX

Ver `05-test-and-acceptance-plan.md` §4: 10 métricas (entender finalidade ≤30 s; criar template ≤1 clique; escolha escalar×composto ≥90%; profissões ≤3 min; endereço ≤6 min com coerência testada; aliases ≥4/5; revisar/salvar ≤2 cliques; editar ≤1 min sem perda; import×export ≥90% corretos).

## Resultado

**Aprovado com ressalvas.** Começar por Fase 0/Fase 1 após aprovação. Itens não lidos integralmente (`TemplateLoader`, partes de `StagingImporter`) devem ser lidos nas tarefas TC-004/TC-601 antes de alterar.
