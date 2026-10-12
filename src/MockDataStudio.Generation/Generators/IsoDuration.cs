using System.Globalization;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Generation.Generators;

// Duração ISO-8601 com sinal opcional (15 §6, notas). TimeSpan não representa anos/meses, então os componentes
// são aplicados em ordem de calendário: anos → meses → dias → tempo, sobre um instante de referência.
public sealed record IsoDuration(int Sign, int Years, int Months, int Days, TimeSpan Time)
{
    private static readonly Regex Pattern = new(
        @"^(?<sign>[+-])?P(?:(?<y>\d+)Y)?(?:(?<m>\d+)M)?(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<mi>\d+)M)?(?:(?<s>\d+)S)?)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static Result<IsoDuration> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = Pattern.Match(text.Trim());
        if (!match.Success || text.Trim().EndsWith('T') || text.Trim() == "P" || text.Trim() == "+P" || text.Trim() == "-P")
        {
            return Result<IsoDuration>.Fail(ErrorCodes.GenParamType, $"Duração ISO-8601 inválida: '{text}'.");
        }

        int Group(string name) => match.Groups[name].Success ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : 0;

        var sign = match.Groups["sign"].Value == "-" ? -1 : 1;
        var time = new TimeSpan(0, Group("h"), Group("mi"), Group("s"));
        var days = Group("d") + 7 * Group("w");
        return Result<IsoDuration>.Ok(new IsoDuration(sign, Group("y"), Group("m"), days, time));
    }

    // Aplica a duração ao instante. Sinal negativo inverte cada componente.
    public DateTime Apply(DateTime instant)
    {
        var result = instant;
        result = result.AddYears(Sign * Years);
        result = result.AddMonths(Sign * Months);
        result = result.AddDays(Sign * Days);
        return result.Add(Sign < 0 ? -Time : Time);
    }
}
