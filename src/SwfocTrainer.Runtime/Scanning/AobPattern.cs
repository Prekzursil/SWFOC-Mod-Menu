namespace SwfocTrainer.Runtime.Scanning;

internal sealed class AobPattern
{
    public byte?[] Bytes { get; }

    private AobPattern(byte?[] bytes)
    {
        Bytes = bytes;
    }

    public static AobPattern Parse(string pattern)
    {
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new byte?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var token = parts[i];
            if (token == "??" || token == "?")
            {
                bytes[i] = null;
            }
            else
            {
                bytes[i] = Convert.ToByte(token, 16);
            }
        }

        return new AobPattern(bytes);
    }
}
