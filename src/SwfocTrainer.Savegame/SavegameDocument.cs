namespace SwfocTrainer.Savegame;

/// <summary>
/// An editable in-memory view of a SWFOC <c>.PetroglyphFoC64Save</c> file — the
/// engine behind the savegame editor tab (spec iter-289). Load a save, walk and
/// mutate its <see cref="EditableChunk"/> tree, then <see cref="Serialize"/> it
/// back to bytes that load in-game.
///
/// <para>
/// Two guarantees back the editor's "edit one micro-chunk, write it back, it
/// loads cleanly" contract:
/// </para>
/// <list type="bullet">
///   <item>An <b>unedited</b> document round-trips byte-for-byte to its input.</item>
///   <item>An <b>edited</b> document recomputes the size field of every chunk on
///   the path from the edited micro-chunk up to the file root, leaving every
///   untouched chunk byte-identical.</item>
/// </list>
///
/// <para>
/// <see cref="Load"/> opens only structurally sound saves; run
/// <see cref="SavegameFixer"/> first to recover a corrupt one.
/// </para>
/// </summary>
public sealed class SavegameDocument
{
    private readonly byte[] _originalBuffer;
    private readonly byte[] _prefix;
    private readonly byte[] _trailing;
    private readonly List<EditableChunk> _chunks;

    private SavegameDocument(
        byte[] originalBuffer,
        byte[] prefix,
        byte[] trailing,
        SavegameHeader header,
        List<EditableChunk> chunks)
    {
        _originalBuffer = originalBuffer;
        _prefix = prefix;
        _trailing = trailing;
        Header = header;
        _chunks = chunks;
    }

    /// <summary>The parsed RGMH header.</summary>
    public SavegameHeader Header { get; }

    /// <summary>The editable top-level chunk tree.</summary>
    public IReadOnlyList<EditableChunk> Chunks => _chunks;

    /// <summary>True when any chunk anywhere in the tree has an unsaved edit.</summary>
    public bool IsDirty => _chunks.Any(static c => c.IsDirty);

    /// <summary>
    /// Loads a savegame buffer into an editable document. Throws
    /// <see cref="SavegameFormatException"/> on a malformed buffer — a bad
    /// header or a chunk that overflows its region; run
    /// <see cref="SavegameFixer"/> first to recover such a save.
    /// </summary>
    public static SavegameDocument Load(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var (header, chunks) = SavegameParser.Parse(buffer);
        SavegameParser.Validate(chunks);

        var prefix = buffer.AsSpan(0, (int)header.ChunkStreamOffset).ToArray();
        var editable = chunks.Select(c => EditableChunk.FromParsed(c, buffer)).ToList();

        // Bytes past the last walked chunk — a sub-8-byte tail, or chunks beyond
        // the parser's per-level cap — are kept verbatim so an edited document
        // still re-emits them untouched.
        var consumed = header.ChunkStreamOffset;
        foreach (var chunk in chunks)
        {
            consumed += SaveChunk.HeaderSize + chunk.DataSize;
        }

        var trailing = consumed < buffer.Length
            ? buffer.AsSpan((int)consumed).ToArray()
            : Array.Empty<byte>();

        return new SavegameDocument(buffer, prefix, trailing, header, editable);
    }

    /// <summary>Reads a savegame file from disk and loads it as an editable document.</summary>
    public static SavegameDocument LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Load(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Serialises the document — with every edit applied — back to a savegame
    /// buffer. An unedited document returns a byte-for-byte copy of its input.
    /// </summary>
    public byte[] Serialize()
    {
        if (!IsDirty)
        {
            return (byte[])_originalBuffer.Clone();
        }

        using var stream = new MemoryStream(_originalBuffer.Length);
        stream.Write(_prefix);
        foreach (var chunk in _chunks)
        {
            chunk.WriteTo(stream);
        }

        stream.Write(_trailing);
        return stream.ToArray();
    }

    /// <summary>Serialises the document and writes it to <paramref name="path"/>.</summary>
    public void SaveFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        File.WriteAllBytes(path, Serialize());
    }
}
