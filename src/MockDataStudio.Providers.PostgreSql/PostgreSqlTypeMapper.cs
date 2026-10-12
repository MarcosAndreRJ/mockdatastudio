using System.Text.RegularExpressions;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.PostgreSql;

// Tipo do catálogo PostgreSQL (format_type) → tipo canônico (docs/11 §5). Aceita tanto a forma longa
// ("character varying(60)") quanto os nomes internos de pg_type ("varchar", "int4", "bpchar").
public sealed partial class PostgreSqlTypeMapper : ITypeMapper
{
    public DataTypeInfo Map(NativeTypeDescriptor native)
    {
        var raw = string.IsNullOrWhiteSpace(native.RawName) ? "text" : native.RawName;
        return NativeTypeParser.Parse(Canonical(raw), native.Charset, domain: native.Domain);
    }

    // Exposto para teste: normaliza o nome antes do parser do Core.
    public static string Canonical(string rawName)
    {
        var text = rawName.Trim().ToLowerInvariant();
        var match = TypeShapeRegex().Match(text);
        if (!match.Success)
        {
            return text;
        }

        var name = match.Groups["name"].Value.Trim();
        var args = match.Groups["args"].Value;
        var mapped = name switch
        {
            "bpchar" or "character" => "char",
            "character varying" => "varchar",
            "bool" => "boolean",
            "int2" => "smallint",
            "int4" => "integer",
            "int8" => "bigint",
            "float4" => "real",
            "float8" => "double precision",
            "timestamp without time zone" => "timestamp",
            "timestamp with time zone" => "timestamptz",
            "time without time zone" => "time",
            _ => name,
        };

        return mapped + args;
    }

    [GeneratedRegex(@"^(?<name>[a-z][a-z0-9 ]*?)(?<args>\([^)]*\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeShapeRegex();
}
