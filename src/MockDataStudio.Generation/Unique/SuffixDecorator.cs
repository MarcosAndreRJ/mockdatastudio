namespace MockDataStudio.Generation.Unique;

// Decoração por sufixo para garantia de unicidade em strings (docs/15 §8.2, Estratégia 4).
public static class SuffixDecorator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string ToBase36(long value)
    {
        if (value <= 0)
        {
            return "0";
        }

        Span<char> buffer = stackalloc char[16];
        var pos = buffer.Length;
        var v = (ulong)value;
        while (v > 0)
        {
            buffer[--pos] = Alphabet[(int)(v % 36)];
            v /= 36;
        }

        return new string(buffer[pos..]);
    }

    public static string Decorate(string baseValue, long rowIndex) =>
        baseValue + "-" + ToBase36(rowIndex);

    public static int SuffixLength(long maxRowIndex) =>
        1 + ToBase36(maxRowIndex).Length;
}
