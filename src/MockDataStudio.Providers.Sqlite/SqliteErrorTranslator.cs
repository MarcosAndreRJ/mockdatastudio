using Microsoft.Data.Sqlite;
using MockDataStudio.Core.Common;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Sqlite;

// Exceção do driver → categoria (docs/10 §7). Usa códigos estendidos do SQLite para distinguir constraints.
public sealed class SqliteErrorTranslator : IErrorTranslator
{
    // Códigos primários e estendidos (sqlite3.h).
    private const int SqliteError = 1;
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteCantOpen = 14;
    private const int SqliteAuthorization = 23;
    private const int SqliteTooBig = 18;
    private const int SqliteInterrupt = 9;
    private const int ConstraintUnique = 2067;
    private const int ConstraintPrimaryKey = 1555;
    private const int ConstraintForeignKey = 787;
    private const int ConstraintNotNull = 1299;
    private const int ConstraintCheck = 275;

    public DbErrorCategory Categorize(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return DbErrorCategory.Cancelled;
        }

        if (ex is TimeoutException)
        {
            return DbErrorCategory.Timeout;
        }

        if (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return DbErrorCategory.Connection;
        }

        if (ex is not SqliteException sqlite)
        {
            return DbErrorCategory.Other;
        }

        return sqlite.SqliteExtendedErrorCode switch
        {
            ConstraintUnique or ConstraintPrimaryKey => DbErrorCategory.UniqueViolation,
            ConstraintForeignKey => DbErrorCategory.ForeignKeyViolation,
            ConstraintNotNull => DbErrorCategory.NotNullViolation,
            ConstraintCheck => DbErrorCategory.CheckViolation,
            _ => sqlite.SqliteErrorCode switch
            {
                SqliteBusy or SqliteLocked => DbErrorCategory.Timeout,
                SqliteInterrupt => DbErrorCategory.Cancelled,
                SqliteCantOpen => DbErrorCategory.Connection,
                SqliteAuthorization => DbErrorCategory.Permission,
                SqliteTooBig => DbErrorCategory.DataTooLong,
                SqliteError => DbErrorCategory.Syntax,
                _ => DbErrorCategory.Other,
            },
        };
    }

    public MdsError Translate(Exception ex)
    {
        var category = Categorize(ex);
        var code = category switch
        {
            DbErrorCategory.Connection => ErrorCodes.PrvConnFailed,
            DbErrorCategory.Timeout => ErrorCodes.PrvTimeout,
            DbErrorCategory.Cancelled => ErrorCodes.PrvCancelled,
            DbErrorCategory.Syntax => ErrorCodes.PrvSqlSyntax,
            _ => ErrorCodes.PrvSqlError,
        };

        var details = new Dictionary<string, string>
        {
            ["category"] = category.ToString(),
        };
        if (ex is SqliteException sqlite)
        {
            details["sqliteCode"] = sqlite.SqliteExtendedErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new MdsError(code, ex.Message, details);
    }
}
