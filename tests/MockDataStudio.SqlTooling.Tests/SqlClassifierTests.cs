using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Classification;
using MockDataStudio.SqlTooling.Lexer;
using Shouldly;
using Xunit;

namespace MockDataStudio.SqlTooling.Tests;

public sealed class SqlClassifierTests
{
    [Theory]
    [InlineData("SELECT id, nome FROM usuarios;", StatementKind.Select, false, false)]
    [InlineData("SELECT * FROM usuarios WHERE id = 1;", StatementKind.Select, false, true)]
    [InlineData("SELECT * FROM usuarios WHERE id = 1 FOR UPDATE;", StatementKind.Select, false, true)]
    [InlineData("SELECT * INTO novos_usuarios FROM usuarios;", StatementKind.Ddl, true, false)]
    [InlineData("INSERT INTO usuarios (nome) VALUES ('Alice');", StatementKind.Insert, false, false)]
    [InlineData("UPDATE usuarios SET nome = 'Bob' WHERE id = 1;", StatementKind.Update, false, true)]
    [InlineData("UPDATE usuarios SET nome = 'Bob';", StatementKind.Update, true, false)]
    [InlineData("UPDATE usuarios SET nome = 'Bob' WHERE 1 = 1;", StatementKind.Update, true, true)]
    [InlineData("UPDATE usuarios SET nome = 'Bob' WHERE true;", StatementKind.Update, true, true)]
    [InlineData("UPDATE usuarios SET nome = 'Bob' WHERE 0 = 0;", StatementKind.Update, true, true)]
    [InlineData("DELETE FROM usuarios WHERE id = 2;", StatementKind.Delete, false, true)]
    [InlineData("DELETE FROM usuarios;", StatementKind.Delete, true, false)]
    [InlineData("DELETE FROM usuarios WHERE 1=1;", StatementKind.Delete, true, true)]
    [InlineData("DELETE FROM usuarios WHERE 'a' = 'a';", StatementKind.Delete, true, true)]
    [InlineData("TRUNCATE TABLE usuarios;", StatementKind.Ddl, true, false)]
    [InlineData("DROP TABLE usuarios;", StatementKind.Ddl, true, false)]
    [InlineData("ALTER TABLE usuarios ADD COLUMN idade INT;", StatementKind.Ddl, true, false)]
    [InlineData("CREATE TABLE teste (id INT);", StatementKind.Ddl, false, false)]
    [InlineData("MERGE INTO target USING source ON target.id = source.id WHEN MATCHED THEN UPDATE SET val = 1;", StatementKind.Merge, true, false)]
    [InlineData("PRAGMA foreign_keys = OFF;", StatementKind.Session, true, false)]
    [InlineData("SET FOREIGN_KEY_CHECKS = 0;", StatementKind.Session, true, false)]
    [InlineData("CALL sp_recalcular_totais();", StatementKind.Procedure, true, false)]
    [InlineData("EXECUTE BLOCK AS BEGIN INSERT INTO t VALUES (1); END;", StatementKind.Procedure, true, false)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-CLASS-001")]
    public void T_SQL_CLASS_001_Statements_MustBeCorrectlyClassified(
        string sql,
        StatementKind expectedKind,
        bool expectedDestructive,
        bool expectedHasWhere)
    {
        var tokens = SqlLexer.Tokenize(sql);
        var (kind, isDestructive, hasWhere) = SqlClassifier.Classify(tokens);

        kind.ShouldBe(expectedKind, $"Kind divergente para: {sql}");
        isDestructive.ShouldBe(expectedDestructive, $"IsDestructive divergente para: {sql}");
        hasWhere.ShouldBe(expectedHasWhere, $"HasWhereClause divergente para: {sql}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-CLASS-001")]
    public void T_SQL_CLASS_001_DataModifyingCte_MustBeClassifiedByMostDestructiveStatement()
    {
        var sql = @"
            WITH deleted_rows AS (
                DELETE FROM clientes WHERE ativo = false RETURNING id
            )
            SELECT count(*) FROM deleted_rows;
        ";

        var tokens = SqlLexer.Tokenize(sql, SqlDialectFeatures.PostgreSql);
        var (kind, isDestructive, hasWhere) = SqlClassifier.Classify(tokens);

        kind.ShouldBe(StatementKind.Delete);
        hasWhere.ShouldBeTrue();
    }
}
