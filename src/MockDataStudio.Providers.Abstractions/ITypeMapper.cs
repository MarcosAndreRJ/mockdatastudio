using MockDataStudio.Core.Schema;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface ITypeMapper
{
    DataTypeInfo Map(NativeTypeDescriptor native);
}
