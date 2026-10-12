# 31 — Estratégia de Extensão

| Campo | Valor |
|---|---|
| Objetivo | Definir pontos de extensão, regras de compatibilidade e o modelo de plugins do V2. |
| Escopo | Providers, geradores, funções MDS-Expr, writers, packs, regras de validação. |
| Relacionados | ADR-009, [C02](contracts/C02-providers.md), [C05](contracts/C05-generation.md), [C08](contracts/C08-output.md) |

## 1. Pontos de extensão

| Ponto | Interface | V1 (interno) | V2 (plugin) | Requisitos de aceitação |
|---|---|---|---|---|
| SGBD | `IDatabaseProvider` (+ extractor, dialeto, mapper, bulk, inspector, translator) | Sim | Sim | Suíte de contrato 100%; valor novo em `DatabaseEngine` (exige versão MINOR do digest/projeto) |
| Gerador | `IGeneratorFactory`/`IValueGenerator`/`IIndexableGenerator` | Sim | Sim | Regras de 15 §11 |
| Função MDS-Expr | `IExprFunction` | Sim | Sim | Pura; determinística; `UsesRandom` correto |
| Saída | `IOutputWriterFactory` | Sim | Sim | Testes de round-trip do formato |
| Regra de validação | `IValidationRule` | Sim | Não (V2.1) | Código `VAL-*` registrado |
| Pack de dados | `ITemplatePackSource` / arquivos `.mdspack.json` | Sim | Sim (dados) | Passa no staging |

## 2. Registro (`IMdsModule`)

```csharp
public interface IMdsModule
{
    string Id { get; }                     // "mds.provider.firebird"
    int ContractVersion { get; }           // deve == MdsContracts.Version
    void Register(IServiceCollection services);
}
```
O composition root registra os módulos internos explicitamente. Ordem de registro determinística (ordenada por `Id`).

## 3. Compatibilidade

- `MdsContracts.Version` (int) incrementa quando qualquer interface de extensão muda de forma incompatível.
- Interfaces novas opcionais (ex.: `IIndexableGenerator`) não incrementam a versão.
- Identificadores de gerador são namespaced: `core.*`, `catalog.*`, `rel.*`, `br.*` reservados; terceiros usam `x.<vendor>.<nome>`.

## 4. Plugins (V2)

| Aspecto | Especificação |
|---|---|
| Local | `%LOCALAPPDATA%\MockDataStudio\plugins\<id>\` |
| Manifesto | `plugin.json`: `id`, `name`, `version`, `contractVersion`, `entryAssembly`, `author`, `license`, `sha256` |
| Carga | `AssemblyLoadContext` por plugin (isolável/descarregável), resolução de dependências privada |
| Confiança | Lista de permitidos em `app_setting`; primeira carga exige confirmação com aviso de execução de código; `--safe-mode` |
| Falha | Exceção em plugin → plugin desativado na sessão, erro registrado, app continua |
| Reprodutibilidade | Geradores de plugin marcados `VersionBound` por padrão |

## 5. CLI (V2) — contrato antecipado
`mds generate <projeto.mdsproj> --profile <nome> --output <dir> [--target <perfil>] [--seed <n>]`, `mds digest capture --profile <perfil> --out <arquivo>`, `mds validate <projeto>`. Implementada sobre `MockDataStudio.Application` sem WPF (por isso Application não referencia WPF desde o MVP). Inserção direta pela CLI exige `--confirm "<nome do banco>"` (equivalente ao TypedConfirm) e é bloqueada para perfis ReadOnly.
