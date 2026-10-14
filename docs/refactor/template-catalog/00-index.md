# Template Catalog — Plano de refatoração (índice e resumo executivo)

**Status:** plano concluído, **aguardando aprovação expressa do usuário**. Nenhum código, XAML, schema ou branch foi alterado.
**Classificação:** **Aprovado com ressalvas** (ver §3).
**Branch proposta (não criada):** `feature/template-catalog-refactor`.

## 1. Resumo

A tela `TemplatesView.xaml` concentra seis responsabilidades num único `TemplatesViewModel` de 704 linhas e é longa demais. O plano a divide em quatro abas (Templates, Importar/Exportar, Presets, Regras aprendidas), adiciona assistente de 4 etapas, editores escalar e composto em grade, e importação/exportação com análise prévia — **sem mudar schema nem contratos** consumidos por Mapping e Generation.

Achados que mudam o plano:
1. **Editar hoje perde dados** (pesos, tags, alias por campo): os editores descartam o que o `TemplateDraft` já suporta → TC-001 é pré-requisito.
2. Compostos aparecem com contagem 0 (`ListAsync`).
3. Imagem 6 mostra coluna "Parâmetros" que não existe no domínio → substituída por "Peso".
4. Imagem 8 cita `br.telefone`/`br.data` inexistentes, versão "1.0.0" (é inteira) e código `cpf-valido` (deve ser maiúsculo).
5. O schema do pack exige `license` e `version`; a imagem 7 não prevê esses campos.
6. `TemplatesFlowTests` (T-UI-FLOW-002) vai quebrar e deve ser migrado.

## 2. Índice

| Doc | Conteúdo |
|---|---|
| [01](01-current-state-audit.md) | Auditoria, matriz de funcionalidades, divergências doc×código |
| [02](02-ux-functional-specification.md) | Comparação com as 8 imagens, telas, wizard, estados |
| [03](03-architecture-impact.md) | Componentes, contratos, persistência, posse de arquivos |
| [04](04-implementation-plan.md) | Fases 0–8, tarefas TC-xxx, ordem, arquivos |
| [05](05-test-and-acceptance-plan.md) | Matriz de testes e 10 critérios mensuráveis |
| [06](06-open-decisions-and-risks.md) | Decisões D-01..D-12, riscos, pressupostos |
| [../../plano_de_refatoracao.md](../../plano_de_refatoracao.md) | Roteiro consolidado para a IA executora |

## 3. Prontidão

**Aprovado com ressalvas.**
- Pode iniciar: Fase 0 (TC-001..009) e Fase 1.
- Ressalvas: (a) TC-001 antes de qualquer edição visual; (b) decisões D-01, D-04, D-05, D-11, D-12 confirmadas pelo usuário; (c) edições em arquivos compartilhados (Shell, ServiceRegistration, Strings.resx, Styles) coordenadas com as outras trilhas; (d) itens não examinados em `06` §4 verificados nas tarefas indicadas.
- Não há bloqueio impeditivo.

## 4. Revisão independente (§17 do prompt)

| Pergunta | Resultado |
|---|---|
| Funcionalidades esquecidas? | Incluídas: duplicar, excluir, alias por campo, tags, embutidos RO, desfazer importação, cópia de hash/código, instruções para IA, exemplos. Adicionado impacto de exclusão e busca por alias/tag |
| Imagem incompatível? | Imagens 3 (tipo lógico livre), 6, 7 (embutido exportável, checkbox redundante), 8 tratadas em `02` §1 |
| Estados cobertos? | Sim (`02` §9) |
| Dependências circulares? | Não (`04` — Ordem) |
| Tarefas vagas? | Cada tarefa lista arquivos e teste; detalhes finais (assinaturas) são decisão do executor dentro dos contratos |
| Perda de dados? | Principal risco existente; mitigado por TC-001 e testes de round-trip |
| Quebra de Mapping/Generation? | Contratos intocados; TC-803/507 |
| Composto coerente? | Registro atômico; TC-507 |
| Importação segura? | Só staging; padrão InsertOnly; testes de rejeição |
| Conflitos com branches? | Arquivos novos próprios; compartilhados editados de forma aditiva (`03` §6) |
| Decisões com alternativas? | Sim (`06`) |

**Limite honesto:** `TemplateLoader` interno e trechos de `StagingImporter` não foram lidos integralmente; não se declara cobertura total do código.
