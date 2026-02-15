namespace SwfocTrainer.Saves.Checksum;

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            var index = (crc ^ b) & 0xFF;
            crc = (crc >> 8) ^ Table[index];
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        const uint poly = 0xEDB88320u;
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var j = 0; j < 8; j++)
            {
                value = (value & 1) == 1 ? (value >> 1) ^ poly : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
