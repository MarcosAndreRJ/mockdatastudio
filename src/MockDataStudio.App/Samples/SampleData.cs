using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MockDataStudio.App.Samples;

// Dados fixos extraídos dos mockups. Não são lidos de banco nem persistidos:
// servem apenas para preencher a interface nesta etapa.

public sealed record NavItem(string Key, string Title, string Glyph);

public sealed record TreeNode(string Text, string Glyph, IReadOnlyList<TreeNode> Children, bool Expanded = false);

public sealed record ProjectItem(string Name, string Description, string Engine, string Updated, string Glyph, Brush Tint, Brush TintBg);

public sealed record KeyValue(string Label, string Value);

public sealed record ConnectionItem(string Name, bool Online, string Glyph, Brush Tint, Brush TintBg, IReadOnlyList<KeyValue> Details);

public sealed record ActivityItem(string Text, string Time, Brush Dot);

public sealed record ColumnRow(string Name, string Type, string Key, string Nullable);

public sealed record CustomerRow(int ClienteId, string Nome, string Email, int TotalPedidos, string ValorTotal, string UltimoPedido);

public sealed record TemplateItem(string Name, string Description, string Count, string Glyph, Brush Tint, Brush TintBg, bool Selected);

public sealed record TableItem(string Name, string Status, string FieldInfo, Brush StatusFg, Brush StatusBg);

public sealed record FieldItem(string Name, string Glyph, string Type, string Constraints, string SuggestedTemplate, string Generator,
    string Confidence, Brush ConfidenceFg, Brush ConfidenceBg, string Status, Brush StatusFg, Brush StatusBg);

public sealed record ChangeItem(string Type, string ObjectName, string Change, Brush ChangeFg, string Details);

public sealed record StatTile(string Value, string Label, string Glyph, Brush Fg, Brush Bg);

public sealed record ScoreReason(string Text, string Points);

public sealed record SavedConnection(string Name, string Provider, string HostPath, string Database, string Environment,
    Brush EnvFg, Brush EnvBg, bool Online, string LastChecked, bool Starred, ImageSource? Logo);

public sealed record ConnectionTest(string When, string Status, Brush StatusFg, Brush StatusBg, string Latency, string Message);

public sealed record ConnectionActivity(string Title, string Detail, string Time, Brush Dot);

public static class SampleData
{
    // Ícones (Segoe MDL2 Assets). Códigos verificados visualmente contra a fonte instalada.
    // Observação: a fonte não possui glifo de "cilindro de banco"; usa-se o de biblioteca (E8F1) como aproximação.
    public const string IconDatabase = "\uE8F1";
    public const string IconFolder = "\uE8A1";
    public const string IconTable = "\uE80A";
    public const string IconColumn = "\uE9D5";
    public const string IconKey = "\uE71B";
    public const string IconIndex = "\uE9D9";
    public const string IconView = "\uE8A1";
    public const string IconFunction = "\uE943";
    public const string IconSequence = "\uE9E9";
    public const string IconType = "\uE8EC";
    public const string IconSchema = "\uE8A1";
    public const string IconCart = "\uE719";
    public const string IconPeople = "\uE716";
    public const string IconChart = "\uE9D2";
    public const string IconTag = "\uE8EC";
    public const string IconDocument = "\uE9F9";
    public const string IconMapPin = "\uE707";
    public const string IconPhone = "\uE717";
    public const string IconMail = "\uE715";
    public const string IconHome = "\uE80F";
    public const string IconPlay = "\uE768";
    public const string IconHistory = "\uE81C";
    public const string IconSettings = "\uE713";
    public const string IconCode = "\uE943";
    public const string IconTemplates = "\uE9D5";
    public const string IconMapping = "\uE8AB";
    public const string IconPlug = "\uE71B";
    public const string IconWarning = "\uE7BA";
    public const string IconCheck = "\uE73E";
    public const string IconBolt = "\uE945";
    public const string IconClock = "\uE121";
    public const string IconPlus = "\uE710";
    public const string IconMinus = "\uE921";
    public const string IconEdit = "\uE70F";

    public static IReadOnlyList<NavItem> Navigation { get; } =
    [
        new("home", "Home", IconHome),
        new("connections", "Connections", IconDatabase),
        new("explorer", "Explorer", IconFolder),
        new("sql", "SQL Editor", IconCode),
        new("digest", "Schema Digest", IconDocument),
        new("templates", "Templates", IconTemplates),
        new("mapping", "Mapping", IconMapping),
        new("generation", "Generation", IconPlay),
        new("history", "History", IconHistory),
        new("settings", "Settings", IconSettings),
    ];

    // Home

    public static IReadOnlyList<ProjectItem> RecentProjects { get; } =
    [
        new("E-Commerce Demo Data", "Mock data para plataforma de e-commerce (clientes, pedidos, produtos)", "PostgreSQL", "2 horas atrás", IconCart, Hex("#2563EB"), Hex("#DBEAFE")),
        new("HR Management System", "Dados de funcionários e organização", "MySQL", "1 dia atrás", IconPeople, Hex("#16A34A"), Hex("#DCFCE7")),
        new("Sales Analytics", "Vendas, regiões e métricas de desempenho", "SQL Server", "3 dias atrás", IconChart, Hex("#7C3AED"), Hex("#EDE9FE")),
        new("Product Catalog", "Produtos, categorias e estoque", "SQLite", "4 dias atrás", IconTag, Hex("#EA580C"), Hex("#FFEDD5")),
        new("Financial Records (Anonymized)", "Lançamentos e razões financeiras sintéticos", "PostgreSQL", "1 semana atrás", IconDocument, Hex("#0891B2"), Hex("#CFFAFE")),
    ];

    public static IReadOnlyList<ConnectionItem> Connections { get; } =
    [
        new("PostgreSQL", true, IconDatabase, Hex("#336791"), Hex("#E0ECF5"),
            [new("Host", "localhost"), new("Port", "5432"), new("Database", "shopdb")]),
        new("MySQL", true, IconDatabase, Hex("#00758F"), Hex("#DDF2F6"),
            [new("Host", "localhost"), new("Port", "3306"), new("Database", "hrdb")]),
        new("SQLite", true, IconDatabase, Hex("#0F766E"), Hex("#CCFBF1"),
            [new("File", "C:\\data\\app.db"), new("Version", "3.44.0"), new("Size", "42 MB")]),
        new("Firebird", false, IconDatabase, Hex("#DC2626"), Hex("#FEE2E2"),
            [new("Host", "192.168.1.50"), new("Port", "3050"), new("Database", "legacydb")]),
    ];

    public static IReadOnlyList<ActivityItem> RecentActivity { get; } =
    [
        new("Mock data gerado para \"E-Commerce Demo\"", "2 horas atrás", Hex("#16A34A")),
        new("Conectado ao PostgreSQL (shopdb)", "3 horas atrás", Hex("#2563EB")),
        new("Schema digest importado do MySQL", "1 dia atrás", Hex("#7C3AED")),
        new("Projeto \"HR Management System\" criado", "2 dias atrás", Hex("#2563EB")),
        new("Mock data gerado para \"Sales Analytics\"", "3 dias atrás", Hex("#16A34A")),
    ];

    // Explorer

    public static IReadOnlyList<TreeNode> ExplorerTree { get; } =
    [
        new("shopdb", IconDatabase, [
            new("public", IconSchema, [
                new("Tables (6)", IconFolder, [
                    new("cliente", IconTable, [
                        new("Columns (8)", IconColumn, []),
                        new("Indexes (3)", IconIndex, []),
                        new("Foreign Keys (1)", IconKey, []),
                    ], Expanded: true),
                    new("pedido", IconTable, [
                        new("Columns (6)", IconColumn, []),
                        new("Indexes (3)", IconIndex, []),
                        new("Foreign Keys (2)", IconKey, []),
                    ], Expanded: true),
                    new("item_pedido", IconTable, [
                        new("Columns (5)", IconColumn, []),
                        new("Indexes (2)", IconIndex, []),
                        new("Foreign Keys (2)", IconKey, []),
                    ], Expanded: true),
                    new("produto", IconTable, [
                        new("Columns (7)", IconColumn, []),
                        new("Indexes (3)", IconIndex, []),
                        new("Foreign Keys (0)", IconKey, []),
                    ], Expanded: true),
                    new("categoria", IconTable, []),
                    new("endereco", IconTable, []),
                ], Expanded: true),
                new("Views (2)", IconView, []),
                new("Functions (4)", IconFunction, []),
                new("Sequences (4)", IconSequence, []),
                new("Types (2)", IconType, []),
            ], Expanded: true),
            new("information_schema", IconSchema, []),
            new("pg_catalog", IconSchema, []),
        ], Expanded: true),
    ];

    public static IReadOnlyList<ColumnRow> ClienteColumns { get; } =
    [
        new("id", "SERIAL", "PK", "No"),
        new("nome", "VARCHAR(100)", "", "No"),
        new("email", "VARCHAR(150)", "", "No"),
        new("telefone", "VARCHAR(20)", "", "Yes"),
        new("data_cadastro", "TIMESTAMP", "", "No"),
        new("ativo", "BOOLEAN", "", "No"),
        new("endereco_id", "INTEGER", "FK", "Yes"),
        new("segmento", "VARCHAR(50)", "", "Yes"),
    ];

    public static string ExplorerQuery { get; } =
        """
        -- Clientes com número de pedidos e valor total
        SELECT
            c.id AS cliente_id,
            c.nome AS nome,
            c.email,
            COUNT(p.id) AS total_pedidos,
            COALESCE(SUM(p.total), 0) AS valor_total,
            MAX(p.data_pedido) AS ultimo_pedido
        FROM cliente c
        LEFT JOIN pedido p ON p.cliente_id = c.id
        WHERE c.ativo = true
        GROUP BY c.id, c.nome, c.email
        ORDER BY valor_total DESC
        LIMIT 100;
        """;

    public static IReadOnlyList<CustomerRow> QueryResults { get; } =
    [
        new(1, "Ana Silva", "ana.silva@email.com", 12, "4.230,50", "2024-02-15 10:23:11"),
        new(2, "Bruno Costa", "bruno.costa@email.com", 8, "2.850,00", "2024-02-10 14:05:32"),
        new(3, "Carla Mendes", "carla.mendes@email.com", 15, "2.410,30", "2024-02-14 09:12:03"),
        new(5, "Eduardo Lima", "eduardo.lima@email.com", 6, "1.980,75", "2024-02-08 16:45:20"),
        new(4, "Daniel Rocha", "daniel.rocha@email.com", 9, "1.240,00", "2024-02-12 11:33:45"),
        new(8, "Fernanda Alves", "fernanda.alves@email.com", 4, "980,20", "2024-02-09 13:21:17"),
        new(10, "Gustavo Pereira", "gustavo.pereira@email.com", 7, "870,60", "2024-02-11 08:56:09"),
        new(7, "Juliana Martins", "juliana.martins@email.com", 3, "650,00", "2024-02-07 18:02:33"),
    ];

    // Schema Digest

    public static IReadOnlyList<StatTile> DigestStats { get; } =
    [
        new("24", "Tables", IconTable, Hex("#2563EB"), Hex("#EFF6FF")),
        new("186", "Columns", IconColumn, Hex("#16A34A"), Hex("#F0FDF4")),
        new("32", "Foreign Keys", IconKey, Hex("#7C3AED"), Hex("#F5F3FF")),
        new("28", "Indexes", IconIndex, Hex("#EA580C"), Hex("#FFF7ED")),
        new("12", "Triggers", IconBolt, Hex("#DC2626"), Hex("#FEF2F2")),
        new("9", "Procedures", IconSettings, Hex("#7C3AED"), Hex("#F5F3FF")),
    ];

    public static IReadOnlyList<KeyValue> DigestInfo { get; } =
    [
        new("Connection", "PostgreSQL"),
        new("Database", "ecommerce_db"),
        new("Schema", "public"),
        new("Extracted at", "Apr 26, 2024 10:24 AM"),
        new("Digest version", "v1.3.0"),
        new("Size", "248 KB"),
    ];

    public static IReadOnlyList<TreeNode> DigestTree { get; } =
    [
        new("public (24 tables)", IconSchema, [
            new("cliente", IconTable, []),
            new("endereco", IconTable, []),
            new("pedido", IconTable, [
                new("Columns (14)", IconColumn, []),
                new("Indexes (3)", IconIndex, []),
                new("Foreign Keys (3)", IconKey, []),
                new("Unique Constraints (1)", IconTag, []),
                new("Check Constraints (2)", IconCheck, []),
            ], Expanded: true),
            new("item_pedido", IconTable, []),
            new("produto", IconTable, []),
            new("categoria", IconTable, []),
            new("estoque", IconTable, []),
            new("pagamento", IconTable, []),
            new("entrega", IconTable, []),
            new("usuario", IconTable, []),
            new("log_auditoria", IconTable, []),
        ], Expanded: true),
    ];

    public static string DigestJson { get; } =
        """
        {
          "type": "table",
          "schema": "public",
          "name": "pedido",
          "description": "Customer orders",
          "columns": [
            {
              "name": "id_pedido",
              "type": "BIGINT",
              "nullable": false,
              "default": "nextval('pedido_id_seq'::regclass)",
              "primary_key": true,
              "position": 1,
              "comment": "Order identifier"
            },
            {
              "name": "id_cliente",
              "type": "BIGINT",
              "nullable": false,
              "foreign_key": {
                "table": "cliente",
                "column": "id_cliente",
                "on_delete": "RESTRICT",
                "on_update": "CASCADE"
              }
            }
          ]
        }
        """;

    public static IReadOnlyList<ChangeItem> DigestChanges { get; } =
    [
        new("Table", "cupom_desconto", "Added", Hex("#16A34A"), "New table"),
        new("Column", "pedido.valor_desconto", "Added", Hex("#16A34A"), "New column (NUMERIC)"),
        new("Index", "idx_pedido_data", "Added", Hex("#16A34A"), "New index on (data_pedido)"),
        new("Table", "pedido_backup", "Removed", Hex("#DC2626"), "Table dropped"),
        new("Column", "produto.preco", "Modified", Hex("#7C3AED"), "Type changed: INTEGER → NUMERIC"),
        new("Foreign Key", "pedido.id_cliente", "Modified", Hex("#7C3AED"), "On delete: NO ACTION → RESTRICT"),
        new("Index", "idx_produto_nome", "Modified", Hex("#7C3AED"), "Columns changed"),
        new("Check", "pedido.valor_total_check", "Modified", Hex("#7C3AED"), "Expression updated"),
    ];

    // Templates

    public static IReadOnlyList<TemplateItem> TemplateList { get; } =
    [
        new("Nome", "First name (Brazilian)", "12.4K", IconPeople, Hex("#2563EB"), Hex("#DBEAFE"), false),
        new("Sobrenome", "Last name (Brazilian)", "11.8K", IconPeople, Hex("#2563EB"), Hex("#DBEAFE"), false),
        new("Nome Completo", "Full name (First + Last)", "9.2K", IconPeople, Hex("#7C3AED"), Hex("#EDE9FE"), false),
        new("CPF", "Brazilian individual taxpayer ID", "15.6K", IconDocument, Hex("#16A34A"), Hex("#DCFCE7"), false),
        new("CEP", "Brazilian postal code", "8.7K", IconMapPin, Hex("#DC2626"), Hex("#FEE2E2"), true),
        new("Endereco", "Street address", "6.1K", IconHome, Hex("#7C3AED"), Hex("#EDE9FE"), false),
        new("Cidade", "City name", "7.9K", "\uE8B6", Hex("#2563EB"), Hex("#DBEAFE"), false),
        new("Estado", "State (UF)", "6.5K", "\uE909", Hex("#16A34A"), Hex("#DCFCE7"), false),
        new("Telefone", "Phone number", "12.1K", IconPhone, Hex("#2563EB"), Hex("#DBEAFE"), false),
        new("Email", "Email address", "13.3K", IconMail, Hex("#7C3AED"), Hex("#EDE9FE"), false),
    ];

    public static IReadOnlyList<string> CepAliases { get; } = ["CEP", "NCEP", "NROCEP", "NRO_CEP", "COD_POSTAL"];

    public static IReadOnlyList<string> CepValuesLeft { get; } = ["01001-000", "01310-100", "04038-001", "04567-050", "07012-220"];

    public static IReadOnlyList<string> CepValuesRight { get; } = ["13084-971", "30140-071", "41820-021", "69005-030", "90670-340"];

    // Mapping

    public static IReadOnlyList<TableItem> MappingTables { get; } =
    [
        new("cliente", "Mapped", "24 fields", Hex("#15803D"), Hex("#DCFCE7")),
        new("produto", "Mapped", "18 fields", Hex("#15803D"), Hex("#DCFCE7")),
        new("categoria", "Mapped", "6 fields", Hex("#15803D"), Hex("#DCFCE7")),
        new("pedido", "Partial", "6 fields", Hex("#B45309"), Hex("#FEF3C7")),
        new("item_pedido", "Mapped", "8 fields", Hex("#15803D"), Hex("#DCFCE7")),
        new("pagamento", "Partial", "7 fields", Hex("#B45309"), Hex("#FEF3C7")),
        new("endereco", "Mapped", "11 fields", Hex("#15803D"), Hex("#DCFCE7")),
        new("transportadora", "Unmapped", "8 fields", Hex("#B91C1C"), Hex("#FEE2E2")),
        new("estoque", "Unmapped", "6 fields", Hex("#B91C1C"), Hex("#FEE2E2")),
        new("fornecedor", "Mapped", "9 fields", Hex("#15803D"), Hex("#DCFCE7")),
        new("cupom", "Partial", "5 fields", Hex("#B45309"), Hex("#FEF3C7")),
        new("avaliacao", "Unmapped", "7 fields", Hex("#B91C1C"), Hex("#FEE2E2")),
    ];

    public static IReadOnlyList<FieldItem> PedidoFields { get; } =
    [
        new("id", IconKey, "INT", "PK, NOT NULL", "Sequential ID", "Auto Increment", "High", Hex("#15803D"), Hex("#DCFCE7"), "Mapped", Hex("#15803D"), Hex("#DCFCE7")),
        new("cliente_id", IconPlug, "INT", "FK, NOT NULL", "Foreign Key", "Reference (cliente.id)", "High", Hex("#15803D"), Hex("#DCFCE7"), "Mapped", Hex("#15803D"), Hex("#DCFCE7")),
        new("status", IconSequence, "VARCHAR(20)", "NOT NULL", "Categorical", "List (Order Status)", "High", Hex("#15803D"), Hex("#DCFCE7"), "Mapped", Hex("#15803D"), Hex("#DCFCE7")),
        new("data_pedido", IconClock, "DATETIME", "NOT NULL", "Date & Time", "Recent Date", "Medium", Hex("#B45309"), Hex("#FEF3C7"), "Mapped", Hex("#15803D"), Hex("#DCFCE7")),
        new("valor_total", IconCalc, "DECIMAL(10,2)", "NOT NULL", "Numeric", "Currency", "Medium", Hex("#B45309"), Hex("#FEF3C7"), "Mapped", Hex("#15803D"), Hex("#DCFCE7")),
        new("observacao", IconDocument, "TEXT", "NULL", "Text", "—", "Low", Hex("#B91C1C"), Hex("#FEE2E2"), "Unmapped", Hex("#B91C1C"), Hex("#FEE2E2")),
    ];

    public const string IconCalc = "\uE8EF";

    public static IReadOnlyList<string> MatchedAliases { get; } = ["id", "pedido_id", "order_id", "pk", "codigo"];

    public static IReadOnlyList<ScoreReason> ScoringReasons { get; } =
    [
        new("Exact name match (id)", "+40"),
        new("Primary key detected", "+30"),
        new("Integer type match", "+20"),
        new("Common identifier pattern", "+10"),
    ];

    public static IReadOnlyList<string> PreviewValues { get; } = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"];

    // Connections

    public static IReadOnlyList<SavedConnection> SavedConnections { get; } =
    [
        new("PostgreSQL - ShopDB", "PostgreSQL", "localhost:5432", "shopdb", "Development", Hex("#1D4ED8"), Hex("#DBEAFE"), true, "2 hours ago", true, Logo("postgresql")),
        new("MySQL - HR System", "MySQL", "192.168.1.50:3306", "hrdb", "Production", Hex("#B91C1C"), Hex("#FEE2E2"), true, "15 minutes ago", false, null),
        new("SQLite - Local Catalog", "SQLite", "C:\\data\\app.db", "app.db", "Local", Hex("#475569"), Hex("#F1F5F9"), true, "1 hour ago", true, Logo("sqlite")),
        new("Firebird - Legacy", "Firebird", "192.168.1.75:3050", "legacydb", "QA", Hex("#6D28D9"), Hex("#EDE9FE"), false, "1 day ago", false, Logo("firebird")),
        new("PostgreSQL - Analytics", "PostgreSQL", "analytics-db:5432", "analytics", "Production", Hex("#B91C1C"), Hex("#FEE2E2"), true, "3 hours ago", false, Logo("postgresql")),
        new("MySQL - E-Commerce", "MySQL", "db.example.com:3306", "ecommerce", "QA", Hex("#6D28D9"), Hex("#EDE9FE"), false, "2 days ago", false, null),
    ];

    public static IReadOnlyList<ConnectionTest> RecentConnectionTests { get; } =
    [
        new("2024-10-16 10:23:11", "Success", Hex("#15803D"), Hex("#DCFCE7"), "12 ms", "Connection successful"),
        new("2024-10-15 14:08:32", "Success", Hex("#15803D"), Hex("#DCFCE7"), "18 ms", "Connection successful"),
        new("2024-10-14 09:12:45", "Success", Hex("#15803D"), Hex("#DCFCE7"), "22 ms", "Connection successful"),
        new("2024-10-13 16:45:10", "Failed", Hex("#B91C1C"), Hex("#FEE2E2"), "-", "Timeout after 30 seconds"),
        new("2024-10-12 11:20:03", "Success", Hex("#15803D"), Hex("#DCFCE7"), "28 ms", "Connection successful"),
    ];

    public static IReadOnlyList<ConnectionActivity> ConnectionActivities { get; } =
    [
        new("Connection tested successfully", "Connection is healthy. Latency: 12 ms", "2 hours ago", Hex("#16A34A")),
        new("Credentials updated", "Username and password were updated.", "1 day ago", Hex("#2563EB")),
        new("Connection failed", "Timeout after 30 seconds", "2 days ago", Hex("#DC2626")),
        new("Connection tested successfully", "Connection is healthy. Latency: 15 ms", "3 days ago", Hex("#16A34A")),
    ];

    private static BitmapImage Logo(string name)
    {
        var image = new BitmapImage(new Uri($"pack://application:,,,/Resources/Logos/{name}.png", UriKind.Absolute));
        image.Freeze();
        return image;
    }

    private static SolidColorBrush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
