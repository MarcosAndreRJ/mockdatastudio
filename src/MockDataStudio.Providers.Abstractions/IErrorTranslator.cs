using MockDataStudio.Core.Common;
using MockDataStudio.Providers.Models;

namespace MockDataStudio.Providers;

public interface IErrorTranslator
{
    MdsError Translate(Exception ex);
    DbErrorCategory Categorize(Exception ex);
}
