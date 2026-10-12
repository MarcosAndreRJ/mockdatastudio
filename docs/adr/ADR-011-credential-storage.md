# ADR-011 — Credenciais via DPAPI

**Status:** Aceito · **Data:** 2026-10-11

## Contexto
Senhas de bancos precisam persistir localmente, nunca em projetos nem logs.

## Alternativas
1. **DPAPI (`ProtectedData`, `DataProtectionScope.CurrentUser`)** com entropia adicional por instalação, armazenado no catálogo.
2. Windows Credential Manager (`CredWrite`/`CredRead` via P/Invoke).
3. Senha mestra do app + AES-GCM.
4. Não persistir (pedir sempre).

## Decisão
Opção 1 como padrão; opção 4 disponível por perfil ("Sempre perguntar a senha").

## Justificativa
Sem senha mestra, vinculado ao usuário Windows, API simples e suportada.

## Consequências positivas
Segredos ilegíveis por outros usuários/máquinas.

## Consequências negativas
Backup do catálogo restaurado em outra máquina/usuário não recupera senhas (comportamento desejado e documentado).

## Riscos
Malware executando como o mesmo usuário pode decifrar (limitação inerente; documentada em [23](../23-security.md)).

## Condições para revisão
Se exigido por política corporativa → opção 3 ou integração com cofre externo (pós-V2).
