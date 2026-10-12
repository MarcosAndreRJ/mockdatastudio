using System.Net.Sockets;
using System.Text.RegularExpressions;
using Npgsql;
using MockDataStudio.Core.Common;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.PostgreSql;

public sealed partial class PostgreSqlErrorTranslator : IErrorTranslator
{
    [GeneratedRegex(@"(?i)(password|pwd)\s*=\s*[^;,\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordRedactionRegex();

    public DbErrorCategory Categorize(Exception ex)
    {
        if (ex is OperationCanceledException)
            return DbErrorCategory.Cancelled;

        if (ex is TimeoutException)
            return DbErrorCategory.Timeout;

        if (ex is SocketException)
            return DbErrorCategory.Connection;

        if (ex is NpgsqlException npgsqlEx)
        {
            if (npgsqlEx.InnerException is SocketException or TimeoutException or IOException)
                return DbErrorCategory.Connection;

            if (npgsqlEx is PostgresException pg)
            {
                return pg.SqlState switch
                {
                    // Class 08 — Connection Exception (08000, 08003, 08006, 08001, 08004, 08007)
                    var s when s.StartsWith("08", StringComparison.Ordinal) => DbErrorCategory.Connection,
                    // Class 28 — Invalid Authorization Specification (28000, 28P01 invalid_password)
                    var s when s.StartsWith("28", StringComparison.Ordinal) => DbErrorCategory.Permission,
                    // Class 42 — Syntax Error or Access Rule Violation
                    "42501" => DbErrorCategory.Permission, // insufficient_privilege
                    "42601" => DbErrorCategory.Syntax,
                    // Class 23 — Integrity Constraint Violation
                    "23505" => DbErrorCategory.UniqueViolation,
                    "23503" => DbErrorCategory.ForeignKeyViolation,
                    "23502" => DbErrorCategory.NotNullViolation,
                    "23514" => DbErrorCategory.CheckViolation,
                    // Class 22 — Data Exception
                    "22001" => DbErrorCategory.DataTooLong,
                    // Class 40 — Transaction Rollback (40P01 deadlock)
                    "40P01" => DbErrorCategory.Deadlock,
                    // Class 57 — Operator Intervention (57014 query_canceled)
                    "57014" => DbErrorCategory.Cancelled,
                    _ => DbErrorCategory.Other,
                };
            }

            return DbErrorCategory.Connection;
        }

        return DbErrorCategory.Other;
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

        if (ex is PostgresException pg)
        {
            details["sqlState"] = pg.SqlState;
            if (!string.IsNullOrEmpty(pg.Detail))
                details["detail"] = pg.Detail;
        }

        var message = SanitizeMessage(ex.Message);
        return new MdsError(code, message, details);
    }

    public static string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return PasswordRedactionRegex().Replace(message, "$1=***");
    }
}
