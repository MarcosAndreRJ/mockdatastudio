using System.Globalization;
using System.Text;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Templates;
using MockDataStudio.Generation.Generators;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Generation.Expressions;

// Ambiente de avaliação: catálogo carregado, referência temporal, semente e escopo da tabela (para slots).
public sealed record ExprEnvironment(
    IReadOnlyDictionary<string, TemplateDataSet> Templates,
    DateTimeOffset ReferenceInstant,
    TimeZoneInfo TimeZone,
    ulong Seed,
    string TableScope);

// Expressão compilada (15 §9): AST imutável, reutilizável entre linhas e threads.
public sealed class CompiledExpr
{
    private static readonly Dictionary<string, (int Min, int Max)> Arity = new(StringComparer.Ordinal)
    {
        ["lower"] = (1, 1), ["upper"] = (1, 1), ["trim"] = (1, 1), ["slug"] = (1, 1), ["length"] = (1, 1),
        ["substr"] = (3, 3), ["left"] = (2, 2), ["right"] = (2, 2), ["pad_left"] = (3, 3), ["replace"] = (3, 3),
        ["concat"] = (1, 64), ["coalesce"] = (1, 64), ["if"] = (3, 3), ["row_index"] = (0, 0),
        ["pick"] = (1, 64), ["rand_int"] = (2, 2), ["rand_decimal"] = (3, 3), ["template"] = (1, 1),
        ["field"] = (2, 3), ["ref_now"] = (0, 0), ["date_add"] = (2, 2), ["date_diff_days"] = (2, 2),
        ["format_date"] = (2, 2), ["year"] = (1, 1), ["month"] = (1, 1), ["day"] = (1, 1),
    };

    private readonly ExprNode _root;
    private readonly ExprEnvironment _env;

    private CompiledExpr(ExprNode root, IReadOnlyList<string> columns, ExprEnvironment env)
    {
        _root = root;
        _env = env;
        ReferencedColumns = columns;
    }

    public IReadOnlyList<string> ReferencedColumns { get; }

    // Compila e valida: sintaxe, limites, funções desconhecidas, aridade e colunas (quando a lista é informada).
    public static Result<CompiledExpr> Compile(string source, ExprMode mode, ExprEnvironment env, IReadOnlyCollection<string>? knownColumns = null)
    {
        var parsed = MdsExprParser.Parse(source, mode, out _);
        if (!parsed.IsSuccess)
        {
            return Result<CompiledExpr>.Fail(parsed.Error);
        }

        var columns = new SortedSet<string>(StringComparer.Ordinal);
        var check = Validate(parsed.Value, columns, knownColumns, depth: 0);
        if (!check.IsSuccess)
        {
            return Result<CompiledExpr>.Fail(check.Error);
        }

        return Result<CompiledExpr>.Ok(new CompiledExpr(parsed.Value, columns.ToList(), env));
    }

    public object? Evaluate(ref GenerationCell cell) => Eval(_root, ref cell);

    private static Result Validate(ExprNode node, SortedSet<string> columns, IReadOnlyCollection<string>? known, int depth)
    {
        if (depth > MdsExprParser.MaxDepth)
        {
            return Result.Fail(ErrorCodes.GenExprLimit, "Profundidade máxima de 64 excedida.");
        }

        switch (node)
        {
            case ColumnNode column:
                if (known is not null && !known.Contains(column.Name))
                {
                    return Result.Fail(ErrorCodes.GenExprColumn, $"Coluna '{column.Name}' não existe na tabela.");
                }

                columns.Add(column.Name);
                return Result.Ok();
            case UnaryNode unary:
                return Validate(unary.Operand, columns, known, depth + 1);
            case BinaryNode binary:
                var left = Validate(binary.Left, columns, known, depth + 1);
                return left.IsSuccess ? Validate(binary.Right, columns, known, depth + 1) : left;
            case CallNode call:
                if (!Arity.TryGetValue(call.Name, out var range))
                {
                    return Result.Fail(ErrorCodes.GenExprUnknownFn, $"Função '{call.Name}' desconhecida.");
                }

                if (call.Arguments.Count < range.Min || call.Arguments.Count > range.Max)
                {
                    return Result.Fail(ErrorCodes.GenExprSyntax, $"Função '{call.Name}' recebeu {call.Arguments.Count} argumento(s).");
                }

                foreach (var argument in call.Arguments)
                {
                    var r = Validate(argument, columns, known, depth + 1);
                    if (!r.IsSuccess)
                    {
                        return r;
                    }
                }

                return Result.Ok();
            case TemplateNode template:
                foreach (var part in template.Parts)
                {
                    var r = Validate(part, columns, known, depth + 1);
                    if (!r.IsSuccess)
                    {
                        return r;
                    }
                }

                return Result.Ok();
            default:
                return Result.Ok();
        }
    }

    private object? Eval(ExprNode node, ref GenerationCell cell)
    {
        switch (node)
        {
            case LiteralNode literal:
                return literal.Value;

            case ColumnNode column:
                return cell.Row.Get(column.Name);

            case TemplateNode template:
                var sb = new StringBuilder();
                foreach (var part in template.Parts)
                {
                    sb.Append(Text(Eval(part, ref cell)));
                }

                return sb.ToString();

            case UnaryNode unary:
                var operand = Eval(unary.Operand, ref cell);
                return unary.Operator == "not" ? Not(operand) : Negate(operand);

            case BinaryNode binary:
                return EvalBinary(binary, ref cell);

            case CallNode call:
                return EvalCall(call, ref cell);

            default:
                throw Type("Nó de expressão desconhecido.");
        }
    }

    private object? EvalBinary(BinaryNode binary, ref GenerationCell cell)
    {
        if (binary.Operator is "and" or "or")
        {
            var a = Eval(binary.Left, ref cell);
            if (a is not null and not bool)
            {
                throw Type($"Operador '{binary.Operator}' exige booleanos.");
            }

            if (binary.Operator == "and" && a is false)
            {
                return false;
            }

            if (binary.Operator == "or" && a is true)
            {
                return true;
            }

            var b = Eval(binary.Right, ref cell);
            if (b is not null and not bool)
            {
                throw Type($"Operador '{binary.Operator}' exige booleanos.");
            }

            return a is null || b is null ? null : b;
        }

        var left = Eval(binary.Left, ref cell);
        var right = Eval(binary.Right, ref cell);
        if (left is null || right is null)
        {
            return null;
        }

        return binary.Operator switch
        {
            "||" => Text(left) + Text(right),
            "+" when left is string || right is string => throw Type("'+' não concatena texto; use '||'."),
            "+" or "-" or "*" or "/" or "%" => Arithmetic(binary.Operator, left, right),
            "=" => Equal(left, right),
            "!=" => !Equal(left, right),
            _ => Compare(binary.Operator, left, right),
        };
    }

    private object? EvalCall(CallNode call, ref GenerationCell cell)
    {
        switch (call.Name)
        {
            case "coalesce":
                foreach (var argument in call.Arguments)
                {
                    var value = Eval(argument, ref cell);
                    if (value is not null)
                    {
                        return value;
                    }
                }

                return null;
            case "if":
                var condition = Eval(call.Arguments[0], ref cell);
                if (condition is not bool flag)
                {
                    throw Type("if() exige condição booleana.");
                }

                return Eval(call.Arguments[flag ? 1 : 2], ref cell);
        }

        var args = new object?[call.Arguments.Count];
        for (var i = 0; i < args.Length; i++)
        {
            args[i] = Eval(call.Arguments[i], ref cell);
        }

        return Invoke(call.Name, args, ref cell);
    }

    private object? Invoke(string name, object?[] args, ref GenerationCell cell)
    {
        switch (name)
        {
            case "lower": return Str(args[0])?.ToLowerInvariant();
            case "upper": return Str(args[0])?.ToUpperInvariant();
            case "trim": return Str(args[0])?.Trim();
            case "slug": return Slug(Str(args[0]));
            case "length": return (long?)Str(args[0])?.Length;
            case "substr": return Substr(Str(args[0]), Int(args[1]), Int(args[2]));
            case "left": return Str(args[0]) is { } l ? l[..Math.Min(l.Length, (int)Int(args[1]))] : null;
            case "right": return Str(args[0]) is { } r ? r[Math.Max(0, r.Length - (int)Int(args[1]))..] : null;
            case "pad_left": return Str(args[0])?.PadLeft((int)Int(args[1]), Str(args[2])![0]);
            case "replace": return Str(args[0])?.Replace(Str(args[1])!, Str(args[2]), StringComparison.Ordinal);
            case "concat": return string.Concat(args.Where(a => a is not null).Select(Text));
            case "row_index": return cell.RowIndex;
            case "pick": return args[cell.Rng.NextInt64(0, args.Length)];
            case "rand_int": return cell.Rng.NextInt64(Int(args[0]), Int(args[1]) + 1);
            case "rand_decimal": return cell.Rng.NextDecimal(Dec(args[0]), Dec(args[1]), (int)Int(args[2]));
            case "template": return TemplateScalar(Str(args[0])!, ref cell);
            case "field": return TemplateField(Str(args[0])!, Str(args[1])!, args.Length > 2 ? Str(args[2]) : Str(args[0]), ref cell);
            case "ref_now": return _env.ReferenceInstant.DateTime;
            case "date_add": return Duration(Str(args[1])!).Apply(Date(args[0]));
            case "date_diff_days": return (long)(Date(args[1]).Date - Date(args[0]).Date).TotalDays;
            case "format_date": return Date(args[0]).ToString(Str(args[1])!, CultureInfo.InvariantCulture);
            case "year": return (long)Date(args[0]).Year;
            case "month": return (long)Date(args[0]).Month;
            case "day": return (long)Date(args[0]).Day;
            default: throw new ExprRuntimeException(ErrorCodes.GenExprUnknownFn, $"Função '{name}' desconhecida.");
        }
    }

    private string TemplateScalar(string code, ref GenerationCell cell)
    {
        var data = Template(code);
        var weights = data.HasNonUniformWeights ? new WeightedIndex(Enumerable.Range(0, data.Count).Select(data.GetWeight).ToList()) : null;
        var index = weights is null ? (int)cell.Rng.NextInt64(0, data.Count) : cell.Rng.NextWeightedIndex(weights);
        return data.GetScalar(index);
    }

    private string? TemplateField(string code, string field, string? slot, ref GenerationCell cell)
    {
        var data = Template(code);
        var slotName = slot ?? code;
        var key = StreamKey.Derive(_env.Seed, _env.TableScope, "slot:" + slotName);
        var slotRng = DeterministicRandom.For(key, cell.RowIndex);
        var index = cell.Row.GetOrSelectSlotRecord(slotName, data, null, ref slotRng);
        return data.GetField(index, field);
    }

    private TemplateDataSet Template(string code) =>
        _env.Templates.TryGetValue(code, out var data)
            ? data
            : throw new ExprRuntimeException(ErrorCodes.GenParamValue, $"Template '{code}' não foi carregado.");

    private static object? Not(object? value) => value switch
    {
        null => null,
        bool b => !b,
        _ => throw Type("'not' exige booleano."),
    };

    private static object? Negate(object? value) => value switch
    {
        null => null,
        long n => -n,
        decimal m => -m,
        double d => -d,
        _ => throw Type("'-' unário exige número."),
    };

    private static object? Arithmetic(string op, object left, object right)
    {
        if (!IsNumber(left) || !IsNumber(right))
        {
            throw Type($"'{op}' exige números.");
        }

        if (left is long a && right is long b)
        {
            return op switch
            {
                "+" => checked(a + b),
                "-" => checked(a - b),
                "*" => checked(a * b),
                "/" => b == 0 ? throw Div() : a / b,
                _ => b == 0 ? throw Div() : a % b,
            };
        }

        var x = ToDecimal(left);
        var y = ToDecimal(right);
        return op switch
        {
            "+" => x + y,
            "-" => x - y,
            "*" => x * y,
            "/" => y == 0 ? throw Div() : x / y,
            _ => y == 0 ? throw Div() : x % y,
        };
    }

    private static bool Equal(object left, object right)
    {
        if (IsNumber(left) && IsNumber(right))
        {
            return ToDecimal(left) == ToDecimal(right);
        }

        if (left is string ls && right is string rs)
        {
            return string.Equals(ls, rs, StringComparison.Ordinal);
        }

        if (left is bool lb && right is bool rb)
        {
            return lb == rb;
        }

        throw Type("Comparação entre tipos diferentes.");
    }

    private static bool Compare(string op, object left, object right)
    {
        int order;
        if (IsNumber(left) && IsNumber(right))
        {
            order = ToDecimal(left).CompareTo(ToDecimal(right));
        }
        else if (left is string ls && right is string rs)
        {
            order = string.CompareOrdinal(ls, rs);
        }
        else
        {
            throw Type($"'{op}' exige dois números ou dois textos.");
        }

        return op switch
        {
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            _ => order >= 0,
        };
    }

    private static bool IsNumber(object value) => value is long or decimal or double;

    private static decimal ToDecimal(object value) => value switch
    {
        long n => n,
        decimal m => m,
        double d => (decimal)d,
        _ => throw Type("Número esperado."),
    };

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string? Str(object? value) => value switch
    {
        null => null,
        string s => s,
        _ => throw Type("Texto esperado."),
    };

    private static long Int(object? value) => value switch
    {
        long n => n,
        decimal m => (long)m,
        _ => throw Type("Inteiro esperado."),
    };

    private static decimal Dec(object? value) => value switch
    {
        long n => n,
        decimal m => m,
        _ => throw Type("Número esperado."),
    };

    private static DateTime Date(object? value) => value switch
    {
        DateTime dt => dt,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
        _ => throw Type("Data esperada."),
    };

    private static IsoDuration Duration(string text) => IsoDuration.Parse(text).IsSuccess
        ? IsoDuration.Parse(text).Value
        : throw new ExprRuntimeException(ErrorCodes.GenExprType, $"Duração inválida: '{text}'.");

    private static string? Slug(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = new string(decomposed.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var sb = new StringBuilder();
        var pendingSeparator = false;
        foreach (var c in plain.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingSeparator && sb.Length > 0)
                {
                    sb.Append('.');
                }

                sb.Append(c);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return sb.ToString();
    }

    private static string? Substr(string? text, long start, long length)
    {
        if (text is null)
        {
            return null;
        }

        var from = (int)Math.Clamp(start - 1, 0, text.Length);
        var count = (int)Math.Clamp(length, 0, text.Length - from);
        return text.Substring(from, count);
    }

    private static ExprRuntimeException Type(string message) => new(ErrorCodes.GenExprType, message);

    private static ExprRuntimeException Div() => new(ErrorCodes.GenExprType, "Divisão por zero.");
}

// Erro de avaliação com código GEN-* (o motor o encapsula com tabela, coluna e linha).
public sealed class ExprRuntimeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
