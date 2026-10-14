# 05 — Plano de testes e aceitação

## 1. Matriz de testes

Projetos: `Catalog.Tests` (CT), `Application.Tests` (AT), `App.UiTests` (UI), VM-tests em `App.Tests`/projeto existente de VMs (VT; criar se não houver).

| Área | Caso | Nível | Tarefa |
|---|---|---|---|
| Escalar | Criar, editar, round-trip de pesos/tags/aliases (peso e por campo) | CT | TC-001 |
| Composto | Criar, editar, remover campo, adicionar campo obrigatório, round-trip de peso | CT | TC-001/502 |
| Integridade composta | N amostras `catalog.template.field`: toda linha (CEP,Cidade,UF) pertence a um registro | CT/Generation.Tests | TC-507 |
| Exclusão | Impacto (valores/aliases/projetos); bloqueio embutido | AT/CT | TC-007 |
| Duplicação | Clone preserva pesos/aliases; código novo único | CT | TC-001 |
| Somente leitura | Embutido: sem Editar; Load falha `CatBuiltinReadOnly`; detalhes ok | CT+VT | TC-202 |
| Aliases | Duplicados, normalização, por campo | CT | TC-001 |
| Validação | Código inválido/duplicado, tipo por célula, vazios, obrigatórios | AT | TC-008 |
| Wizard | Navegar entre etapas sem persistir (contagem de linhas no catálogo = antes) | VT | TC-301 |
| Cancelar / não salvo | Guarda de navegação e Esc | VT | TC-104/304 |
| Import SQL | Análise sem gravar; só INSERT/UPDATE/DELETE; bloqueia DDL/PRAGMA/ATTACH | CT | TC-004/601 |
| Import JSON | Pack válido/inválido, legado, limite de tamanho | CT | TC-003 |
| Merge | InsertOnly não altera existente; Upsert; Replace com confirmação; desfazer restaura | CT | TC-602 |
| Export | Seleção, `required`/peso, idempotência export→import→export, embutidos fora | CT | TC-003/603 |
| Presets | Código maiúsculo, versão inteira, JSON, embutido RO, no-op falha | AT/CT | TC-005/701 |
| Regras aprendidas | Listar, esquecer, casamento cru × compacto | AT | TC-703/006 |
| Mapping | `core.preset` resolve após edição de preset; binding antigo preservado (DA-306) | AT | TC-803 |
| Generation | Templates editados geram; `ContentHash` muda só se conteúdo mudar | Generation.Tests | TC-803 |
| Teclado | Tab/Enter/Esc/Ctrl+S/Ctrl+N; foco inicial por etapa | UI | TC-802 |
| Estados | Vazio, sem resultados, erro de salvamento, carregando | VT+UI | TC-103 |
| Regressão contratos | T-CAT-008, `CatalogTests`, `SmokeTests`, T-UI-FLOW-002 migrado | todos | TC-801 |

Regra: nenhum teste é removido; testes migrados mantêm a intenção (comentário `// migrado de …`).

## 2. Definition of Done por tarefa

Build limpo sem warnings novos, testes da tarefa + suíte completa verdes, sem acesso a repositório na View, strings em resx, `AutomationProperties.Name`, evidência anexada ao PR (saída de teste e, para UI, captura).

## 3. Evidências por fase

F0: diff de teste round-trip; F1: gravação de troca de abas; F3: captura dos 4 passos; F5: teste de integridade; F6: relatório de importação + restauração; F8: log da suíte completa.

## 4. Critérios de aceitação de UX (observáveis)

Teste de usabilidade com ≥ 5 pessoas sem conhecimento prévio, roteiro cronometrado, sem ajuda; ou, na ausência de pessoas, roteiro automatizado de UI como critério mínimo (não substitui o teste humano).

| # | Capacidade | Métrica de aprovação |
|---|---|---|
| 1 | Entender a finalidade | ≥ 4/5 descrevem corretamente o catálogo em 1 frase em ≤ 30 s (título + subtítulo visíveis sem rolagem) |
| 2 | Achar "criar template" | ≤ 10 s e ≤ 1 clique; botão visível sem rolagem em 1366×768 |
| 3 | Escolher escalar × composto | ≥ 90% escolhem corretamente para "lista de profissões" e para "endereço" |
| 4 | Criar "Profissões" | ≤ 3 min; zero erros bloqueantes; 10 valores gravados |
| 5 | Informar valores sem documentação | 0 consultas a docs; dicas na tela suficientes (observador) |
| 6 | Criar endereço CEP/cidade/UF | ≤ 6 min; 3 registros; teste automatizado de coerência passa |
| 7 | Entender aliases | ≥ 4/5 explicam o propósito após ler o helper text |
| 8 | Revisar e salvar | Etapa de revisão visível; salvar ≤ 2 cliques; toast de sucesso |
| 9 | Editar template criado | ≤ 1 min para alterar 1 valor, sem perda de dados (hash só muda pelo valor) |
| 10 | Importar × exportar sem confusão | ≥ 90% executam a operação certa na 1ª tentativa; botões distintos e separados por colunas/títulos |

Automáticos (UI test): fluxos 4, 6, 8, 9, 10 com contagem de cliques/passos e asserções de estado.

Aceitação final exige: matriz verde, critérios 1–10, ausência de bloqueios abertos em `06`, e aprovação expressa do usuário.
