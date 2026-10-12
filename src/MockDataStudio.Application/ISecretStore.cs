namespace MockDataStudio.Application;

/// <summary>
/// Contrato de proteção de credenciais conforme especificado em C09 e docs/23 §3.
/// Segredos nunca saem deste serviço exceto para montar a connection string em tempo de execução.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Protege o segredo utilizando DPAPI CurrentUser e entropia da instalação.
    /// </summary>
    byte[] Protect(string secret);

    /// <summary>
    /// Recupera o segredo protegido. Lança CryptographicException se a credencial foi gravada por outro usuário ou máquina.
    /// </summary>
    string Unprotect(byte[] protectedSecret);
}
