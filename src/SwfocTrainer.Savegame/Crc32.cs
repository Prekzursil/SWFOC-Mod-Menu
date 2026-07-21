namespace SwfocTrainer.Savegame;

/// <summary>
/// IEEE 802.3 CRC-32 (the zlib / PNG variant): reflected input and output,
/// polynomial <c>0xEDB88320</c>, initial value <c>0xFFFFFFFF</c>, final XOR
/// <c>0xFFFFFFFF</c>. This is the default mod-hash algorithm
/// <see cref="ModHashValidator"/> uses to fingerprint a mod's ObjectType
/// definitions.
///
/// <para>
/// The exact algorithm the Alamo engine uses for its embedded mod hash is RE
/// open question 3 — decompile the hash routine near
/// <c>GameObjectTypeList @ 0xA172D0</c>. IEEE CRC-32 is the working assumption:
/// the embedded hash is a 4-byte type-0x01 int32 micro-chunk, the natural width
/// of a CRC-32. If a later RE pass shows a different polynomial, only this
/// table changes; <see cref="ModHashValidator"/> also accepts a custom hash
/// delegate so its comparison and re-anchor logic stay correct meanwhile.
/// </para>
/// </summary>
public static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;
    private static readonly uint[] LookupTable = BuildLookupTable();

    /// <summary>
    /// Computes the IEEE CRC-32 of <paramref name="data"/>. The check value for
    /// the ASCII string <c>"123456789"</c> is <c>0xCBF43926</c>.
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ LookupTable[(crc ^ value) & 0xFFu];
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildLookupTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256u; index++)
        {
            var entry = index;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1u) != 0u ? (entry >> 1) ^ Polynomial : entry >> 1;
            }

            table[index] = entry;
        }

        return table;
    }
}
