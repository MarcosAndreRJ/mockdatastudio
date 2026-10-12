# Inventário da Interface do Usuário (UI)

| Campo | Valor |
|---|---|
| Objetivo | Mapear o estado do protótipo visual inicial (`src/MockDataStudio.App`) contra a especificação S01–S19 de [docs/21](21-user-interface-ux.md), documentando divergências e decisões. |
| Data | 2026-10-12 (Sprint 0 / Etapa 0.2) |
| Relacionados | [docs/21](21-user-interface-ux.md), [docs/32](32-decisions-and-assumptions.md) |

---

## 1. Visão Geral do Protótipo Existente

O protótipo inicial foi implementado em WPF (`net10.0-windows`) com base direta nos mockups aprovados (`images/`).
Características gerais do protótipo:
- **Janela e Shell**: `MainWindow.xaml` com custom chrome (`WindowChrome`), barra superior com busca global (`SearchField`), notificações, botão de ajuda, perfil de usuário ("JD / John Doe") e botões de controle da janela (Minimizar, Maximizar/Restaurar, Fechar).
- **Navegação Lateral**: `ListBox` com ícones Segoe MDL2 Assets e itens: `Home`, `Connections`, `Explorer`, `SQL Editor`, `Schema Digest`, `Templates`, `Mapping`, `Generation`, `History`, `Settings`.
- **Estilo e Tema**: Paleta moderna e limpa em `Themes/Colors.xaml` e componentes base em `Themes/Styles.xaml`.
- **Origem dos Dados**: Dados estáticos mockados centralizados em `Samples/SampleData.cs`, vinculados via `x:Static` no XAML sem ViewModels ou injeção de dependência.

---

## 2. Matriz de Mapeamento: Views do Protótipo × Telas da Especificação (S01–S19)

| Especificação (docs/21) | View / Controle no Protótipo | Status Atual | Divergências Encontradas | Decisão Técnica (DA) |
|---|---|---|---|---|
| **S01 — Início** | `Views/HomeView.xaml` | Implementado (protótipo) | Rótulos da interface em inglês ("Workspace Overview", "Recent Projects", "Quick Actions", "Connections", "Recent Activity"); subtítulos em pt-BR. Dados via `SampleData.RecentProjects`, `Connections`, `RecentActivity`. | Preservar layout visual idêntico ao mockup. Migrar textos para `Strings.resx` (pt-BR padrão) no Sprint 1. Criar `HomeViewModel`. (DA-026) |
| **S02 — Conexões** | `Views/ConnectionsView.xaml` | Implementado (protótipo) | Rótulos e botões em inglês ("New Connection", "Import", "Refresh All", "Filter"). Indicadores numéricos fixos. Logos para SQLite, PostgreSQL, MariaDB e Firebird — ausência de logo próprio para MySQL (`mariadb.png` está no lugar de MySQL). | Criar `ConnectionsViewModel`. Adicionar logo oficial do MySQL (`mysql.png`) preservando `mariadb.png` para conexões MariaDB (DA-025). Externalizar textos para `Strings.resx`. |
| **S03 — Explorer de Banco** | `Views/ExplorerView.xaml` (painel esquerdo) | Implementado (agrupado) | No protótipo atual, `ExplorerView` contém tanto o Explorer de objetos (S03), quanto o Editor SQL com AvalonEdit (S04), o Inspetor de Detalhes da tabela (S05) e a Grade de Resultados. | Separar a navegação de "SQL Editor" em uma `SqlEditorView` dedicada no Sprint 1 (DA-024). O Explorer manterá o foco na navegação e inspeção rápida da estrutura do banco. |
| **S04 — Editor SQL** | `Views/ExplorerView.xaml` (painel central) | Implementado (compartilhado) | O item "SQL Editor" do menu lateral navega para a mesma `ExplorerView` (`"explorer" or "sql" => new ExplorerView()`). Usa `AvalonEdit` com realce `sql.xshd`. | Criar `SqlEditorView` própria no Sprint 1 (DA-024), extraindo o `AvalonEdit` e a barra de execução SQL, mantendo estilo e sintaxe. |
| **S05 — Inspetor de Tabela** | `Views/ExplorerView.xaml` (painel direito) | Implementado (embutido) | Detalhes da tabela (colunas, índices, FKs, preview) aparecem como painel lateral retrátil dentro de `ExplorerView`. | Manter como painel de inspeção rápida no Explorer e preparar modelo independente no Sprint 12. |
| **S06 — Schema Digest** | `Views/SchemaDigestView.xaml` | Implementado (protótipo) | Rótulos em inglês ("Extract Digest", "Compare Versions", etc.). Exibe snapshots históricos, visualizador em árvore e diff de versões fixo com cores de severidade. | Migrar para `SchemaDigestViewModel` no Sprint 1. Ligar ao extrator e serializador real no Sprint 10. |
| **S07 — Catálogo** | `Views/TemplatesView.xaml` | Implementado (protótipo) | "Template Catalog" exibe lista de templates, estatísticas, tags, categorias e valores de amostra. | Criar `TemplatesViewModel`. No Sprint 1, dados via `IDesignDataSource`; no Sprint 10/17, conectar ao SQLite `catalog.db`. |
| **S08 — Editor de Template** | `Views/TemplatesView.xaml` (painel de detalhes) | Parcial | Apenas visualização de valores e metadados no painel lateral; não possui diálogo completo de edição inline. | Será expandido para CRUD completo na etapa 17.1 (Sprint 17). |
| **S09 — Importar Script** | — | Não implementado | Botão "Import SQL" existe em `TemplatesView`, mas sem tela de staging isolado. | Implementar no Sprint 17 (etapa 17.2) com editor e schema de staging. |
| **S10 — Workspace do Projeto** | — | Não implementado | Não há view isolada; no protótipo o fluxo inicia diretamente na Home ou Mapping. | Implementar no Sprint 10 (etapa 10.6 e fluxo de projeto). |
| **S11 — Mapeamento da Tabela** | `Views/MappingView.xaml` | Implementado (protótipo) | Exibe colunas da tabela "pedido", geradores sugeridos, badges de confiança (High/Medium/Low) e painel lateral com pontuação detalhada de sugestão. Textos em inglês e dados estáticos. | Criar `MappingViewModel` no Sprint 1. Externalizar textos para `Strings.resx`. Conectar com `MappingService` real no Sprint 10. |
| **S12 — Configuração de Gerador** | `Views/MappingView.xaml` (painel direito) | Parcial | Exibição de pontuação e parâmetros fixos. Falta formulário gerado dinamicamente a partir dos descriptors. | Implementar componente `GeneratorEditor` no Sprint 10/12. |
| **S13 — Relacionamentos** | — | Não implementado | Não existe tela dedicada para gerenciamento de ciclos e cardinalidade de FKs. | Implementar no Sprint 16 (etapa 16.6). |
| **S14 — Plano e Validação** | `Views/PlaceholderView.xaml` ("Generation") | Placeholder | Navegação "Generation" exibe `PlaceholderView("Generation")`. | Implementar no Sprint 10 (etapa 10.5). |
| **S15 — Preview** | `Views/PlaceholderView.xaml` / Embutido | Placeholder / Parcial | Preview parcial embutido em `MappingView` e `ExplorerView`. Tela cheia de preview ainda é placeholder. | Implementar no Sprint 10 (etapa 10.5). |
| **S16 — Execução** | `Views/PlaceholderView.xaml` ("Generation") | Placeholder | Configurações de saída e execução fazem parte do placeholder de geração. | Implementar no Sprint 10 (etapa 10.5). |
| **S17 — Histórico de Execuções** | `Views/PlaceholderView.xaml` ("History") | Placeholder | Navegação "History" exibe `PlaceholderView("History")`. | Implementar no Sprint 15 (etapa 15.5). |
| **S18 — Configurações** | `Views/PlaceholderView.xaml` ("Settings") | Placeholder | Navegação "Settings" exibe `PlaceholderView("Settings")`. | Implementar no Sprint 19 (etapa 19.2). |
| **S19 — Grafo de Relacionamentos** | — | Fora do escopo V1 | Recurso previsto para V2. | V2. |

---

## 3. Controles Customizados e Temas

| Arquivo | Finalidade | Diferenças com a Especificação | Decisão |
|---|---|---|---|
| `Controls/SearchField.xaml` | Caixa de busca com atalho `Ctrl+K` e ícone de lupa | Atende bem a S01/S02/S03/S07. | Manter. Integrar futuramente à Paleta de Comandos (`Ctrl+Shift+P`). |
| `Controls/TreeViewExtensions.cs` | Helper de seleção e expansão para WPF `TreeView` | Atende à navegação em árvore. | Manter. |
| `Themes/Colors.xaml` | Paleta de cores base (`WindowBgBrush`, `SurfaceBrush`, `AccentBrush`, etc.) | Utiliza nomes convencionais (`AccentBrush`, `WarningBrush`) em vez dos tokens semânticos exatos de docs/21 §2.1 (`Bg.Base`, `Env.Development`, `Conf.High`). | No Sprint 1.5, mapear os tokens formais em `Themes/Tokens.xaml` mantendo compatibilidade com as cores aprovadas. |
| `Themes/Styles.xaml` | Estilos para botões, caixas de texto, cartões, tipografia | Estilos modernos alinhados com o protótipo. | Preservar 100% da identidade visual. Criar novos estilos complementares (`EnvironmentChip`, `SeverityBadge`, `ConfidenceBadge`) conforme docs/21 §2.2. |

---

## 4. Recursos Visuais e Ativos

| Ativo | Localização | Observação | Decisão |
|---|---|---|---|
| `sql.xshd` | `Resources/Highlighting/sql.xshd` | Definição de sintaxe para o AvalonEdit. | Manter; expandir futuramente para suportar particularidades por dialeto. |
| Logos SGBD | `Resources/Logos/*.png` | Possui `sqlite.png`, `postgresql.png`, `mariadb.png`, `firebird.png`. | Adicionar `mysql.png` para representar o suporte oficial a MySQL. (DA-025) |
| Ícones de Fontes | Segoe MDL2 Assets | Usados extensivamente para glifos consistentes no Windows. | Manter. Segoe MDL2 Assets é padrão nativo no Windows 10 e 11. |

---

## 5. Resumo das Decisões Técnicas Registradas (em docs/32)

1. **DA-023**: Migração formal da solução para `MockDataStudio.slnx` nativo do .NET 10.
2. **DA-024**: Separação de `SqlEditorView` e `ExplorerView` no Sprint 1, mantendo o controle AvalonEdit funcional e idêntico visualmente.
3. **DA-025**: Adição de `mysql.png` aos recursos para MySQL oficial, mantendo `mariadb.png` para suporte a MariaDB.
4. **DA-026**: Externalização completa dos rótulos de navegação e telas para `Strings.resx` (pt-BR) no Sprint 1, preservando a identidade visual dos mockups.
