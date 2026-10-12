using MockDataStudio.Core.Common;

using MockDataStudio.Core.Schema;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Schema;

// TASK-01.02: invariantes dos construtores Create(...) retornam Result com o código DIG-* adequado.
public sealed class SchemaInvariantTests
{
    private static ColumnModel Col(string name, int ordinal = 1, IdentityInfo? identity = null, GeneratedInfo? generated = null) =>
        new(name, ordinal, new DataTypeInfo("integer", LogicalType.Integer), IsNullable: false, Identity: identity, Generated: generated);

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-007")]
    public void TableModel_PkComColunaInexistente_FalhaComDigPkCol()
    {
        var result = TableModel.Create("cliente", [Col("id")], new PrimaryKeyModel(null, ["inexistente"]));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.DigPkCol);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TableModel_ColunasDuplicadas_FalhaComDigDup()
    {
        var result = TableModel.Create("cliente", [Col("id", 1), Col("id", 2)]);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.DigDup);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void TableModel_FkComAridadeDiferente_FalhaComDigFkArity()
    {
        var fk = new ForeignKeyModel("fk", ["id", "nome"], new MockDataStudio.Core.Naming.ObjectName("main", "pai"), ["id"]);

        var result = TableModel.Create("filho", [Col("id"), Col("nome", 2)], foreignKeys: [fk]);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.DigFkArity);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ColumnModel_IdentidadeEGerada_FalhaComDigFlags()
    {
        var result = ColumnModel.Create("id", 1, new DataTypeInfo("integer", LogicalType.Integer), false,
            identity: new IdentityInfo(IdentityKind.AutoIncrement), generated: new GeneratedInfo("1", Stored: true));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.DigFlags);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ColumnModel_OrdinalInvalido_Falha()
    {
        ColumnModel.Create("id", 0, new DataTypeInfo("integer", LogicalType.Integer), false).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NamespaceModel_TabelasDuplicadas_FalhaComDigDup()
    {
        var table = TableModel.Create("cliente", [Col("id")]).Value;

        var result = NamespaceModel.Create("main", false, [table, table]);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.DigDup);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SchemaModel_NamespacesDuplicados_FalhaComDigDup()
    {
        var ns = NamespaceModel.Create("main", false, []).Value;

        var result = SchemaModel.Create(MockDataStudio.Core.Common.DatabaseEngine.Sqlite, "3.46", "loja", [ns, ns], DateTimeOffset.UnixEpoch);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(ErrorCodes.DigDup);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SchemaModel_FindTable_EncontraTabelaPorObjectName()
    {
        var table = TableModel.Create("cliente", [Col("id")]).Value;
        var ns = NamespaceModel.Create("main", false, [table]).Value;
        var schema = SchemaModel.Create(MockDataStudio.Core.Common.DatabaseEngine.Sqlite, "3.46", "loja", [ns], DateTimeOffset.UnixEpoch).Value;

        schema.FindTable(new MockDataStudio.Core.Naming.ObjectName("main", "cliente")).ShouldBe(table);
        schema.FindTable(new MockDataStudio.Core.Naming.ObjectName("main", "outra")).ShouldBeNull();
        schema.AllTables().Count().ShouldBe(1);
    }
}
