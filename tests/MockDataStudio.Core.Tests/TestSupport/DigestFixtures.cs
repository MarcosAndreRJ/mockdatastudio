using MockDataStudio.Core.Common;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Core.Tests.TestSupport;

// Construtores de modelos de teste a partir dos exemplos oficiais (docs/examples).
public static class DigestFixtures
{
    public static readonly string[] Engines = ["sqlite", "mysql", "postgresql", "firebird"];

    public static IDigestSerializer NewSerializer() => new DigestSerializer(new StructureHasher());

    public static SchemaModel LoadExample(string engine)
    {
        var result = NewSerializer().Deserialize(RepoPaths.ReadExample(engine));
        return result.IsSuccess ? result.Value : throw new InvalidOperationException($"Exemplo {engine} inválido: {result.Error.Code} {result.Error.Message}");
    }

    public static SchemaModel MapTables(SchemaModel model, Func<TableModel, TableModel> map) =>
        model with
        {
            Namespaces = model.Namespaces
                .Select(ns => ns with { Tables = ns.Tables.Select(map).ToList() })
                .ToList(),
        };

    public static TableModel FindTable(SchemaModel model, string table) =>
        model.Namespaces.SelectMany(ns => ns.Tables).First(t => t.Name == table);

    public static SchemaModel ReplaceTable(SchemaModel model, string table, Func<TableModel, TableModel> map) =>
        MapTables(model, t => t.Name == table ? map(t) : t);
}
