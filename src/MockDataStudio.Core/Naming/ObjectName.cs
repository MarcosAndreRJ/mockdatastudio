using System.Globalization;

namespace MockDataStudio.Core.Naming;

// Identidade de um objeto de banco: namespace + nome, preservados como no catálogo do SGBD (08 §2.1).
public sealed record ObjectName(string Namespace, string Name) : IComparable<ObjectName>
{
    public static bool operator <(ObjectName left, ObjectName right) => left.CompareTo(right) < 0;

    public static bool operator >(ObjectName left, ObjectName right) => left.CompareTo(right) > 0;

    public static bool operator <=(ObjectName left, ObjectName right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ObjectName left, ObjectName right) => left.CompareTo(right) >= 0;

    public string CanonicalId => CanonicalFormat.Join(Namespace, Name);

    // Chave tolerante a caixa, usada apenas como evidência de correspondência (diff/renomeação).
    public string NormalizedKey => (Namespace + "." + Name).ToUpperInvariant();

    public static ObjectName ParseCanonical(string canonicalId)
    {
        var parts = CanonicalFormat.Split(canonicalId);
        if (parts.Count != 2)
        {
            throw new FormatException($"Identificador de objeto deve ter 2 partes: '{canonicalId}'.");
        }

        return new ObjectName(parts[0], parts[1]);
    }

    public int CompareTo(ObjectName? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byNamespace = string.CompareOrdinal(Namespace, other.Namespace);
        return byNamespace != 0 ? byNamespace : string.CompareOrdinal(Name, other.Name);
    }

    public override string ToString() => CanonicalId;
}

// Coluna identificada pela tabela que a contém e pelo nome.
public sealed record ColumnRef(ObjectName Table, string Column)
{
    public string CanonicalId => Table.CanonicalId + "." + CanonicalFormat.QuotePart(Column);

    public static ColumnRef ParseCanonical(string canonicalId)
    {
        var parts = CanonicalFormat.Split(canonicalId);
        if (parts.Count != 3)
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Referência de coluna deve ter 3 partes: '{canonicalId}'."));
        }

        return new ColumnRef(new ObjectName(parts[0], parts[1]), parts[2]);
    }

    public override string ToString() => CanonicalId;
}
