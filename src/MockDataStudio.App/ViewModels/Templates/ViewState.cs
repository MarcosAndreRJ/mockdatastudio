namespace MockDataStudio.App.ViewModels.Templates;

/// <summary>Estado visual de uma lista ou área da tela Templates (TC-103).</summary>
public enum ViewState
{
    /// <summary>Carregando dados.</summary>
    Loading,

    /// <summary>Carregado, mas não há nenhum item (ex.: catálogo sem templates).</summary>
    Empty,

    /// <summary>Há itens, mas o filtro atual não encontrou nenhum.</summary>
    NoResults,

    /// <summary>Falha ao carregar.</summary>
    Error,

    /// <summary>Dados prontos para uso.</summary>
    Ready,
}

/// <summary>Abas da tela Templates, na ordem de exibição (TC-102).</summary>
public enum TemplatesTab
{
    Templates = 0,
    ImportExport = 1,
    Presets = 2,
    LearnedRules = 3,
}
