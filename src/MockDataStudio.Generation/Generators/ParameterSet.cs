using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Generation.Generators;

// Parâmetros validados contra o descritor (15 §6, §11.3). Erros usam GEN-PARAM-REQUIRED/TYPE/RANGE.
public sealed class ParameterSet
{
    private readonly Dictionary<string, JsonNode?> _values = new(StringComparer.Ordinal);

    private ParameterSet()
    {
    }

    public static Result<ParameterSet> Bind(JsonObject? json, IReadOnlyList<GeneratorParameterDefinition> definitions)
    {
        var set = new ParameterSet();
        foreach (var definition in definitions)
        {
            JsonNode? node = null;
            var present = json is not null && json.TryGetPropertyValue(definition.Name, out node) && node is not null;
            if (!present)
            {
                if (definition.Required)
                {
                    return Result<ParameterSet>.Fail(ErrorCodes.GenParamRequired,
                        $"Parâmetro obrigatório '{definition.Name}' ausente.");
                }

                set._values[definition.Name] = definition.Default?.DeepClone();
                continue;
            }

            var check = Check(definition, node!);
            if (!check.IsSuccess)
            {
                return Result<ParameterSet>.Fail(check.Error);
            }

            set._values[definition.Name] = node!.DeepClone();
        }

        return Result<ParameterSet>.Ok(set);
    }

    public long GetLong(string name) => checked((long)GetNumber(Require(name)));

    public int GetInt(string name) => checked((int)GetLong(name));

    public double GetDouble(string name) => IsNumber(Require(name), out var value) ? value : throw new InvalidOperationException("Número esperado.");

    public decimal GetDecimal(string name) => AsDecimal(Require(name));

    public string GetString(string name) => Require(name).GetValue<string>();

    public bool GetBool(string name) => Require(name).GetValue<bool>();

    public string? OptionalString(string name) =>
        _values.TryGetValue(name, out var node) && node is not null ? node.GetValue<string>() : null;

    public IReadOnlyList<string> GetStringList(string name) =>
        Require(name).AsArray().Select(n => n!.GetValue<string>()).ToList();

    public IReadOnlyList<double>? GetDoubleList(string name) =>
        _values.TryGetValue(name, out var node) && node is not null
            ? node.AsArray().Select(n => GetNumber(n!)).ToList()
            : null;

    public IReadOnlyList<int>? GetIntList(string name) =>
        _values.TryGetValue(name, out var node) && node is not null
            ? node.AsArray().Select(n => (int)GetNumber(n!)).ToList()
            : null;

    public double? OptionalDouble(string name) =>
        _values.TryGetValue(name, out var node) && node is not null && IsNumber(node, out var v) ? v : null;

    public bool Has(string name) => _values.TryGetValue(name, out var node) && node is not null;

    private JsonNode Require(string name)
    {
        if (!_values.TryGetValue(name, out var node) || node is null)
        {
            throw new InvalidOperationException($"Parâmetro '{name}' não definido no descritor.");
        }

        return node;
    }

    private static Result Check(GeneratorParameterDefinition definition, JsonNode node)
    {
        var name = definition.Name;
        switch (definition.Type)
        {
            case "int":
            case "long":
                if (!IsNumber(node, out var number) || number != Math.Truncate(number))
                {
                    return Fail(ErrorCodes.GenParamType, name, "inteiro");
                }

                return InRange(definition, number, name);

            case "decimal":
            case "double":
                if (!IsNumber(node, out var fraction))
                {
                    return Fail(ErrorCodes.GenParamType, name, "número");
                }

                return InRange(definition, fraction, name);

            case "string":
            case "duration":
            case "datetime":
                return node is JsonValue s && s.TryGetValue<string>(out _)
                    ? Result.Ok()
                    : Fail(ErrorCodes.GenParamType, name, "texto");

            case "bool":
                return node is JsonValue b && b.TryGetValue<bool>(out _)
                    ? Result.Ok()
                    : Fail(ErrorCodes.GenParamType, name, "booleano");

            case "enum":
                if (node is not JsonValue e || !e.TryGetValue<string>(out var option))
                {
                    return Fail(ErrorCodes.GenParamType, name, "texto");
                }

                return definition.EnumValues is null || definition.EnumValues.Contains(option, StringComparer.Ordinal)
                    ? Result.Ok()
                    : Result.Fail(ErrorCodes.GenParamRange, $"Parâmetro '{name}' deve ser um de: {string.Join(", ", definition.EnumValues)}.");

            case "stringList":
                return node is JsonArray strings && strings.All(x => x is JsonValue v && v.TryGetValue<string>(out _))
                    ? Result.Ok()
                    : Fail(ErrorCodes.GenParamType, name, "lista de textos");

            case "doubleList":
                return node is JsonArray numbers && numbers.All(x => x is not null && IsNumber(x, out _))
                    ? Result.Ok()
                    : Fail(ErrorCodes.GenParamType, name, "lista de números");

            case "intList":
                return node is JsonArray ints && ints.All(x => x is not null && IsNumber(x, out var n) && n == Math.Truncate(n))
                    ? Result.Ok()
                    : Fail(ErrorCodes.GenParamType, name, "lista de inteiros");

            default:
                return Result.Fail(ErrorCodes.GenParamType, $"Tipo de parâmetro '{definition.Type}' desconhecido em '{name}'.");
        }
    }

    // Aceita qualquer representação numérica JSON (int, long, double, decimal ou JsonElement).
    private static bool IsNumber(JsonNode node, out double value)
    {
        value = 0;
        if (node is not JsonValue v)
        {
            return false;
        }

        try
        {
            var obj = v.GetValue<object>();
            if (obj is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.Number && elem.TryGetDouble(out var d))
                {
                    value = d;
                    return true;
                }

                return false;
            }

            value = Convert.ToDouble(obj, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static decimal AsDecimal(JsonNode node) => node is JsonValue v && v.TryGetValue<decimal>(out var d)
        ? d
        : (decimal)GetNumber(node);

    private static double GetNumber(JsonNode node) => IsNumber(node, out var value) ? value : throw new InvalidOperationException("Número esperado.");

    private static Result InRange(GeneratorParameterDefinition definition, double value, string name)
    {
        if ((definition.Min is { } min && value < min) || (definition.Max is { } max && value > max))
        {
            return Result.Fail(ErrorCodes.GenParamRange,
                string.Create(CultureInfo.InvariantCulture, $"Parâmetro '{name}' fora da faixa [{definition.Min}, {definition.Max}]."));
        }

        return Result.Ok();
    }

    private static Result Fail(string code, string name, string expected) =>
        Result.Fail(code, $"Parâmetro '{name}' deve ser {expected}.");
}
