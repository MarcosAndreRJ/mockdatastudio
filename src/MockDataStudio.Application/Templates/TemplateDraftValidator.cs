using System.Globalization;
using System.Text.RegularExpressions;
using MockDataStudio.Catalog;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Templates;

namespace MockDataStudio.Application.Templates;

public sealed record ValidationMessage(string PropertyName, string Message, bool IsError = true);

public sealed record TemplateValidationResult(bool IsValid, IReadOnlyList<ValidationMessage> Messages)
{
    public static TemplateValidationResult Success { get; } = new(true, []);

    public static TemplateValidationResult Failure(params ValidationMessage[] messages) =>
        new(false, messages);

    public static TemplateValidationResult Failure(IEnumerable<ValidationMessage> messages) =>
        new(false, messages.ToList());
}

public static partial class TemplateDraftValidator
{
    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,63}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex FieldCodePattern();

    public static TemplateValidationResult Validate(TemplateDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var messages = new List<ValidationMessage>();

        // Código
        if (string.IsNullOrWhiteSpace(draft.Code))
        {
            messages.Add(new ValidationMessage(nameof(draft.Code), "O código do template é obrigatório."));
        }
        else if (!CodePattern().IsMatch(draft.Code))
        {
            messages.Add(new ValidationMessage(nameof(draft.Code),
                $"Código '{draft.Code}' inválido: use 2 a 64 caracteres maiúsculos, dígitos ou sublinhado, iniciando com letra."));
        }

        // Nome
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            messages.Add(new ValidationMessage(nameof(draft.Name), "O nome do template é obrigatório."));
        }
        else if (draft.Name.Length > 120)
        {
            messages.Add(new ValidationMessage(nameof(draft.Name), "O nome deve ter no máximo 120 caracteres."));
        }

        // Tipo
        if (draft.Kind == TemplateKind.Scalar)
        {
            if (draft.LogicalType is null)
            {
                messages.Add(new ValidationMessage(nameof(draft.LogicalType), "Template escalar exige tipo lógico."));
            }

            if (draft.Values.Count == 0)
            {
                messages.Add(new ValidationMessage(nameof(draft.Values), "Informe ao menos um valor para o template escalar."));
            }
            else if (draft.LogicalType is { } lt)
            {
                for (var i = 0; i < draft.Values.Count; i++)
                {
                    var val = draft.Values[i];
                    if (!IsValidValue(val.Value, lt))
                    {
                        messages.Add(new ValidationMessage(nameof(draft.Values),
                            $"Valor '{val.Value}' (linha {i + 1}) incompatível com o tipo {lt}.", IsError: false));
                    }
                }
            }
        }
        else if (draft.Kind == TemplateKind.Composite)
        {
            if (draft.Fields.Count == 0)
            {
                messages.Add(new ValidationMessage(nameof(draft.Fields), "Template composto exige ao menos um campo."));
            }

            var fieldCodes = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < draft.Fields.Count; i++)
            {
                var f = draft.Fields[i];
                if (string.IsNullOrWhiteSpace(f.FieldCode) || !FieldCodePattern().IsMatch(f.FieldCode))
                {
                    messages.Add(new ValidationMessage(nameof(draft.Fields),
                        $"Campo {i + 1}: código '{f.FieldCode}' inválido (letras minúsculas, dígitos e '_')."));
                }
                else if (!fieldCodes.Add(f.FieldCode))
                {
                    messages.Add(new ValidationMessage(nameof(draft.Fields),
                        $"Código de campo duplicado: '{f.FieldCode}'."));
                }

                if (f.MaxLength is <= 0)
                {
                    messages.Add(new ValidationMessage(nameof(draft.Fields),
                        $"Campo '{f.FieldCode}': tamanho máximo deve ser positivo."));
                }
            }

            // Registros
            for (var r = 0; r < draft.Records.Count; r++)
            {
                var rec = draft.Records[r];
                foreach (var f in draft.Fields)
                {
                    rec.Data.TryGetValue(f.FieldCode, out var val);
                    if (f.Required && string.IsNullOrWhiteSpace(val))
                    {
                        messages.Add(new ValidationMessage(nameof(draft.Records),
                            $"Registro {r + 1}: campo obrigatório '{f.FieldCode}' está vazio."));
                    }
                    else if (!string.IsNullOrEmpty(val) && !IsValidValue(val, f.LogicalType))
                    {
                        messages.Add(new ValidationMessage(nameof(draft.Records),
                            $"Registro {r + 1}, campo '{f.FieldCode}': valor '{val}' incompatível com {f.LogicalType}.", IsError: false));
                    }
                }
            }
        }

        // Aliases
        var validFieldCodes = draft.Fields.Select(f => f.FieldCode).ToHashSet(StringComparer.Ordinal);
        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in draft.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias.Alias))
            {
                messages.Add(new ValidationMessage(nameof(draft.Aliases), "O texto do alias não pode estar vazio."));
            }
            else if (!seenAliases.Add(alias.FieldCode is null ? alias.Alias : $"{alias.Alias}:{alias.FieldCode}"))
            {
                messages.Add(new ValidationMessage(nameof(draft.Aliases), $"Alias duplicado: '{alias.Alias}'.", IsError: false));
            }

            if (alias.Weight is <= 0 or > 2.0)
            {
                messages.Add(new ValidationMessage(nameof(draft.Aliases),
                    $"Peso do alias '{alias.Alias}' deve estar entre 0 e 2.0."));
            }

            if (alias.FieldCode is not null && !validFieldCodes.Contains(alias.FieldCode))
            {
                messages.Add(new ValidationMessage(nameof(draft.Aliases),
                    $"Alias '{alias.Alias}' referencia campo inexistente '{alias.FieldCode}'."));
            }
        }

        var hasErrors = messages.Any(m => m.IsError);
        return hasErrors ? TemplateValidationResult.Failure(messages) : new TemplateValidationResult(true, messages);
    }

    private static bool IsValidValue(string value, LogicalType logicalType)
    {
        return logicalType switch
        {
            LogicalType.Integer => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                || long.TryParse(value, NumberStyles.Integer, CultureInfo.GetCultureInfo("pt-BR"), out _),
            LogicalType.Decimal or LogicalType.Float => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.GetCultureInfo("pt-BR"), out _),
            LogicalType.Boolean => bool.TryParse(value, out _)
                || value is "1" or "0" or "sim" or "não" or "nao" or "s" or "n" or "true" or "false",
            LogicalType.Date => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                || DateTime.TryParse(value, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out _),
            _ => true
        };
    }
}
