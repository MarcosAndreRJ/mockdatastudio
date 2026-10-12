using MockDataStudio.Core.Schema;

namespace MockDataStudio.Providers;

public interface ISqlLiteralFormatter
{
    string Null { get; }
    string Format(object? value, DataTypeInfo type);
}
