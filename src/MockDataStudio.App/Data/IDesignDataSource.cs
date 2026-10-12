using MockDataStudio.App.Samples;

namespace MockDataStudio.App.Data;

// Fonte dos dados exibidos pelas telas. Nesta fase a implementação é SampleDesignDataSource (dados fixos
// dos mockups). Quando a fonte real existir (Sprints 2–17), uma nova implementação substitui esta sem alterar as views.
public interface IDesignDataSource
{
    IReadOnlyList<ProjectItem> RecentProjects { get; }
    IReadOnlyList<ConnectionItem> Connections { get; }
    IReadOnlyList<ActivityItem> RecentActivity { get; }

    IReadOnlyList<SavedConnection> SavedConnections { get; }
    IReadOnlyList<ConnectionTest> RecentConnectionTests { get; }
    IReadOnlyList<ConnectionActivity> ConnectionActivities { get; }

    IReadOnlyList<TreeNode> ExplorerTree { get; }
    IReadOnlyList<ColumnRow> ClienteColumns { get; }
    string ExplorerQuery { get; }
    IReadOnlyList<CustomerRow> QueryResults { get; }

    IReadOnlyList<StatTile> DigestStats { get; }
    IReadOnlyList<KeyValue> DigestInfo { get; }
    IReadOnlyList<TreeNode> DigestTree { get; }
    string DigestJson { get; }
    IReadOnlyList<ChangeItem> DigestChanges { get; }

    IReadOnlyList<TemplateItem> TemplateList { get; }
    IReadOnlyList<string> CepAliases { get; }
    IReadOnlyList<string> CepValuesLeft { get; }
    IReadOnlyList<string> CepValuesRight { get; }

    IReadOnlyList<TableItem> MappingTables { get; }
    IReadOnlyList<FieldItem> PedidoFields { get; }
    IReadOnlyList<string> MatchedAliases { get; }
    IReadOnlyList<ScoreReason> ScoringReasons { get; }
    IReadOnlyList<string> PreviewValues { get; }
}
