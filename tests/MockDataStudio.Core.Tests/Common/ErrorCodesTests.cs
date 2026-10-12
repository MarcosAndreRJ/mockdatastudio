using System.Reflection;
using MockDataStudio.Core.Common;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Common;

public sealed class ErrorCodesTests
{
    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-ERR-001")]
    public void T_ERR_001_AllErrorCodes_MustHaveMessageInStringsResx()
    {
        var fields = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToList();

        fields.ShouldNotBeEmpty();

        var missingMessages = new List<string>();

        foreach (var field in fields)
        {
            var code = (string)field.GetValue(null)!;
            var message = Strings.ResourceManager.GetString(code, System.Globalization.CultureInfo.InvariantCulture);

            if (string.IsNullOrWhiteSpace(message))
            {
                missingMessages.Add($"{field.Name} (Code: '{code}')");
            }
        }

        missingMessages.ShouldBeEmpty(
            $"Os seguintes códigos de erro em ErrorCodes não possuem mensagem correspondente em Strings.resx: {string.Join(", ", missingMessages)}");
    }
}
