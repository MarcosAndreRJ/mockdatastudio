using System.Net.Sockets;
using System.Text.RegularExpressions;
using FirebirdSql.Data.FirebirdClient;
using MockDataStudio.Core.Common;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Firebird;

public sealed partial class FirebirdErrorTranslator : IErrorTranslator
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

        if (ex is FbException fbEx)
        {
            if (fbEx.InnerException is SocketException or TimeoutException or IOException)
                return DbErrorCategory.Connection;

            // Firebird SQLSTATE ou ErrorCode
            // 335544472: Your user name and password are not defined.
            // 335544721: Unable to complete network request to host.
            // 335544344: I/O error during "open" operation for file.
            return fbEx.ErrorCode switch
            {
                335544472 or 335544473 => DbErrorCategory.Permission,
                335544721 or 335544722 or 335544723 or 335544344 => DbErrorCategory.Connection,
                335544345 or 335544379 => DbErrorCategory.Timeout, // Lock conflict / wait
                335544336 => DbErrorCategory.Deadlock,
                335544349 or 335544665 => DbErrorCategory.UniqueViolation,
                335544466 => DbErrorCategory.ForeignKeyViolation,
                335544347 => DbErrorCategory.CheckViolation,
                335544458 => DbErrorCategory.DataTooLong,
                335544569 => DbErrorCategory.Syntax,
                _ => fbEx.SqlState switch
                {
                    var s when s != null && s.StartsWith("08", StringComparison.Ordinal) => DbErrorCategory.Connection,
                    var s when s != null && s.StartsWith("28", StringComparison.Ordinal) => DbErrorCategory.Permission,
                    "23000" => DbErrorCategory.UniqueViolation,
                    "22001" => DbErrorCategory.DataTooLong,
                    "42000" => DbErrorCategory.Syntax,
                    _ => DbErrorCategory.Other,
                },
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

        if (ex is FbException fbEx)
        {
            details["fbErrorCode"] = fbEx.ErrorCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(fbEx.SqlState))
            {
                details["sqlState"] = fbEx.SqlState;
            }
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
