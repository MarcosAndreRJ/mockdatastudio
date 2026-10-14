# 02 — Especificação UX e funcional

Imagens: `images/template/ChatGPT Image Oct 13, 2026, 11_00_4x–5x PM-N.png` (N = 1..8). São **referência de UX**, não contrato.

## 1. Comparação tela atual × imagens

| Imagem | Tela atual | Lacuna | Divergência com o domínio |
|---|---|---|---|
| 1 Visão geral | Cards empilhados, formulário no topo, rolagem longa | Abas, cabeçalho de detalhes, ações Editar/Duplicar/Mais, cards "Como usar" e "Importante" | "Endereço completo — Composto — 0": contagem 0 é bug (`ListAsync`), contar registros. Origem "CORE_PTBR (embutido)": exibir; hash copiável OK (`ContentHash`) |
| 2 Tipo | Não existe | Assistente | Nenhuma |
| 3 Informações | `FormLogicalType` livre | Campos com ajuda, chips de alias | Código da imagem "PROFISSAO" ok; **Tipo lógico deve ser lista fechada** (tipos do domínio) |
| 4 Dados escalar | TextBox simples | Números de linha, dicas, prévia | Sem divergência; acrescentar coluna opcional "Peso" (domínio tem `weight`) |
| 5 Revisão | Não existe | Resumo/amostra | "Salvar e criar outro" é compatível |
| 6 Composto | Texto `a\|b\|c` | Grade, sub-abas | **Coluna "Parâmetros (tipo: capital)" não existe no domínio**; atributos reais por registro: `weight` e `record_key`. Substituir por "Peso" e chave somente leitura avançada |
| 7 Import/Export | Cards misturados na tela | Área própria | Abas internas "SQL/Pack" + checkbox "É um pack JSON" são redundantes: manter só uma. **Exportar embutido: exportador só exporta não-embutidos** → desabilitar embutidos com tooltip (ou decisão D-04) |
| 8 Regras/Presets | Cards no fim da tela | Abas próprias | `br.telefone`/`br.data` inexistentes (A5 prevê `br.phone`); parâmetros reais `formatted`/`validOnly`; versão é inteira (imagem "1.0.0"); código de preset maiúsculo (`cpf-valido` inválido → `CPF_VALIDO`); título "Presets sugeridos" enganoso; **não misturar presets dentro da aba Regras aprendidas** |

## 2. Estrutura de navegação

Cabeçalho: título "Template Catalog", botão Atualizar. Abas (UnderlineTabControl existente): **Templates | Importar/Exportar | Presets de gerador | Regras aprendidas**. Seleção de template e filtros preservados ao trocar de aba (VM singleton; estado em `TemplateCatalogViewModel`).

Rotas internas: Catálogo (lista + detalhes + valores) → Assistente (substitui a área central; botão Voltar) → Editor de edição (mesmo componente do assistente em modo `Edit`, com composto usando sub-abas).

Regras:
- Navegar para fora com rascunho sujo exige `ConfirmDialog` (Salvar / Descartar / Continuar editando). Requer ponto de extensão no `ShellViewModel` (`INavigationGuard`).
- Máx. 2 cliques da aba até "Novo template"; botão primário sempre visível na lista.

## 3. Tela Catálogo (Templates)

- Lista: busca (código/nome/alias/tag), ícone, badge **Escalar/Composto**, contagem (valores ou registros), selo **Embutido**, botão "Novo template".
- Detalhes: título, badges, código (copiar), botões **Editar** (embutido: oculto), **Duplicar**, **Mais ações** (Excluir, Exportar este, Copiar código). Embutido mostra "Clonar para editar".
- Card "Informações": código, nome, tipo, tipo lógico, total, origem (pack + locale), tags, hash (copiar).
- Cards "Como usar" (4 passos) e "Importante" (aviso).
- "Exemplos de valores": chips (composto: prévia de linhas).
- Coluna direita "Valores (N)" com busca, copiar por linha; composto: mini-grade somente leitura.
- Exclusão: diálogo com nº de valores/aliases; `TypedConfirmDialog` para templates com `valueCount` ≥ 100 (decisão D-06); impacto em projetos recentes (opcional TC-206).

## 4. Assistente (4 etapas)

Stepper numerado 1–4; Voltar/Continuar; **nenhuma persistência até "Salvar"** (rascunho em memória: `TemplateDraft`).

| Etapa | Escalar | Composto |
|---|---|---|
| 1 Tipo | Dois cartões radio (padrão: nenhum selecionado, Continuar desabilitado) | idem |
| 2 Informações | Nome*, Código*, Descrição, Tipo lógico (lista), Aliases (chips) | Sem tipo lógico (cada campo tem o seu) |
| 3 Dados | Editor com números de linha, colar listas, remover vazias/duplicadas, peso opcional, prévia chips | Definição de campos (código, tipo, tamanho, obrigatório) + grade de registros + importar planilha (TSV colado) |
| 4 Revisão | Resumo, contagem, amostra, avisos/erros, "Salvar" e "Salvar e criar outro" | Resumo + tabela completa de registros + campos |

Validações:
- Nome obrigatório (≤120). Código: auto-sugerido a partir do nome (maiúsculo, `_`), regex `^[A-Z][A-Z0-9_]{1,63}$`, único (consulta ao catálogo, debounce 300 ms, mensagem "Já existe um template com este código").
- Tipo lógico: valor obrigatório de lista fechada.
- Escalar ≥ 1 valor; duplicatas removidas com aviso "N duplicados removidos"; valores inválidos ao tipo → aviso por linha (não bloqueia, como no staging).
- Composto ≥ 1 campo e ≥ 1 registro; `field_code` `[a-z][a-z0-9_]*` único; cada célula validada ao tipo do campo; obrigatório sem NULL.
- Aliases: normalizar, sem duplicata, peso padrão 1.

Cancelar: se `IsDirty`, confirmar descarte. Falha ao salvar: permanece na revisão com `DiagnosticsList`; nada é perdido.

## 5. Editor composto (edição e etapa 3)

Sub-abas **Campos e registros | Configuração | Aliases | Prévia**.
- Grade (DataGrid sobre `ObservableCollection<CompositeRow>` com células por campo; colunas geradas dinamicamente a partir dos campos).
- Colunas: seleção, campos, **Peso** (opcional), lixeira. Células com combo para tipos enumeráveis (ex.: UF).
- Botões: Adicionar registro, Remover selecionados, Importar da planilha (colar TSV com cabeçalho opcional → mapeamento de colunas por nome).
- Ordem preservada (`record_key` = posição na gravação, igual ao atual; reordenar permitido por arrastar → TC-505 opcional).
- Linhas permanecem atômicas: o registro é um único `TemplateRecord`; nunca há edição que misture campos entre linhas; ordenação de coluna reordena a linha inteira.
- Remover campo: confirmação "N registros perderão este dado"; adiciona campo: células nulas por padrão (se obrigatório, bloqueia salvar até preencher).
- Configuração: lista de campos reordenável (ordinal), tipo, tamanho, obrigatório.
- Aliases: aliases do template e **por campo** (`FieldCode`), preservando os existentes.
- Prévia: simula 10 sorteios com `catalog.template` mostrando linhas completas (somente leitura).
- Painel "Dicas" (4 itens, como imagem 6).

## 6. Importar / Exportar

Duas colunas independentes.

**Importar** (máquina de estados: Vazio → Entrada → Analisando → Analisado(ok/avisos/erros) → Importando → Concluído/Falha):
1. Formato: **seletor único** "Instruções SQL | Pack JSON" (detecção automática por conteúdo; remover o checkbox redundante).
2. Entrada: colar texto ou "Escolher arquivo…" (limite existente em `ImportSizeLimitTests`).
3. **Analisar** (não grava): exibe templates detectados (código, tipo, nº itens, existe? novo/atualiza), issues `CAT-*` por severidade, licença/autor quando houver.
4. Modo de mesclagem: padrão **InsertOnly**; "Atualizar existentes" (Upsert) com confirmação explícita; `ReplaceTemplateData` só atrás de `TypedConfirmDialog` (D-05).
5. **Importar** habilitado só após análise sem erro bloqueante; relatório final (criados/atualizados/ignorados/falhas) e ação "Desfazer última importação" (restaura backup premerge).
6. Botões: Copiar instruções para IA, Analisar, Importar, Descartar análise.
- Segurança: SQL **somente** via staging isolado (`StagingImporter`; só INSERT/UPDATE/DELETE); nunca no catálogo interno.

**Exportar:** código/nome do pack (validação regex), lista com checkbox, busca, selecionar todos, "N selecionados · total X valores/registros", botão Exportar → diálogo salvar arquivo; embutidos desabilitados (D-04); relatório de sucesso com caminho.

## 7. Presets de gerador (aba própria)

Quando usar: padronizar parâmetros de um gerador para reutilização no mapeamento (`core.preset`). Lista (embutidos somente leitura, selo) + formulário: Código (maiúsculo, validação em tempo real), Nome, Gerador (combo pesquisável do `GeneratorRegistry`), Versão do gerador (inteira), Parâmetros: editor JSON com validação de objeto e, quando disponível, schema de parâmetros do gerador. Ações: Novo, Carregar, Salvar, Excluir (confirma). Aviso: "Editar um preset não altera mapeamentos já feitos (DA-306: resolvido no momento do binding)". Lista de sugestões: somente para códigos que existem de fato.

## 8. Regras aprendidas (aba própria)

Quando usar: o sistema memoriza correções do usuário ("coluna `nm_cli` → template NOME"). Somente **listar, filtrar, esquecer** (e opcionalmente editar escopo). Estado vazio com "Como isso funciona?" (4 passos). Colunas: padrão de coluna, tabela, engine, destino (tipo/ref), confiança, escopo. Esquecer pede confirmação. **Não permitir criar manualmente** nesta fase (origem é o feedback no Mapping).

## 9. Estados e mensagens

| Estado | Comportamento |
|---|---|
| Vazio (sem templates do usuário) | Ilustração + "Crie seu primeiro template" + botão Novo |
| Carregando | `ProgressPanel` indeterminado; lista desabilitada |
| Sem resultados | "Nenhum template corresponde a '{termo}'" + Limpar busca |
| Edição / Criação | Cabeçalho "Editando X" / "Novo template"; botão Cancelar |
| Salvando | Botões desabilitados, spinner |
| Sucesso | Toast/banner `SeverityBadge` "Template 'X' salvo" e seleção do salvo |
| Falha | `DiagnosticsList` com causa (códigos `CAT-*`) |
| Inválido | Mensagem inline sob o campo + botão Salvar desabilitado com motivo |
| Não salvo | Indicador "•" no título + guarda de navegação |
| Somente leitura | Selo "Embutido", campos desabilitados, "Clonar para editar" |

Textos: todos em `Strings.resx` (pt-BR; coordenar com C1-r2).

## 10. Acessibilidade

`AutomationProperties.Name` em todo controle interativo (preservando os nomes já usados por `TemplatesFlowTests` ou migrando o teste), ordem de Tab lógica, atalhos (Ctrl+N novo, Ctrl+S salvar, Esc cancelar assistente, F2 renomear alias), foco inicial por etapa, contraste conforme `Colors.xaml`.

## 11. Critérios de UX → ver `05-test-and-acceptance-plan.md` §4.
