namespace MockDataStudio.Core.Schema;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Nomes exigidos pelo contrato C01 (LogicalType).")]
public enum LogicalType
{
    Boolean,
    Integer,
    Decimal,
    Float,
    String,
    Text,
    Binary,
    Date,
    Time,
    DateTime,
    DateTimeTz,
    Uuid,
    Json,
    Enum,
    Interval,
    Other,
}

public enum LengthUnit
{
    Chars,
    Bytes,
}

public enum IdentityKind
{
    AutoIncrement,
    IdentityAlways,
    IdentityByDefault,
    Serial,
    TriggerSequence,
}

public enum ReferentialAction
{
    NoAction,
    Restrict,
    Cascade,
    SetNull,
    SetDefault,
}

public enum TriggerTiming
{
    Before,
    After,
    InsteadOf,
}

[Flags]
public enum TriggerEvents
{
    None = 0,
    Insert = 1,
    Update = 2,
    Delete = 4,
}

public enum RoutineKind
{
    Procedure,
    Function,
}

public enum DefaultKind
{
    Literal,
    Function,
    Sequence,
    Other,
}

public enum UniqueSource
{
    Constraint,
    UniqueIndex,
}
