using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Types;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers.Sqlite;

// Tipo declarado no SQLite → tipo canônico (docs/10 §3, docs/11 §5). Usa o NativeTypeParser do Core.
// Tipo vazio (colunas de view sem tipo declarado) vira "any", como no digest de referência.
public sealed class SqliteTypeMapper : ITypeMapper
{
    public DataTypeInfo Map(NativeTypeDescriptor native)
    {
        var raw = string.IsNullOrWhiteSpace(native.RawName) ? "any" : native.RawName;
        return NativeTypeParser.Parse(raw.ToLowerInvariant(), native.Charset, domain: native.Domain);
    }
}
