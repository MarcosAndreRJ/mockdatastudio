namespace MockDataStudio.Application;

/// <summary>Preferências do usuário persistidas entre execuções (docs/05 §3, FR-120).</summary>
public interface IAppSettings
{
    /// <summary>"light" ou "dark".</summary>
    string Theme { get; set; }

    /// <summary>Cultura da interface (ex.: pt-BR).</summary>
    string Language { get; set; }

    /// <summary>Novos perfis de conexão nascem somente leitura.</summary>
    bool DefaultReadOnly { get; set; }

    /// <summary>Grava as preferências. Falhas de E/S não derrubam o aplicativo: retornam false.</summary>
    bool Save();
}
