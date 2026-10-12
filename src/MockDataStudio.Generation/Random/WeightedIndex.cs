namespace MockDataStudio.Generation.Random;

// Tabela de alias de Vose: amostragem ponderada em O(1) (docs/15 §4, C05 §3).
public sealed class WeightedIndex
{
    private readonly double[] _probability;
    private readonly int[] _alias;

    public WeightedIndex(IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
        {
            throw new ArgumentException("Sem pesos.", nameof(weights));
        }

        if (weights.Any(w => double.IsNaN(w) || double.IsInfinity(w) || w < 0))
        {
            throw new ArgumentException("Pesos devem ser finitos e não negativos.", nameof(weights));
        }

        var n = weights.Count;
        var total = weights.Sum();
        if (total <= 0)
        {
            throw new ArgumentException("A soma dos pesos deve ser positiva.", nameof(weights));
        }

        Count = n;
        _probability = new double[n];
        _alias = new int[n];
        var scaled = weights.Select(w => w * n / total).ToArray();
        var small = new Stack<int>();
        var large = new Stack<int>();
        for (var i = 0; i < n; i++)
        {
            (scaled[i] < 1.0 ? small : large).Push(i);
        }

        while (small.Count > 0 && large.Count > 0)
        {
            var s = small.Pop();
            var l = large.Pop();
            _probability[s] = scaled[s];
            _alias[s] = l;
            scaled[l] = scaled[l] + scaled[s] - 1.0;
            (scaled[l] < 1.0 ? small : large).Push(l);
        }

        while (large.Count > 0)
        {
            _probability[large.Pop()] = 1.0;
        }

        while (small.Count > 0)
        {
            _probability[small.Pop()] = 1.0;
        }
    }

    public int Count { get; }

    internal double Probability(long column) => _probability[column];

    internal int Alias(long column) => _alias[column];
}
