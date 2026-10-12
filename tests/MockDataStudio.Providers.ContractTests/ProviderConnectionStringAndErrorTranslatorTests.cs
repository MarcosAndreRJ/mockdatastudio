using System.Data.Common;
using MockDataStudio.Core.Connections;
using MockDataStudio.Providers.Firebird;
using MockDataStudio.Providers.Models;
using MockDataStudio.Providers.MySql;
using MockDataStudio.Providers.PostgreSql;
using Shouldly;
using Xunit;

namespace MockDataStudio.Providers.ContractTests;

public sealed class ProviderConnectionStringAndErrorTranslatorTests
{
    // Lê um parâmetro da string por qualquer nome equivalente (sinônimos do driver). Testa o VALOR, não a grafia (docs/10 §4, §6).
    private static string? Value(string connectionString, params string[] keys)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value))
            {
                return value?.ToString();
            }
        }

        return null;
    }

    // --- PostgreSQL ---

    [Fact]
    public void PostgreSqlProvider_BuildConnectionString_ShouldIncludeAllConfiguredParameters()
    {
        var provider = new PostgreSqlProvider();
        var settings = new ConnectionSettings(
            Host: "db.server.internal",
            Port: 5433,
            Database: "shop_production",
            User: "app_user",
            UseSsl: false,
            Charset: null,
            ConnectTimeoutSec: 25,
            CommandTimeoutSec: 0,
            Extra: new Dictionary<string, string> { ["sslMode"] = "Require" }
        );

        var cs = provider.BuildConnectionString(settings, "P@ssw0rd123");

        cs.ShouldContain("Host=db.server.internal");
        cs.ShouldContain("Port=5433");
        cs.ShouldContain("Database=shop_production");
        cs.ShouldContain("Username=app_user");
        cs.ShouldContain("Password=P@ssw0rd123");
        cs.ShouldContain("Ssl Mode=Require");
        cs.ShouldContain("Timeout=25");
    }

    [Fact]
    public void PostgreSqlErrorTranslator_ShouldSanitizePassword_AndTranslateStandardExceptions()
    {
        var translator = new PostgreSqlErrorTranslator();

        // Sanitização de mensagem que contém senha acidental
        var rawMessage = "Connection failed to server db.internal with Password=SuperSecretPwd! after timeout";
        var sanitized = PostgreSqlErrorTranslator.SanitizeMessage(rawMessage);
        sanitized.ShouldNotContain("SuperSecretPwd!");
        sanitized.ShouldContain("Password=***");

        // Tradução de exceção de timeout
        var ex = new TimeoutException("Connection attempt timed out");
        var category = translator.Categorize(ex);
        category.ShouldBe(DbErrorCategory.Timeout);
    }

    // --- MySQL ---

    [Fact]
    public void MySqlProvider_BuildConnectionString_MustEnforceSecurityInvariants()
    {
        var provider = new MySqlProvider();
        var settings = new ConnectionSettings(
            Host: "mysql.local",
            Port: 3307,
            Database: "customers",
            User: "root",
            UseSsl: false,
            Charset: null,
            ConnectTimeoutSec: 10,
            CommandTimeoutSec: 0,
            Extra: new Dictionary<string, string> { ["sslMode"] = "Preferred" }
        );

        var cs = provider.BuildConnectionString(settings, "root_pass");

        cs.ShouldContain("Server=mysql.local");
        cs.ShouldContain("Port=3307");
        cs.ShouldContain("Database=customers");
        cs.ShouldContain("User ID=root");
        cs.ShouldContain("Password=root_pass");
        cs.ShouldContain("Connection Timeout=10");

        // Invariantes de segurança (docs/10 §4, Prompt §5.2). Devem estar EXPLÍCITOS na string, não apenas como padrão do driver.
        Value(cs, "Allow Load Local Infile", "AllowLoadLocalInfile").ShouldBe("False");
        Value(cs, "Allow User Variables", "AllowUserVariables").ShouldBe("False");
        Value(cs, "Character Set", "CharacterSet", "CharSet").ShouldBe("utf8mb4");
    }

    [Fact]
    public void MySqlErrorTranslator_ShouldSanitizePassword_AndTranslateGenericErrors()
    {
        var translator = new MySqlErrorTranslator();

        var rawMessage = "User 'admin' with pwd 'TopSecret!' failed to connect";
        var sanitized = MySqlErrorTranslator.SanitizeMessage(rawMessage);
        sanitized.ShouldNotContain("TopSecret!");

        var timeoutEx = new TimeoutException("The socket was closed");
        var category = translator.Categorize(timeoutEx);
        category.ShouldBe(DbErrorCategory.Timeout);
    }

    // --- Firebird ---

    [Fact]
    public void FirebirdProvider_BuildConnectionString_ShouldIncludeDialectAndCharset()
    {
        var provider = new FirebirdProvider();
        var settings = new ConnectionSettings(
            Host: "firebird.host",
            Port: 3051,
            Database: "/var/lib/firebird/data/test.fdb",
            User: "sysdba",
            UseSsl: false,
            Charset: "UTF8",
            ConnectTimeoutSec: 20,
            CommandTimeoutSec: 0,
            Extra: null
        );

        var cs = provider.BuildConnectionString(settings, "masterkey");

        Value(cs, "Data Source", "DataSource").ShouldBe("firebird.host");
        Value(cs, "Port Number", "Port").ShouldBe("3051");
        Value(cs, "Initial Catalog", "Database").ShouldBe("/var/lib/firebird/data/test.fdb");
        Value(cs, "User ID", "User").ShouldBe("sysdba");
        Value(cs, "Password").ShouldBe("masterkey");
        Value(cs, "Character Set", "Charset", "CharSet").ShouldBe("UTF8");
        Value(cs, "Dialect").ShouldBe("3");
        Value(cs, "Connection Timeout", "ConnectionTimeout").ShouldBe("20");
    }

    [Fact]
    public void FirebirdErrorTranslator_ShouldSanitizePassword()
    {
        var translator = new FirebirdErrorTranslator();

        var rawMessage = "Error connecting with Password=masterkey to database";
        var sanitized = FirebirdErrorTranslator.SanitizeMessage(rawMessage);
        sanitized.ShouldNotContain("masterkey");
        sanitized.ShouldContain("Password=***");

        var opCancelledEx = new OperationCanceledException();
        var category = translator.Categorize(opCancelledEx);
        category.ShouldBe(DbErrorCategory.Cancelled);
    }
}
