namespace SwfocTrainer.Meg;

public sealed class MegArchive
{
    private readonly byte[] _payload;
    private readonly Dictionary<string, MegEntry> _entriesByPath;

    public MegArchive(
        string source,
        string format,
        IReadOnlyList<MegEntry> entries,
        byte[] payload,
        IReadOnlyList<string> diagnostics)
    {
        Source = source;
        Format = format;
        Entries = entries;
        Diagnostics = diagnostics;
        _payload = payload;
        _entriesByPath = entries.ToDictionary(
            x => NormalizePath(x.Path),
            x => x,
            StringComparer.OrdinalIgnoreCase);
    }

    public string Source { get; }

    public string Format { get; }

    public IReadOnlyList<MegEntry> Entries { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public bool TryOpenEntryStream(string entryPath, out Stream? stream, out string? error)
    {
        stream = null;
        error = null;
        if (!_entriesByPath.TryGetValue(NormalizePath(entryPath), out var entry))
        {
            error = $"Entry '{entryPath}' was not found in MEG archive.";
            return false;
        }

        if (entry.StartOffset < 0 || entry.SizeBytes < 0 || entry.StartOffset + entry.SizeBytes > _payload.Length)
        {
            error = $"Entry '{entry.Path}' has invalid range start={entry.StartOffset} size={entry.SizeBytes}.";
            return false;
        }

        stream = new MemoryStream(_payload, entry.StartOffset, entry.SizeBytes, writable: false, publiclyVisible: false);
        return true;
    }

    public bool TryReadEntryBytes(string entryPath, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;
        if (!TryOpenEntryStream(entryPath, out var stream, out error) || stream is null)
        {
            return false;
        }

        using (stream)
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            bytes = memory.ToArray();
        }

        return true;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim();
}
