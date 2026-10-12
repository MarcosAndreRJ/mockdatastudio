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

    /// <summary>Caminhos dos projetos abertos recentemente, do mais novo para o mais antigo (máximo 10).</summary>
    IReadOnlyList<string> RecentProjects { get; }

    /// <summary>Move o caminho para o topo da lista de recentes e grava as preferências.</summary>
    void AddRecentProject(string path);

    /// <summary>Remove o caminho dos recentes (arquivo apagado ou movido) e grava as preferências.</summary>
    void RemoveRecentProject(string path);

    /// <summary>Grava as preferências. Falhas de E/S não derrubam o aplicativo: retornam false.</summary>
    bool Save();
}
