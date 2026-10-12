using System.Globalization;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Core.Types;

// Converte o texto do tipo no digest (ex.: "varchar(120)", "numeric(12,2)", "int unsigned")
// em DataTypeInfo. A inferência de LogicalType é genérica e determinística; os providers
// podem sobrescrever com o ITypeMapper do SGBD ao capturar um schema real.
public static partial class NativeTypeParser
{
    public static DataTypeInfo Parse(string typeText, string? charset = null, IReadOnlyList<string>? enumValues = null,
        string? domain = null, bool bytes = false)
    {
        var text = typeText.Trim();
        var isUnsigned = false;

        if (text.EndsWith(" unsigned", StringComparison.Ordinal))
        {
            isUnsigned = true;
            text = text[..^" unsigned".Length].TrimEnd();
        }

        int? length = null;
        int? precision = null;
        int? scale = null;
        var nativeName = text;

        var withLength = LengthRegex().Match(text);
        if (withLength.Success)
        {
            nativeName = withLength.Groups["name"].Value.Trim();
            length = int.Parse(withLength.Groups["n"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var withPrecision = PrecisionRegex().Match(text);
            if (withPrecision.Success)
            {
                nativeName = withPrecision.Groups["name"].Value.Trim();
                precision = int.Parse(withPrecision.Groups["p"].Value, CultureInfo.InvariantCulture);
                scale = int.Parse(withPrecision.Groups["s"].Value, CultureInfo.InvariantCulture);
            }
        }

        var logical = InferLogicalType(nativeName, out var bits);

        return new DataTypeInfo(
            NativeName: nativeName,
            LogicalType: enumValues is not null || logical == LogicalType.Enum ? LogicalType.Enum : logical,
            Length: length,
            Precision: precision,
            Scale: scale,
            IsUnsigned: isUnsigned,
            IntegerBits: bits,
            EnumValues: enumValues,
            Charset: charset,
            LengthUnit: bytes ? LengthUnit.Bytes : LengthUnit.Chars,
            Domain: domain);
    }

    // Texto canônico do digest para o tipo (inverso de Parse, para o caso sem extras).
    public static string Format(DataTypeInfo type)
    {
        var sb = new System.Text.StringBuilder(type.NativeName);

        if (type.Length is { } length)
        {
            sb.Append(CultureInfo.InvariantCulture, $"({length})");
        }
        else if (type.Precision is { } precision)
        {
            sb.Append(CultureInfo.InvariantCulture, $"({precision}");
            if (type.Scale is { } scale)
            {
                sb.Append(CultureInfo.InvariantCulture, $",{scale}");
            }

            sb.Append(')');
        }

        if (type.IsUnsigned)
        {
            sb.Append(" unsigned");
        }

        return sb.ToString();
    }

    private static LogicalType InferLogicalType(string nativeName, out int? integerBits)
    {
        integerBits = null;
        var name = nativeName.ToLowerInvariant();

        switch (name)
        {
            case "boolean":
            case "bool":
                return LogicalType.Boolean;
            case "tinyint":
            case "int1":
                integerBits = 8;
                return LogicalType.Integer;
            case "smallint":
            case "int2":
            case "smallserial":
            case "int16":
                integerBits = 16;
                return LogicalType.Integer;
            case "int":
            case "integer":
            case "int4":
            case "mediumint":
            case "serial":
            case "int32":
                integerBits = 32;
                return LogicalType.Integer;
            case "bigint":
            case "int8":
            case "bigserial":
            case "int64":
                integerBits = 64;
                return LogicalType.Integer;
            case "numeric":
            case "decimal":
            case "number":
                return LogicalType.Decimal;
            case "real":
            case "float":
            case "float4":
            case "float8":
            case "double":
            case "double precision":
                return LogicalType.Float;
            case "char":
            case "character":
            case "varchar":
            case "character varying":
            case "varchar2":
            case "nchar":
            case "nvarchar":
                return LogicalType.String;
            case "text":
            case "clob":
            case "tinytext":
            case "mediumtext":
            case "longtext":
                return LogicalType.Text;
            case "blob":
            case "bytea":
            case "binary":
            case "varbinary":
            case "tinyblob":
            case "mediumblob":
            case "longblob":
                return LogicalType.Binary;
            case "date":
                return LogicalType.Date;
            case "time":
            case "time without time zone":
                return LogicalType.Time;
            case "datetime":
            case "timestamp":
            case "timestamp without time zone":
                return LogicalType.DateTime;
            case "timestamptz":
            case "timestamp with time zone":
                return LogicalType.DateTimeTz;
            case "uuid":
                return LogicalType.Uuid;
            case "json":
            case "jsonb":
                return LogicalType.Json;
            case "enum":
                return LogicalType.Enum;
            case "interval":
                return LogicalType.Interval;
        }

        if (name.StartsWith("blob ", StringComparison.Ordinal))
        {
            return name.Contains("sub_type text", StringComparison.Ordinal) ? LogicalType.Text : LogicalType.Binary;
        }

        return LogicalType.Other;
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_ ]*?)\((?<n>\d+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex LengthRegex();

    [GeneratedRegex(@"^(?<name>[A-Za-z_][A-Za-z0-9_ ]*?)\((?<p>\d+),\s*(?<s>\d+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex PrecisionRegex();
}
