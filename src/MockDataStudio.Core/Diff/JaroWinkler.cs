namespace MockDataStudio.Core.Diff;

// Similaridade Jaro-Winkler local (sem pacote externo), usada para candidatos de renomeação (12 §8).
public static class JaroWinkler
{
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1.0;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0.0;
        }

        var matchWindow = Math.Max(a.Length, b.Length) / 2 - 1;
        matchWindow = Math.Max(matchWindow, 0);

        var aMatches = new bool[a.Length];
        var bMatches = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var start = Math.Max(0, i - matchWindow);
            var end = Math.Min(i + matchWindow + 1, b.Length);
            for (var j = start; j < end; j++)
            {
                if (bMatches[j] || a[i] != b[j])
                {
                    continue;
                }

                aMatches[i] = true;
                bMatches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0)
        {
            return 0.0;
        }

        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatches[i])
            {
                continue;
            }

            while (!bMatches[k])
            {
                k++;
            }

            if (a[i] != b[k])
            {
                transpositions++;
            }

            k++;
        }

        var m = matches;
        var jaro = ((m / (double)a.Length) + (m / (double)b.Length) + ((m - transpositions / 2.0) / m)) / 3.0;

        var prefix = 0;
        while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix])
        {
            prefix++;
        }

        return jaro + prefix * 0.1 * (1 - jaro);
    }
}
