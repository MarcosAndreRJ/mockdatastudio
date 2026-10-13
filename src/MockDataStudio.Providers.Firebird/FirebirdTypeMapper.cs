using System.Globalization;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Firebird;

// Tipos do Firebird (docs/10 §6, docs/11 §5). O catálogo guarda o tipo como código em RDB$FIELDS; o extrator
// converte o código para o nome SQL (TypeName) e este mapper produz o tipo canônico. CHAR/VARCHAR com charset
// OCTETS são binários; inteiros com escala negativa são NUMERIC/DECIMAL.
public sealed class FirebirdTypeMapper : ITypeMapper
{
    public DataTypeInfo Map(NativeTypeDescriptor native)
    {
        var raw = string.IsNullOrWhiteSpace(native.RawName) ? "varchar(1)" : native.RawName.Trim().ToLowerInvariant();
        var domain = string.IsNullOrWhiteSpace(native.Domain) ? null : native.Domain;

        if (string.Equals(native.Charset, "OCTETS", StringComparison.OrdinalIgnoreCase)
            && (raw.StartsWith("char(", StringComparison.Ordinal) || raw.StartsWith("varchar(", StringComparison.Ordinal)))
        {
            var bin = NativeTypeParser.Parse(raw, native.Charset, domain: domain, bytes: true);
            return bin with { LogicalType = LogicalType.Binary };
        }

        if (raw.StartsWith("decfloat", StringComparison.Ordinal))
        {
            return NativeTypeParser.Parse("decimal", native.Charset, domain: domain) with { NativeName = raw };
        }

        if (raw == "int128")
        {
            return NativeTypeParser.Parse("numeric(38,0)", native.Charset, domain: domain) with { NativeName = "int128" };
        }

        if (raw == "time with time zone")
        {
            return NativeTypeParser.Parse("time", native.Charset, domain: domain) with { NativeName = raw };
        }

        return NativeTypeParser.Parse(raw, native.Charset, domain: domain);
    }

    // Nome SQL a partir de RDB$FIELDS (RDB$FIELD_TYPE, SUB_TYPE, LENGTH, CHARACTER_LENGTH, PRECISION, SCALE).
    public static string TypeName(int type, int subType, int length, int? charLength, int precision, int scale)
    {
        string Numeric(int defaultPrecision) =>
            (subType == 2 ? "decimal(" : "numeric(")
            + (precision > 0 ? precision : defaultPrecision).ToString(CultureInfo.InvariantCulture) + ","
            + (-scale).ToString(CultureInfo.InvariantCulture) + ")";

        var chars = (charLength ?? length).ToString(CultureInfo.InvariantCulture);
        return type switch
        {
            7 => scale < 0 || subType > 0 ? Numeric(4) : "smallint",
            8 => scale < 0 || subType > 0 ? Numeric(9) : "integer",
            16 => scale < 0 || subType > 0 ? Numeric(18) : "bigint",
            26 => scale < 0 || subType > 0 ? Numeric(38) : "int128",
            10 => "float",
            27 => scale < 0 ? Numeric(15) : "double precision",
            11 => "double precision",
            12 => "date",
            13 => "time",
            35 => "timestamp",
            28 => "time with time zone",
            29 => "timestamp with time zone",
            14 => "char(" + chars + ")",
            37 => "varchar(" + chars + ")",
            40 => "varchar(" + chars + ")",
            261 => subType == 1 ? "blob sub_type text" : "blob",
            23 => "boolean",
            24 => "decfloat(16)",
            25 => "decfloat(34)",
            9 => "bigint",
            _ => "varchar(" + chars + ")",
        };
    }
}
