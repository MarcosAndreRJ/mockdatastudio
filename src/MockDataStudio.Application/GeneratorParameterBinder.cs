using System.Globalization;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;
using MockDataStudio.Generation.Generators;

namespace MockDataStudio.Application;

/// <summary>
/// Converte entre os parâmetros JSON de um <see cref="MockDataStudio.Core.Bindings.GeneratorSpec"/> e os valores textuais do
/// editor de gerador, que é montado a partir dos <see cref="GeneratorParameterDefinition"/> do descriptor (C05 §4).
/// </summary>
public static class GeneratorParameterBinder
{
    /// <summary>Valores iniciais do editor: o parâmetro do spec, ou o padrão do descriptor, ou vazio.</summary>
    public static IReadOnlyDictionary<string, string> ToText(GeneratorDescriptor descriptor, JsonObject? parameters)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in descriptor.Parameters)
        {
            var node = parameters?[definition.Name] ?? definition.Default;
            values[definition.Name] = node is null ? string.Empty : NodeToText(node);
        }

        return values;
    }

    /// <summary>
    /// Monta os parâmetros a partir do texto digitado. Vazio em parâmetro opcional é omitido (vale o padrão);
    /// vazio em obrigatório, valor fora do tipo ou fora de [Min, Max] resultam em falha com mensagem para o usuário.
    /// </summary>
    public static Result<JsonObject> FromText(GeneratorDescriptor descriptor, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(values);

        var result = new JsonObject();
        foreach (var definition in descriptor.Parameters)
        {
            values.TryGetValue(definition.Name, out var text);
            text = text?.Trim() ?? string.Empty;

            if (text.Length == 0)
            {
                if (definition.Required)
                {
                    return Fail(definition, "é obrigatório.");
                }

                continue;
            }

            var parsed = Parse(definition, text);
            if (!parsed.IsSuccess)
            {
                return Result<JsonObject>.Fail(parsed.Error);
            }

            result[definition.Name] = parsed.Value;
        }

        return Result<JsonObject>.Ok(result);
    }

    private static Result<JsonNode> Parse(GeneratorParameterDefinition definition, string text)
    {
        switch (definition.Type)
        {
            case "int":
            case "long":
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    return FailNode(definition, "deve ser um número inteiro.");
                }

                return InRange(definition, integer) ?? Result<JsonNode>.Ok(JsonValue.Create(integer));

            case "decimal":
            case "double":
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    return FailNode(definition, "deve ser um número (use ponto como separador decimal).");
                }

                return InRange(definition, number) ?? Result<JsonNode>.Ok(JsonValue.Create(number));

            case "bool":
                if (!bool.TryParse(text, out var flag))
                {
                    return FailNode(definition, "deve ser true ou false.");
                }

                return Result<JsonNode>.Ok(JsonValue.Create(flag));

            case "enum":
                if (definition.EnumValues is { Count: > 0 } options && !options.Contains(text, StringComparer.Ordinal))
                {
                    return FailNode(definition, "deve ser um de: " + string.Join(", ", options) + ".");
                }

                return Result<JsonNode>.Ok(JsonValue.Create(text));

            case "stringList":
                var strings = new JsonArray();
                foreach (var item in Split(text))
                {
                    strings.Add(item);
                }

                return Result<JsonNode>.Ok(strings);

            case "doubleList":
                var doubles = new JsonArray();
                foreach (var item in Split(text))
                {
                    if (!double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    {
                        return FailNode(definition, $"\"{item}\" não é um número.");
                    }

                    doubles.Add(value);
                }

                return Result<JsonNode>.Ok(doubles);

            default: // string, duration, datetime: texto validado pelo próprio gerador ao criar a instância
                return Result<JsonNode>.Ok(JsonValue.Create(text));
        }
    }

    private static Result<JsonNode>? InRange(GeneratorParameterDefinition definition, double value)
    {
        if (definition.Min is { } min && value < min)
        {
            return FailNode(definition, $"deve ser maior ou igual a {min.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (definition.Max is { } max && value > max)
        {
            return FailNode(definition, $"deve ser menor ou igual a {max.ToString(CultureInfo.InvariantCulture)}.");
        }

        return null;
    }

    // Itens separados por quebra de linha ou ponto e vírgula; vírgula é aceita quando não há nenhum dos dois.
    private static string[] Split(string text)
    {
        var separators = text.Contains('\n', StringComparison.Ordinal) || text.Contains(';', StringComparison.Ordinal)
            ? new[] { '\n', '\r', ';' }
            : [','];
        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NodeToText(JsonNode node) => node switch
    {
        JsonArray array => string.Join("\n", array.Select(item => item is null ? string.Empty : NodeToText(item))),
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag ? "true" : "false",
        JsonValue value when value.TryGetValue<double>(out var number) => number.ToString("R", CultureInfo.InvariantCulture),
        _ => node.ToJsonString(),
    };

    private static Result<JsonObject> Fail(GeneratorParameterDefinition definition, string message) =>
        Result<JsonObject>.Fail(ErrorCodes.GenParamRange, $"{definition.Label}: {message}");

    private static Result<JsonNode> FailNode(GeneratorParameterDefinition definition, string message) =>
        Result<JsonNode>.Fail(ErrorCodes.GenParamRange, $"{definition.Label}: {message}");
}
