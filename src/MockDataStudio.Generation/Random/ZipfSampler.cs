namespace MockDataStudio.Generation.Random;

// Amostrador Zipf determinístico (docs/15 §6).
// Para N <= 1.000.000 usa tabela cumulativa pré-computada; acima, rejeição.
internal sealed class ZipfSampler
{
    private readonly double[]? _cdf;
    private readonly double _s;
    private readonly long _n;

    public ZipfSampler(long n, double s)
    {
        if (n <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "n deve ser positivo.");
        }

        if (s <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(s), "s deve ser positivo.");
        }

        _n = n;
        _s = s;

        if (n <= 1_000_000)
        {
            _cdf = new double[n];
            double sum = 0;
            for (var i = 1; i <= n; i++)
            {
                sum += 1.0 / Math.Pow(i, s);
                _cdf[i - 1] = sum;
            }
            for (var i = 0; i < n; i++)
            {
                _cdf[i] /= sum;
            }
        }
    }

    public long Sample(ref DeterministicRandom rng)
    {
        if (_cdf is not null)
        {
            var u = rng.NextDouble();
            var idx = Array.BinarySearch(_cdf, u);
            if (idx < 0)
            {
                idx = ~idx;
            }

            if (idx >= _n)
            {
                idx = (int)(_n - 1);
            }

            return idx;
        }

        while (true)
        {
            var u = rng.NextDouble();
            var exponent = _s > 1.0 ? -1.0 / (_s - 1.0) : -1.0;
            var x = Math.Floor(Math.Pow(u, exponent));
            if (x >= 1 && x <= _n)
            {
                return (long)(x - 1);
            }
        }
    }
}
