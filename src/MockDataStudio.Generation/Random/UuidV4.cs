namespace MockDataStudio.Generation.Random;

// UUID v4 a partir do RNG determinístico (docs/15 §6). Usado pelo gerador core.uuid e pelo oracle de chaves UUID.
public static class UuidV4
{
    public static Guid Create(ref DeterministicRandom rng)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], rng.NextUInt64());
        BitConverter.TryWriteBytes(bytes[8..], rng.NextUInt64());
        // Guid grava o terceiro grupo em ordem little-endian: o nibble de versão está no byte 7, não no 6.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
