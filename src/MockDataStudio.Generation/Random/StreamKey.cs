using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace MockDataStudio.Generation.Random;

// Chave de fluxo (docs/15 §4.1, RngAlgorithm.Version = 1). Derivação congelada: alterar qualquer byte exige nova versão.
public readonly record struct StreamKey(ulong Value)
{
    private const string Prefix = "mds/v1|";

    // StreamKey.Derive(seed, scope, purpose) = XxHash64(UTF8("mds/v1|" + seed + "|" + scope + "|" + purpose), 0)
    public static StreamKey Derive(ulong seed, string scope, string purpose)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(purpose);
        var text = Prefix + seed.ToString(CultureInfo.InvariantCulture) + "|" + scope + "|" + purpose;
        return new StreamKey(XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(text), 0));
    }

    // StreamKey.Child(purpose) = XxHash64(UTF8(hex16(parent) + "|" + purpose), 0). hex16 em maiúsculas (DA-045).
    public StreamKey Child(string purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        var text = Value.ToString("X16", CultureInfo.InvariantCulture) + "|" + purpose;
        return new StreamKey(XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(text), 0));
    }
}
