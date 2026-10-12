using System.Globalization;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Providers.Sqlite;

// Conversão de valores .NET para o armazenamento do SQLite. Compartilhada por literais de script e bulk insert,
// para que ambos gravem datas do mesmo jeito (perfil Extra["dateStorage"], docs/10 §3).
public static class SqliteValues
{
    public const string DateStorageText = "text";
    public const string DateStorageUnix = "unix";
    public const string DateStorageJulian = "julian";

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static object? ToStorage(object? value, DataTypeInfo type, string dateStorage)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        switch (value)
        {
            case bool b:
                return b ? 1L : 0L;
            case byte[] bytes:
                return bytes;
            case Guid guid:
                return guid.ToString();
            case DateTimeOffset dto when IsTemporal(type.LogicalType):
                return ToTemporal(dto.UtcDateTime, dto.ToUnixTimeMilliseconds() / 1000.0, type.LogicalType, dateStorage, dto);
            case DateTime dt when IsTemporal(type.LogicalType):
                return ToTemporal(dt, (dt.ToUniversalTime() - UnixEpoch).TotalSeconds, type.LogicalType, dateStorage, null);
            case TimeSpan ts:
                return ts.ToString("c", CultureInfo.InvariantCulture);
            case IConvertible convertible when value is not string:
                return convertible.ToString(CultureInfo.InvariantCulture);
            default:
                return value.ToString();
        }
    }

    private static bool IsTemporal(LogicalType logical) =>
        logical is LogicalType.Date or LogicalType.Time or LogicalType.DateTime or LogicalType.DateTimeTz;

    private static object ToTemporal(DateTime utcOrLocal, double unixSeconds, LogicalType logical, string dateStorage, DateTimeOffset? offset)
    {
        var wall = offset?.DateTime ?? utcOrLocal;
        switch (dateStorage)
        {
            case DateStorageUnix:
                return (long)Math.Floor(unixSeconds);
            case DateStorageJulian:
                return unixSeconds / 86400.0 + 2440587.5;
            default:
                return logical switch
                {
                    LogicalType.Date => wall.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LogicalType.Time => wall.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    _ => wall.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                };
        }
    }
}
