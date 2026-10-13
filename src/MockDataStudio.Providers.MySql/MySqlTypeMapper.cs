using System.Text.RegularExpressions;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.MySql;

// COLUMN_TYPE do MySQL (information_schema.columns) → tipo canônico (docs/10 §4, docs/11 §5).
// Trata: largura de exibição de inteiros ("int(11)", MySQL 5.7), "zerofill", tinyint(1) como Boolean (configurável),
// enum/set com valores, bit(1) como Boolean e year como inteiro de 16 bits.
public sealed partial class MySqlTypeMapper(bool tinyint1IsBool = true) : ITypeMapper
{
    public DataTypeInfo Map(NativeTypeDescriptor native)
    {
        var raw = string.IsNullOrWhiteSpace(native.RawName) ? "text" : native.RawName.Trim().ToLowerInvariant();
        raw = raw.Replace(" zerofill", string.Empty, StringComparison.Ordinal).Trim();

        var enumMatch = EnumRegex().Match(raw);
        if (enumMatch.Success)
        {
            var values = ValueRegex().Matches(enumMatch.Groups["values"].Value)
                .Select(m => m.Groups["v"].Value.Replace("''", "'", StringComparison.Ordinal))
                .ToList();
            // "set" aceita combinações: mantido como texto, com os valores permitidos para referência.
            return enumMatch.Groups["kind"].Value == "enum"
                ? NativeTypeParser.Parse("enum", native.Charset, values)
                : NativeTypeParser.Parse("varchar", native.Charset) with { NativeName = "set", EnumValues = values };
        }

        if (tinyint1IsBool && BoolTinyIntRegex().IsMatch(raw))
        {
            return NativeTypeParser.Parse("boolean") with { NativeName = "tinyint", Length = 1 };
        }

        if (raw == "bit(1)")
        {
            return NativeTypeParser.Parse("boolean") with { NativeName = "bit", Length = 1 };
        }

        if (raw.StartsWith("bit(", StringComparison.Ordinal))
        {
            return NativeTypeParser.Parse("varbinary") with { NativeName = "bit" };
        }

        if (raw == "year" || raw.StartsWith("year(", StringComparison.Ordinal))
        {
            return NativeTypeParser.Parse("smallint") with { NativeName = "year" };
        }

        // Largura de exibição de inteiros não é tamanho: "int(11) unsigned" → "int unsigned".
        var intMatch = IntDisplayWidthRegex().Match(raw);
        if (intMatch.Success)
        {
            raw = intMatch.Groups["name"].Value + intMatch.Groups["rest"].Value;
        }

        return NativeTypeParser.Parse(raw, native.Charset);
    }

    [GeneratedRegex(@"^(?<kind>enum|set)\((?<values>.*)\)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex EnumRegex();

    [GeneratedRegex(@"'(?<v>(?:[^']|'')*)'", RegexOptions.CultureInvariant)]
    private static partial Regex ValueRegex();

    [GeneratedRegex(@"^tinyint\(1\)( unsigned)?$", RegexOptions.CultureInvariant)]
    private static partial Regex BoolTinyIntRegex();

    [GeneratedRegex(@"^(?<name>tinyint|smallint|mediumint|int|integer|bigint)\(\d+\)(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex IntDisplayWidthRegex();
}
