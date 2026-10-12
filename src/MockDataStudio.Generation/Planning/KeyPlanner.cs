using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Generation.Planning;

// Reserva de faixa para chave Explicit inteira (16 §5.2):
// start = max(MAX(col) no destino, valor da sequence, 0) + 1 + KeyOffset (ou KeyStartOverride);
// step = 1; faixa = [start, start + step·(RowCount−1)].
public static class KeyPlanner
{
    public static (KeyColumnPlan Plan, IReadOnlyList<PlanDiagnostic> Diagnostics) PlanExplicitRange(
        string table, string column, DataTypeInfo type, long rowCount, long? maxExisting, long? sequenceNext,
        long? startOverride, long keyOffset, bool offline, bool needsIdentityOverride, string? sequence)
    {
        var diagnostics = new List<PlanDiagnostic>();
        var path = table + "." + column;

        long start;
        if (startOverride is { } forced)
        {
            start = forced;
        }
        else if (offline)
        {
            // Sem destino: a faixa não é verificada. Só o aviso PLN-KEY-OFFLINE registra isso.
            start = 1;
            diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Warning, "PLN-KEY-OFFLINE",
                $"Faixa de chaves de '{path}' não verificada contra o destino (modo offline).", path,
                "Conecte o destino ou defina KeyStartOverride."));
        }
        else
        {
            var baseline = Math.Max(Math.Max(maxExisting ?? 0, sequenceNext ?? 0), 0);
            start = baseline >= long.MaxValue - 1 - keyOffset ? long.MaxValue : baseline + 1 + keyOffset;
        }

        var last = rowCount > 1 && start <= long.MaxValue - (rowCount - 1) ? start + (rowCount - 1) : rowCount > 1 ? long.MaxValue : start;
        var max = MaxValue(type);
        if (last > max || start > max)
        {
            diagnostics.Add(new PlanDiagnostic(DiagnosticSeverity.Error, "PLN-KEY-OVERFLOW",
                $"A faixa de '{path}' ({start}..{last}) excede o limite do tipo '{type.NativeName}' ({max}).", path,
                "Reduza a quantidade de linhas, use um tipo maior ou defina KeyStartOverride dentro da faixa."));
        }

        var plan = new KeyColumnPlan(column, KeyStrategy.Explicit, start, 1, needsIdentityOverride, sequence);
        return (plan, diagnostics);
    }

    // Limite superior do tipo inteiro. Tipo sem largura conhecida não tem verificação de overflow (long.MaxValue).
    public static long MaxValue(DataTypeInfo type)
    {
        if (type.IntegerBits is not { } bits || bits >= 64)
        {
            return long.MaxValue;
        }

        return type.IsUnsigned
            ? (1L << bits) - 1
            : (1L << (bits - 1)) - 1;
    }
}
