using System.Globalization;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Expressions;

namespace MockDataStudio.Generation.Generators;

// Geradores V1 implementados nesta versão (docs/15 §6). Descritores e validação seguem C05 §4.
internal static class BuiltInGenerators
{
    private const string TextKind = "string";
    private const string IntKind = "long";
    private const string DblKind = "double";
    private const string DecKind = "decimal";
    private const string EnumKind = "enum";
    private const string DurationKind = "duration";
    private const string DateTimeKind = "datetime";
    private const string ListKind = "stringList";
    private const string DoubleListKind = "doubleList";

    private static readonly string[] Granularities = ["day", "hour", "minute", "second", "millisecond"];

    public static IEnumerable<IGeneratorFactory> All() =>
    [
        Basic("core.fixed", "Valor fixo", [GeneratorFactory.Required("value", TextKind, "Valor")],
            (p, _) => Ok(new FixedGenerator(p.GetString("value")))),
        Basic("core.null", "Nulo", [], (_, _) => Ok(new NullGenerator())),
        Basic("core.skip", "Omitir coluna", [], (_, _) => Ok(new SkipGenerator())),
        Basic("core.bool", "Booleano",
            [GeneratorFactory.Optional("probabilityTrue", DblKind, "Probabilidade de verdadeiro", 0.5, min: 0, max: 1)],
            (p, _) => Ok(new BoolGenerator(p.GetDouble("probabilityTrue")))),
        Basic("core.list.random", "Lista aleatória",
            [GeneratorFactory.Required("values", ListKind, "Valores"), GeneratorFactory.Optional("weights", DoubleListKind, "Pesos", null)],
            (p, _) => ListOf(p, sequential: false, offset: 0)),
        Basic("core.list.sequential", "Lista sequencial",
            [GeneratorFactory.Required("values", ListKind, "Valores"), GeneratorFactory.Optional("offset", IntKind, "Deslocamento", 0L)],
            (p, _) => ListOf(p, sequential: true, offset: p.GetInt("offset"))),
        Basic("core.uuid", "UUID v4",
            [GeneratorFactory.Optional("version", EnumKind, "Versão", "v4", ["v4"]),
             GeneratorFactory.Optional("format", EnumKind, "Formato", "standard", ["standard"])],
            (_, _) => Ok(new UuidV4Generator())),
        Basic("core.random.bytes", "Bytes aleatórios",
            [GeneratorFactory.Optional("length", IntKind, "Tamanho", 16L, min: 1, max: 65536)],
            (p, _) => Ok(new BytesGenerator(p.GetInt("length")))),

        Basic("core.sequence.int", "Sequência inteira",
            [GeneratorFactory.Optional("start", IntKind, "Início", 1L), GeneratorFactory.Optional("step", IntKind, "Passo", 1L, min: 1)],
            (p, _) => Ok(new SequenceIntGenerator(p.GetLong("start"), p.GetLong("step")))),
        Basic("core.random.int", "Inteiro aleatório",
            [GeneratorFactory.Required("min", IntKind, "Mínimo"), GeneratorFactory.Required("max", IntKind, "Máximo")],
            (p, _) => p.GetLong("min") <= p.GetLong("max") && p.GetLong("max") < long.MaxValue
                ? Ok(new RandomIntGenerator(p.GetLong("min"), p.GetLong("max")))
                : Fail(ErrorCodes.GenParamValue, "min deve ser ≤ max e max < 2^63-1.")),
        Basic("core.random.decimal", "Decimal aleatório",
            [GeneratorFactory.Required("min", DecKind, "Mínimo"), GeneratorFactory.Required("max", DecKind, "Máximo"),
             GeneratorFactory.Optional("scale", IntKind, "Escala", 2L, min: 0, max: 9)],
            (p, _) => p.GetDecimal("min") <= p.GetDecimal("max")
                ? Ok(new RandomDecimalGenerator(p.GetDecimal("min"), p.GetDecimal("max"), p.GetInt("scale")))
                : Fail(ErrorCodes.GenParamValue, "min deve ser ≤ max.")),
        Basic("core.random.float", "Real aleatório",
            [GeneratorFactory.Required("min", DblKind, "Mínimo"), GeneratorFactory.Required("max", DblKind, "Máximo")],
            (p, _) => p.GetDouble("min") <= p.GetDouble("max")
                ? Ok(new RandomFloatGenerator(p.GetDouble("min"), p.GetDouble("max")))
                : Fail(ErrorCodes.GenParamValue, "min deve ser ≤ max.")),

        Dates("core.date.range", "Intervalo de datas",
            [GeneratorFactory.Required("start", DateTimeKind, "Início"), GeneratorFactory.Required("end", DateTimeKind, "Fim"),
             GeneratorFactory.Optional("granularity", EnumKind, "Granularidade", "day", Granularities)],
            (p, _) => Range(ParseDate(p.GetString("start")), ParseDate(p.GetString("end")), p.GetString("granularity"))),
        Dates("core.date.relative", "Data relativa",
            [GeneratorFactory.Required("from", DurationKind, "Início (duração a partir da referência)"),
             GeneratorFactory.Optional("to", DurationKind, "Fim (duração a partir da referência)", "P0D"),
             GeneratorFactory.Optional("granularity", EnumKind, "Granularidade", "day", Granularities)],
            Relative),
        Dates("core.date.sequence", "Sequência de datas",
            [GeneratorFactory.Required("start", DateTimeKind, "Início"), GeneratorFactory.Required("step", DurationKind, "Passo")],
            Sequence),

        Text("core.random.string", "Texto aleatório",
            [GeneratorFactory.Required("minLength", IntKind, "Mínimo", min: 0), GeneratorFactory.Required("maxLength", IntKind, "Máximo", min: 0),
             GeneratorFactory.Optional("charset", EnumKind, "Conjunto", "alnum", ["alpha", "alnum", "numeric", "hex"])],
            (p, _) => p.GetInt("minLength") <= p.GetInt("maxLength")
                ? Ok(new RandomStringGenerator(p.GetInt("minLength"), p.GetInt("maxLength"), p.GetString("charset")))
                : Fail(ErrorCodes.GenParamValue, "minLength deve ser ≤ maxLength.")),
        Text("core.mask", "Máscara", [GeneratorFactory.Required("pattern", TextKind, "Padrão")],
            (p, _) => Ok(new MaskGenerator(p.GetString("pattern")))),

        Expression("core.format", "Formatação (template MDS-Expr)", [GeneratorFactory.Required("template", TextKind, "Template")],
            ExprMode.Template, "template"),
        Expression("core.expr", "Expressão MDS-Expr", [GeneratorFactory.Required("expression", TextKind, "Expressão")],
            ExprMode.Expression, "expression"),

        Catalog("catalog.template", "Template escalar",
            [GeneratorFactory.Required("template", TextKind, "Template"),
             GeneratorFactory.Optional("selection", EnumKind, "Seleção", "weighted", ["weighted", "uniform", "sequential"]),
             GeneratorFactory.Optional("padLeft", IntKind, "Zeros à esquerda", null, min: 1, max: 64)],
            CatalogScalar),
        Catalog("catalog.template.field", "Campo de template composto",
            [GeneratorFactory.Required("template", TextKind, "Template"), GeneratorFactory.Required("field", TextKind, "Campo"),
             GeneratorFactory.Optional("slot", TextKind, "Slot", null),
             GeneratorFactory.Optional("selection", EnumKind, "Seleção", "weighted", ["weighted", "uniform"])],
            CatalogField),
    ];

    private static Result<IValueGenerator> Ok(IValueGenerator generator) => Result<IValueGenerator>.Ok(generator);

    private static Result<IValueGenerator> Fail(string code, string message) => Result<IValueGenerator>.Fail(code, message);

    private static DateTime ParseDate(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Result<IValueGenerator> ListOf(ParameterSet p, bool sequential, int offset)
    {
        var values = p.GetStringList("values");
        if (values.Count == 0)
        {
            return Fail(ErrorCodes.GenParamValue, "A lista de valores está vazia.");
        }

        var weights = p.GetDoubleList("weights");
        if (weights is not null && weights.Count != values.Count)
        {
            return Fail(ErrorCodes.GenParamValue, "weights deve ter o mesmo tamanho de values.");
        }

        return Ok(new ListGenerator(values, weights, sequential, offset));
    }

    private static Result<IValueGenerator> Range(DateTime start, DateTime end, string granularity) =>
        end < start
            ? Fail(ErrorCodes.GenParamValue, "end deve ser ≥ start.")
            : Ok(new DateRangeGenerator(start, end, granularity));

    private static Result<IValueGenerator> Relative(ParameterSet p, GeneratorCreateContext ctx)
    {
        var from = IsoDuration.Parse(p.GetString("from"));
        var to = IsoDuration.Parse(p.GetString("to"));
        if (!from.IsSuccess || !to.IsSuccess)
        {
            return Fail(ErrorCodes.GenParamType, "Duração ISO-8601 inválida em from/to.");
        }

        var reference = TimeZoneInfo.ConvertTime(ctx.ReferenceInstant, ctx.TimeZone).DateTime;
        return Range(from.Value.Apply(reference), to.Value.Apply(reference), p.GetString("granularity"));
    }

    private static Result<IValueGenerator> Sequence(ParameterSet p, GeneratorCreateContext ctx)
    {
        var step = IsoDuration.Parse(p.GetString("step"));
        if (!step.IsSuccess)
        {
            return Fail(ErrorCodes.GenParamType, "Duração ISO-8601 inválida em step.");
        }

        if (step.Value.Years != 0 || step.Value.Months != 0)
        {
            return Fail(ErrorCodes.GenUnsupported, "Passo com anos ou meses ainda não suportado.");
        }

        var days = step.Value.Sign * step.Value.Days;
        var time = step.Value.Sign < 0 ? -step.Value.Time : step.Value.Time;
        return Ok(new DateSequenceGenerator(ParseDate(p.GetString("start")), days, time));
    }

    private static Result<IValueGenerator> CatalogScalar(ParameterSet p, GeneratorCreateContext ctx)
    {
        var code = p.GetString("template");
        if (!ctx.Templates.TryGetValue(code, out var data))
        {
            return Fail(ErrorCodes.GenParamValue, $"Template '{code}' não foi carregado.");
        }

        if (data.Kind != TemplateKind.Scalar)
        {
            return Fail(ErrorCodes.GenParamValue, $"'{code}' é composto: use catalog.template.field.");
        }

        var selection = Enum.Parse<TemplateSelection>(p.GetString("selection"), ignoreCase: true);
        var padLeft = p.Has("padLeft") ? (int?)p.GetInt("padLeft") : null;
        return Ok(new CatalogScalarGenerator(data, selection, padLeft));
    }

    private static Result<IValueGenerator> CatalogField(ParameterSet p, GeneratorCreateContext ctx)
    {
        var code = p.GetString("template");
        if (!ctx.Templates.TryGetValue(code, out var data))
        {
            return Fail(ErrorCodes.GenParamValue, $"Template '{code}' não foi carregado.");
        }

        var field = p.GetString("field");
        if (!data.FieldCodes.Contains(field, StringComparer.Ordinal))
        {
            return Fail(ErrorCodes.GenParamValue, $"Campo '{field}' não existe em '{code}'.");
        }

        var slot = p.OptionalString("slot") ?? code;
        return Ok(new CatalogFieldGenerator(data, field, slot, ctx.Seed, ctx.Table.CanonicalId, p.GetString("selection") == "weighted"));
    }

    private static GeneratorFactory Basic(string id, string name, GeneratorParameterDefinition[] parameters,
        Func<ParameterSet, GeneratorCreateContext, Result<IValueGenerator>> create) =>
        Make(id, name, GeneratorCategory.Basic, [LogicalType.Other], parameters, create);

    private static GeneratorFactory Text(string id, string name, GeneratorParameterDefinition[] parameters,
        Func<ParameterSet, GeneratorCreateContext, Result<IValueGenerator>> create) =>
        Make(id, name, GeneratorCategory.Text, [LogicalType.String], parameters, create);

    private static GeneratorFactory Dates(string id, string name, GeneratorParameterDefinition[] parameters,
        Func<ParameterSet, GeneratorCreateContext, Result<IValueGenerator>> create) =>
        Make(id, name, GeneratorCategory.DateTime, [LogicalType.Date, LogicalType.DateTime], parameters, create);

    private static GeneratorFactory Catalog(string id, string name, GeneratorParameterDefinition[] parameters,
        Func<ParameterSet, GeneratorCreateContext, Result<IValueGenerator>> create) =>
        Make(id, name, GeneratorCategory.Catalog, [LogicalType.String], parameters, create);

    private static GeneratorFactory Expression(string id, string name, GeneratorParameterDefinition[] parameters, ExprMode mode, string source) =>
        Make(id, name, GeneratorCategory.Expression, [LogicalType.String, LogicalType.Other], parameters,
            (p, ctx) => ExpressionGenerator.Create(p.GetString(source), mode, ctx));

    private static GeneratorFactory Make(string id, string name, GeneratorCategory category, IReadOnlyList<LogicalType> outputs,
        GeneratorParameterDefinition[] parameters, Func<ParameterSet, GeneratorCreateContext, Result<IValueGenerator>> create) =>
        new GeneratorFactory(GeneratorFactory.Describe(id, name, category, outputs, id, parameters), create);
}
