using MockDataStudio.Core.Schema;

namespace MockDataStudio.Core.Diff;

public enum ChangeKind
{
    Added,
    Removed,
    Modified,
    RenamedCandidate,
}

public enum ChangeSeverity
{
    Info,
    Compatible,
    Breaking,
}

public sealed record SchemaChange(
    ChangeKind Kind, string Path, string? Before, string? After, ChangeSeverity Severity, string? Note = null);

public sealed record SchemaDiff(string FromHash, string ToHash, IReadOnlyList<SchemaChange> Changes)
{
    public bool IsEmpty => Changes.Count == 0;
}

public interface ISchemaDiffer
{
    SchemaDiff Compare(SchemaModel before, SchemaModel after);
}
