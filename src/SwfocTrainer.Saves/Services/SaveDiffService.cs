namespace SwfocTrainer.Saves.Services;

public static class SaveDiffService
{
    private const int DefaultMaxEntries = 200;

    public static IReadOnlyList<string> BuildDiffPreview(byte[] original, byte[] current, int maxEntries)
    {
        var result = new List<string>();
        var len = Math.Min(original.Length, current.Length);
        for (var i = 0; i < len; i++)
        {
            if (original[i] == current[i])
            {
                continue;
            }

            result.Add($"0x{i:X8}: {original[i]:X2} -> {current[i]:X2}");
            if (result.Count >= maxEntries)
            {
                break;
            }
        }

        if (original.Length != current.Length)
        {
            result.Add($"Length changed: {original.Length} -> {current.Length}");
        }

        return result;
    }

    public static IReadOnlyList<string> BuildDiffPreview(byte[] original, byte[] current)
    {
        return BuildDiffPreview(original, current, DefaultMaxEntries);
    }
}
