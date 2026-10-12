# Procedência dos dados do pacote `CORE_PTBR`

| Campo | Valor |
|---|---|
| Arquivo | `pack-core-ptbr.json` (formato `template-pack-1.0`) |
| Escopo | Subconjunto MVP (TASK-04.06): NOME 200, SOBRENOME 150, EMAIL_DOMINIO 20, UF 27, TELEFONE, ENDERECO 200 |
| Gerado por | Script determinístico (seed fixa `20261012`), mantido fora do repositório de produto |
| Verificado em | 2026-10-12 |
| Licença do pacote | **A definir** (DP-002). Até a decisão, o pacote não deve ser redistribuído fora do projeto |

## Origem por template

| Template | Origem | Observação |
|---|---|---|
| `NOME` (200) | Listas **autorais** do projeto (nomes próprios comuns no Brasil, sem fonte de terceiros) | Nenhuma lista copiada de outra base |
| `SOBRENOME` (150) | Listas **autorais** do projeto | Idem |
| `EMAIL_DOMINIO` (20) | Domínios **fictícios** no TLD reservado `.test` (RFC 2606) | Nunca domínios de provedores reais |
| `UF` (27) | Siglas oficiais das unidades federativas | Fato público, não protegido (PA-009) |
| `TELEFONE` (20 valores + aliases) | Números **fictícios** no formato `(DD) 9NNNN-NNNN` | Não correspondem a linhas reais verificadas |
| `ENDERECO` (200 registros) | Capitais das UFs (fato público) + logradouros, bairros e CEPs **sintéticos** | CEP gerado aleatoriamente de forma determinística; não é validado nos Correios |

## Garantias

- Nenhum dado pessoal real foi usado.
- Nenhuma lista foi copiada de terceiros; as listas de nomes e sobrenomes foram escritas para este projeto.
- Os valores são reprodutíveis: o mesmo gerador produz o mesmo arquivo byte a byte.

## Pendências

- Definir a licença do pacote (DP-002).
- Expandir para os volumes completos de 09 §6 (NOME 400, SOBRENOME 300, EMAIL_DOMINIO 20, CIDADE_UF, EMPRESA 300 etc.) nas versões seguintes do pacote.
- Presets `CPF` e `CNPJ` ficam para quando os geradores algorítmicos existirem (Sprint 6+).
