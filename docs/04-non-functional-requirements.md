# 04 — Requisitos Não Funcionais

| Campo | Valor |
|---|---|
| Objetivo | Requisitos de qualidade mensuráveis. |
| Escopo | Produto V1 (metas MVP indicadas quando diferentes). |
| Relacionados | [24](24-performance-scalability.md), [23](23-security.md), [26](26-testing-strategy.md) |

| ID | Categoria | Requisito | Métrica / verificação | Versão |
|---|---|---|---|---|
| NFR-PLAT-001 | Plataforma | Windows 10 22H2+ e Windows 11, x64; .NET 10 Desktop Runtime (ou self-contained) | Instalação em VMs limpas Win10/Win11 (checklist de release) | MVP |
| NFR-PLAT-002 | Plataforma | Funciona 100% offline | T-SEC-NET-001 | MVP |
| NFR-PLAT-003 | Plataforma | ARM64 Windows | Fora do escopo V1 (PA-006) | V2 |
| NFR-PERF-001..009 | Desempenho | Ver [24 §2](24-performance-scalability.md) | Benchmarks B-* | V1 (MVP: 001, 002) |
| NFR-REL-001 | Confiabilidade | Nenhuma perda de projeto por falha durante gravação | T-PRJ-006 | MVP |
| NFR-REL-002 | Confiabilidade | Catálogo íntegro após falha durante merge/migração (transação + backup) | T-CAT-005, T-CAT-MIG-* com falha injetada | MVP |
| NFR-REL-003 | Confiabilidade | Execuções interrompidas detectadas e reportadas | T-DIR-002 | V1 |
| NFR-DET-001 | Determinismo | Mesma entrada → mesma saída byte a byte (exceto data no cabeçalho) independentemente de nº de núcleos | T-GEN-DET-002 | MVP |
| NFR-SEC-001 | Segurança | Nenhum segredo em projetos, logs, histórico, relatórios | T-SEC-002, T-SEC-004 | MVP |
| NFR-SEC-002 | Segurança | Escrita em banco externo só com autorização vinculada a fingerprint | T-SEC-001, T-SEC-003 | V1 |
| NFR-SEC-003 | Segurança | Nenhuma conexão de rede além dos bancos configurados | T-SEC-NET-001 | MVP |
| NFR-USA-001 | Usabilidade | Usuário experiente gera primeiro script para schema de 30 tabelas em ≤ 5 min | Teste roteirizado com 3 usuários (registro em `docs/usability/`) | V1 |
| NFR-USA-002 | Usabilidade | Toda operação > 2 s mostra progresso e é cancelável | Revisão de telas + T-UI-PERF-002 | MVP |
| NFR-USA-003 | Usabilidade | Mensagens de erro no padrão o-quê/por-quê/o-que-fazer | Revisão de `Strings.resx` (checklist) | V1 |
| NFR-A11Y-001 | Acessibilidade | Navegação completa por teclado; nomes UIA; contraste 4,5:1; DPI 100–200% | T-UI-A11Y-001..003, T-UI-DPI-001 | V1 |
| NFR-I18N-001 | Internacionalização | UI pt-BR; strings externalizadas; formatação de dados para SQL invariante | T-ERR-001 (recursos), revisão | MVP |
| NFR-MNT-001 | Manutenibilidade | Regras de dependência entre assemblies verificadas | T-ARC-001..007 | MVP |
| NFR-MNT-002 | Manutenibilidade | Cobertura de linhas ≥ 80% em Core/Generation/Mapping/SqlTooling/Output; ≥ 60% em Application | coverlet no CI | V1 (MVP: 70% Core/Generation) |
| NFR-MNT-003 | Manutenibilidade | Build sem warnings (`TreatWarningsAsErrors`) | CI | MVP |
| NFR-EXT-001 | Extensibilidade | Novo SGBD sem alterar núcleo | T-ARC-007 + suíte de contrato | MVP |
| NFR-COMP-001 | Compatibilidade | Matriz de versões de SGBD ([06 §7](06-technology-decisions.md)) | CI nightly/marcos | V1 |
| NFR-COMP-002 | Compatibilidade | Formatos de digest/projeto retrocompatíveis dentro do MAJOR | T-DIG-020, T-PRJ-004 | V1 |
| NFR-LIC-001 | Licenciamento | Todas as dependências com licença permissiva (MIT/Apache/BSD/PostgreSQL/IDPL/MS-PL) registrada | `docs/licenses.md` gerado na release + revisão | MVP |
| NFR-INST-001 | Instalação | Instalador assinado; atualização manual; desinstalação preserva `%LOCALAPPDATA%` (opção de remover) | Checklist de release | V1 |
