using MockDataStudio.App.Data;
using MockDataStudio.App.Samples;

namespace MockDataStudio.App.ViewModels;

// ViewModels finos: expõem os dados da fonte de design às views. Sem regra de negócio.

public sealed class HomeViewModel(IDesignDataSource data)
{
    public IReadOnlyList<ProjectItem> RecentProjects { get; } = data.RecentProjects;
    public IReadOnlyList<ConnectionItem> Connections { get; } = data.Connections;
    public IReadOnlyList<ActivityItem> RecentActivity { get; } = data.RecentActivity;
}

public sealed class ConnectionsViewModel(IDesignDataSource data)
{
    public IReadOnlyList<SavedConnection> SavedConnections { get; } = data.SavedConnections;
    public IReadOnlyList<ConnectionTest> RecentConnectionTests { get; } = data.RecentConnectionTests;
    public IReadOnlyList<ConnectionActivity> ConnectionActivities { get; } = data.ConnectionActivities;
}

public sealed class ExplorerViewModel(IDesignDataSource data, SqlEditorViewModel sqlEditor, SqlResultsViewModel sqlResults)
{
    public IReadOnlyList<TreeNode> ExplorerTree { get; } = data.ExplorerTree;
    public IReadOnlyList<ColumnRow> ClienteColumns { get; } = data.ClienteColumns;
    public SqlEditorViewModel SqlEditor { get; } = sqlEditor;
    public SqlResultsViewModel SqlResults { get; } = sqlResults;
}

public sealed class SqlEditorViewModel(IDesignDataSource data)
{
    public string Title { get; } = "Query - shopdb";
    public string DatabaseName { get; } = "shopdb (PostgreSQL)";
    public string QueryText { get; } = data.ExplorerQuery;
}

public sealed class SqlResultsViewModel(IDesignDataSource data)
{
    public IReadOnlyList<CustomerRow> Rows { get; } = data.QueryResults;
}

public sealed class SqlEditorPageViewModel(SqlEditorViewModel editor, SqlResultsViewModel results)
{
    public SqlEditorViewModel Editor { get; } = editor;
    public SqlResultsViewModel Results { get; } = results;
}

public sealed class SchemaDigestViewModel(IDesignDataSource data)
{
    public IReadOnlyList<StatTile> DigestStats { get; } = data.DigestStats;
    public IReadOnlyList<KeyValue> DigestInfo { get; } = data.DigestInfo;
    public IReadOnlyList<TreeNode> DigestTree { get; } = data.DigestTree;
    public string DigestJson { get; } = data.DigestJson;
    public IReadOnlyList<ChangeItem> DigestChanges { get; } = data.DigestChanges;
}

public sealed class TemplatesViewModel(IDesignDataSource data)
{
    public IReadOnlyList<TemplateItem> TemplateList { get; } = data.TemplateList;
    public IReadOnlyList<string> CepAliases { get; } = data.CepAliases;
    public IReadOnlyList<string> CepValuesLeft { get; } = data.CepValuesLeft;
    public IReadOnlyList<string> CepValuesRight { get; } = data.CepValuesRight;
}

public sealed class MappingViewModel(IDesignDataSource data)
{
    public IReadOnlyList<TableItem> MappingTables { get; } = data.MappingTables;
    public IReadOnlyList<FieldItem> PedidoFields { get; } = data.PedidoFields;
    public IReadOnlyList<string> MatchedAliases { get; } = data.MatchedAliases;
    public IReadOnlyList<ScoreReason> ScoringReasons { get; } = data.ScoringReasons;
    public IReadOnlyList<string> PreviewValues { get; } = data.PreviewValues;
}

// Telas sem mockup nesta etapa (Generation, History, Settings).
public sealed class PlaceholderViewModel(string title)
{
    public string Title { get; } = title;
}
