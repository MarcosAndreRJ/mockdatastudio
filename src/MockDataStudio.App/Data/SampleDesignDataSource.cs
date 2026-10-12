using MockDataStudio.App.Samples;

namespace MockDataStudio.App.Data;

// Única classe da UI autorizada a referenciar SampleData (ver Sprint 1, verificação do sprint).
public sealed class SampleDesignDataSource : IDesignDataSource
{
    public IReadOnlyList<ProjectItem> RecentProjects => SampleData.RecentProjects;
    public IReadOnlyList<ConnectionItem> Connections => SampleData.Connections;
    public IReadOnlyList<ActivityItem> RecentActivity => SampleData.RecentActivity;

    public IReadOnlyList<SavedConnection> SavedConnections => SampleData.SavedConnections;
    public IReadOnlyList<ConnectionTest> RecentConnectionTests => SampleData.RecentConnectionTests;
    public IReadOnlyList<ConnectionActivity> ConnectionActivities => SampleData.ConnectionActivities;

    public IReadOnlyList<TreeNode> ExplorerTree => SampleData.ExplorerTree;
    public IReadOnlyList<ColumnRow> ClienteColumns => SampleData.ClienteColumns;
    public string ExplorerQuery => SampleData.ExplorerQuery;
    public IReadOnlyList<CustomerRow> QueryResults => SampleData.QueryResults;

    public IReadOnlyList<StatTile> DigestStats => SampleData.DigestStats;
    public IReadOnlyList<KeyValue> DigestInfo => SampleData.DigestInfo;
    public IReadOnlyList<TreeNode> DigestTree => SampleData.DigestTree;
    public string DigestJson => SampleData.DigestJson;
    public IReadOnlyList<ChangeItem> DigestChanges => SampleData.DigestChanges;

    public IReadOnlyList<TemplateItem> TemplateList => SampleData.TemplateList;
    public IReadOnlyList<string> CepAliases => SampleData.CepAliases;
    public IReadOnlyList<string> CepValuesLeft => SampleData.CepValuesLeft;
    public IReadOnlyList<string> CepValuesRight => SampleData.CepValuesRight;

    public IReadOnlyList<TableItem> MappingTables => SampleData.MappingTables;
    public IReadOnlyList<FieldItem> PedidoFields => SampleData.PedidoFields;
    public IReadOnlyList<string> MatchedAliases => SampleData.MatchedAliases;
    public IReadOnlyList<ScoreReason> ScoringReasons => SampleData.ScoringReasons;
    public IReadOnlyList<string> PreviewValues => SampleData.PreviewValues;
}
