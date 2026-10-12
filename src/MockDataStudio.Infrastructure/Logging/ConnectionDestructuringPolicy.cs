using MockDataStudio.Core.Connections;
using Serilog.Core;
using Serilog.Events;

namespace MockDataStudio.Infrastructure.Logging;

/// <summary>
/// Bloqueia o destructuring de ConnectionProfile e ConnectionSettings: só Id e Name chegam ao log (docs/23 §6).
/// </summary>
public sealed class ConnectionDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        switch (value)
        {
            case ConnectionProfile profile:
                result = new StructureValue(
                [
                    new LogEventProperty("Id", new ScalarValue(profile.Id)),
                    new LogEventProperty("Name", new ScalarValue(profile.Name)),
                ]);
                return true;
            case ConnectionSettings:
                result = new ScalarValue("[ConnectionSettings omitido]");
                return true;
            default:
                result = null!;
                return false;
        }
    }
}
