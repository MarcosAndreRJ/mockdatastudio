using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Classification;
using MockDataStudio.SqlTooling.Splitter;
using Shouldly;
using Xunit;

namespace MockDataStudio.SqlTooling.Tests;

public sealed class SqlSplitterTests
{
    private readonly SqlScriptSplitter _splitter = new();

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-SPLIT-SQLITE")]
    public void T_SQL_SPLIT_SQLITE_SplitsStandardStatementsAndPreservesSemicolonsInStringsAndComments()
    {
        var script = @"
            -- Criação
            CREATE TABLE t (id INT, nome TEXT DEFAULT 'exemplo; com ponto-e-virgula');
            /* Bloco de comentário ; dentro */
            INSERT INTO t VALUES (1, 'valor');
            SELECT * FROM t;
        ";

        var statements = _splitter.Split(script, SqlDialectFeatures.Sqlite);

        statements.Count.ShouldBe(3);
        statements[0].Kind.ShouldBe(StatementKind.Ddl);
        statements[0].Text.ShouldContain("CREATE TABLE t");
        statements[1].Kind.ShouldBe(StatementKind.Insert);
        statements[2].Kind.ShouldBe(StatementKind.Select);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-SPLIT-MYSQL")]
    public void T_SQL_SPLIT_MYSQL_HandlesDelimiterDirectivesAndStoredProcedureBlocks()
    {
        var script = @"
            DELIMITER //
            CREATE PROCEDURE sp_teste()
            BEGIN
                SELECT 1;
                SELECT 2;
            END//
            DELIMITER ;
            SELECT * FROM tabela;
        ";

        var statements = _splitter.Split(script, SqlDialectFeatures.MySql);

        statements.Count.ShouldBe(2);
        statements[0].Text.ShouldContain("CREATE PROCEDURE sp_teste()");
        statements[0].Text.ShouldContain("SELECT 1;");
        statements[0].Terminator.ShouldBe("//");
        statements[1].Text.ShouldContain("SELECT * FROM tabela");
        statements[1].Terminator.ShouldBe(";");
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-SPLIT-PG")]
    public void T_SQL_SPLIT_PG_HandlesDollarQuotedStringsWithInternalSemicolons()
    {
        var script = @"
            CREATE OR REPLACE FUNCTION fn_teste() RETURNS void AS $$
            BEGIN
                UPDATE t SET x = 1;
                INSERT INTO log (msg) VALUES ('ok; fim');
            END;
            $$ LANGUAGE plpgsql;

            SELECT * FROM t;
        ";

        var statements = _splitter.Split(script, SqlDialectFeatures.PostgreSql);

        statements.Count.ShouldBe(2);
        statements[0].Kind.ShouldBe(StatementKind.Ddl);
        statements[0].Text.ShouldContain("UPDATE t SET x = 1;");
        statements[1].Kind.ShouldBe(StatementKind.Select);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-SPLIT-FB")]
    public void T_SQL_SPLIT_FB_HandlesSetTermDirectivesAndTriggers()
    {
        var script = @"
            SET TERM ^ ;
            CREATE OR ALTER PROCEDURE sp_fb
            AS
            BEGIN
                INSERT INTO audit VALUES ('evento; registrado');
            END^
            SET TERM ; ^
            SELECT * FROM audit;
        ";

        var statements = _splitter.Split(script, SqlDialectFeatures.Firebird);

        statements.Count.ShouldBe(2);
        statements[0].Terminator.ShouldBe("^");
        statements[0].Text.ShouldContain("INSERT INTO audit VALUES ('evento; registrado');");
        statements[1].Terminator.ShouldBe(";");
        statements[1].Kind.ShouldBe(StatementKind.Select);
    }

    [Theory]
    [InlineData("sqlite", 5)]
    [InlineData("mysql", 4)]
    [InlineData("postgresql", 3)]
    [InlineData("firebird", 3)]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-SQL-SPLIT-CORPUS")]
    public void T_SQL_SPLIT_CorpusFixtures_MustSplitAccurately(string dialectFolder, int expectedStatementsCount)
    {
        var repoRoot = FindSolutionRoot();
        var fixturePath = Path.Combine(repoRoot, "tests", "fixtures", "sql-split", dialectFolder, "sample.sql");

        File.Exists(fixturePath).ShouldBeTrue($"Fixture {fixturePath} deve existir.");
        var content = File.ReadAllText(fixturePath);

        var statements = _splitter.Split(content, SqlDialectFeatures.ByName(dialectFolder));
        statements.Count.ShouldBe(expectedStatementsCount);
    }

    private static string FindSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "MockDataStudio.slnx")) ||
                File.Exists(Path.Combine(dir, "MockDataStudio.sln")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        throw new InvalidOperationException("Não foi possível localizar a raiz da solution MockDataStudio.");
    }
}
