using System.Net.Sockets;
using System.Text.RegularExpressions;
using MySqlConnector;
using MockDataStudio.Core.Common;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.MySql;

public sealed partial class MySqlErrorTranslator : IErrorTranslator
{
    [GeneratedRegex(@"(?i)(password|pwd|senha)\s*=\s*[^;,\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordRedactionRegex();

    [GeneratedRegex(@"(?i)(password|pwd|senha)(\s+)'[^']*'", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedPasswordRegex();

    public DbErrorCategory Categorize(Exception ex)
    {
        if (ex is OperationCanceledException)
            return DbErrorCategory.Cancelled;

        if (ex is TimeoutException)
            return DbErrorCategory.Timeout;

        if (ex is SocketException)
            return DbErrorCategory.Connection;

        if (ex is MySqlException mySqlEx)
        {
            if (mySqlEx.InnerException is SocketException or TimeoutException or IOException)
                return DbErrorCategory.Connection;

            return mySqlEx.Number switch
            {
                1045 or 1698 or 1044 => DbErrorCategory.Permission, // Access denied / User check failed
                2002 or 2003 or 2005 or 2013 or 1042 or 1043 => DbErrorCategory.Connection, // Can't connect to server
                1205 => DbErrorCategory.Timeout, // Lock wait timeout
                1213 => DbErrorCategory.Deadlock, // Deadlock
                1062 => DbErrorCategory.UniqueViolation, // Duplicate entry
                1451 or 1452 => DbErrorCategory.ForeignKeyViolation, // Cannot add or update child row / delete parent
                1048 => DbErrorCategory.NotNullViolation, // Column cannot be null
                3819 => DbErrorCategory.CheckViolation, // Check constraint violated
                1406 => DbErrorCategory.DataTooLong, // Data too long for column
                1064 => DbErrorCategory.Syntax, // Syntax error
                _ => DbErrorCategory.Other,
            };
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

        if (ex is MySqlException mySqlEx)
        {
            details["mySqlCode"] = mySqlEx.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(mySqlEx.SqlState))
            {
                details["sqlState"] = mySqlEx.SqlState;
            }
        }

        var message = SanitizeMessage(ex.Message);
        return new MdsError(code, message, details);
    }

    public static string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        var redacted = PasswordRedactionRegex().Replace(message, "$1=***");
        return QuotedPasswordRegex().Replace(redacted, "$1$2'***'");
    }
}
