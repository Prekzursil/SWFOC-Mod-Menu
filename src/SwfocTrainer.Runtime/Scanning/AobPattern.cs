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
        ArgumentNullException.ThrowIfNull(pattern);
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new byte?[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var token = parts[i];
            bytes[i] = token is "??" or "?"
                ? null
                : Convert.ToByte(token, 16);
        }

        return new AobPattern(bytes);
    }
}
