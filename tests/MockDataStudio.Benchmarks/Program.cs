using System.Diagnostics;
using MockDataStudio.Generation.Random;

namespace MockDataStudio.Benchmarks;

// B-RNG-001 (medição): throughput do RNG determinístico, uma instância por linha como no motor (docs/15 §4).
// Medição informal com Stopwatch; não há meta numérica definida em docs/24 para este identificador.
public static class Program
{
    public static void Main(string[] args)
    {
        _ = args;
        const int rows = 5_000_000;
        var key = StreamKey.Derive(20261012, "main.cliente", "value");

        // Aquecimento para não medir JIT.
        Consume(key, 200_000);

        var clock = Stopwatch.StartNew();
        var checksum = Consume(key, rows);
        clock.Stop();

        var perSecond = rows / clock.Elapsed.TotalSeconds;
        Console.WriteLine($"B-RNG-001: {rows:N0} linhas (1 RNG por linha, 2 saídas)");
        Console.WriteLine($"B-RNG-001: {clock.Elapsed.TotalMilliseconds:N0} ms, {perSecond:N0} linhas/s, checksum {checksum:X16}");
    }

    private static ulong Consume(StreamKey key, int rows)
    {
        ulong checksum = 0;
        for (var row = 0; row < rows; row++)
        {
            var rng = DeterministicRandom.For(key, row);
            checksum ^= rng.NextUInt64();
            checksum ^= rng.NextUInt64();
        }

        return checksum;
    }
}
