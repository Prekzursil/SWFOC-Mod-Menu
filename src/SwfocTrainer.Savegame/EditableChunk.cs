using System.Buffers.Binary;
using System.Text;

namespace SwfocTrainer.Savegame;

/// <summary>
/// A mutable node in a <see cref="SavegameDocument"/> chunk tree — the editor
/// layer (spec iter-289) over the read-only <see cref="SaveChunk"/> the parser
/// produces. Each chunk captures its original on-disk bytes; a clean chunk
/// re-serialises byte-for-byte, and only a chunk on the path of an actual edit
/// is rebuilt — with its size field recomputed and propagated up to every
/// containing parent.
/// </summary>
public sealed class EditableChunk
{
    private readonly byte[] _originalChunkBytes;
    private readonly List<EditableChunk> _children;
    private readonly List<MicroChunk>? _microChunks;
    private readonly byte[]? _rawLeafBody;
    private bool _localDirty;

    private EditableChunk(
        uint id,
        bool hasSubChunks,
        byte[] originalChunkBytes,
        List<EditableChunk> children,
        List<MicroChunk>? microChunks,
        byte[]? rawLeafBody)
    {
        Id = id;
        HasSubChunks = hasSubChunks;
        _originalChunkBytes = originalChunkBytes;
        _children = children;
        _microChunks = microChunks;
        _rawLeafBody = rawLeafBody;
    }

    /// <summary>The 32-bit chunk id.</summary>
    public uint Id { get; }

    /// <summary>True when this chunk's body is a nested chunk stream (bit 31 of the size field).</summary>
    public bool HasSubChunks { get; }

    /// <summary>True when this chunk is a leaf whose body decoded as an editable micro-chunk stream.</summary>
    public bool IsMicroLeaf => _microChunks is not null;

    /// <summary>True when this chunk is a leaf carrying raw, non-micro-chunk data.</summary>
    public bool IsRawLeaf => _rawLeafBody is not null;

    /// <summary>The chunk id rendered as <c>0xXXXXXXXX</c>.</summary>
    public string IdHex => $"0x{Id:X8}";

    /// <summary>
    /// The chunk id decoded as four little-endian ASCII bytes, or an empty
    /// string when any byte is non-printable — mirrors <see cref="SaveChunk.IdFourCc"/>.
    /// </summary>
    public string IdFourCc
    {
        get
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Id);
            foreach (var value in bytes)
            {
                if (value is < 0x20 or > 0x7E)
                {
                    return string.Empty;
                }
            }

            return Encoding.ASCII.GetString(bytes);
        }
    }

    /// <summary>Child chunks; non-empty only when <see cref="HasSubChunks"/> is true.</summary>
    public IReadOnlyList<EditableChunk> Children => _children;

    /// <summary>
    /// The editable micro-chunk list. Empty unless this chunk is a micro-chunk
    /// leaf (<see cref="IsMicroLeaf"/>); mutate it through
    /// <see cref="SetMicroChunk"/>, <see cref="SetMicroChunkInt32"/> and
    /// <see cref="DeleteMicroChunk"/>.
    /// </summary>
    public IReadOnlyList<MicroChunk> MicroChunks =>
        _microChunks ?? (IReadOnlyList<MicroChunk>)Array.Empty<MicroChunk>();

    /// <summary>
    /// True when this chunk — or any descendant — has an unsaved edit. A clean
    /// chunk re-serialises byte-for-byte from its captured original bytes; a
    /// dirty one is rebuilt from its (possibly edited) children or micro-chunks.
    /// </summary>
    public bool IsDirty => _localDirty || _children.Any(static c => c.IsDirty);

    /// <summary>
    /// Replaces the micro-chunk at <paramref name="index"/>. The replacement may
    /// carry a different payload length — <see cref="SavegameDocument.Serialize"/>
    /// recomputes the leaf size field and propagates it to every parent.
    /// </summary>
    public void SetMicroChunk(int index, MicroChunk replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var list = RequireMicroLeaf();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, list.Count);
        list[index] = replacement;
        _localDirty = true;
    }

    /// <summary>
    /// Rewrites the int32 payload of the micro-chunk at <paramref name="index"/>,
    /// keeping its existing type code. Valid only for the int32 field codes
    /// 0x01-0x04 — the common case for editing mod CRCs, faction ids and counts.
    /// </summary>
    public void SetMicroChunkInt32(int index, int value)
    {
        var list = RequireMicroLeaf();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, list.Count);
        var existing = list[index];
        if (!existing.IsInt32Field)
        {
            throw new InvalidOperationException(
                $"micro-chunk {index} has type 0x{existing.TypeCode:X2}; SetMicroChunkInt32 is " +
                "valid only for the int32 field codes 0x01-0x04.");
        }

        var data = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(data, value);
        list[index] = MicroChunk.Create(existing.TypeCode, data);
        _localDirty = true;
    }

    /// <summary>Removes the micro-chunk at <paramref name="index"/> from the leaf body.</summary>
    public void DeleteMicroChunk(int index)
    {
        var list = RequireMicroLeaf();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, list.Count);
        list.RemoveAt(index);
        _localDirty = true;
    }

    /// <summary>Builds an editable chunk tree from a parsed <see cref="SaveChunk"/>.</summary>
    internal static EditableChunk FromParsed(SaveChunk chunk, byte[] sourceBuffer)
    {
        var originalChunkBytes = sourceBuffer
            .AsSpan((int)chunk.Offset, SaveChunk.HeaderSize + (int)chunk.DataSize)
            .ToArray();

        if (chunk.HasSubChunks)
        {
            var children = chunk.Children
                .Select(c => FromParsed(c, sourceBuffer))
                .ToList();
            return new EditableChunk(
                chunk.Id, hasSubChunks: true, originalChunkBytes, children,
                microChunks: null, rawLeafBody: null);
        }

        if (chunk.MicroChunks.Count > 0)
        {
            return new EditableChunk(
                chunk.Id, hasSubChunks: false, originalChunkBytes,
                children: new List<EditableChunk>(),
                microChunks: chunk.MicroChunks.ToList(),
                rawLeafBody: null);
        }

        var rawBody = sourceBuffer
            .AsSpan((int)chunk.BodyOffset, (int)chunk.DataSize)
            .ToArray();
        return new EditableChunk(
            chunk.Id, hasSubChunks: false, originalChunkBytes,
            children: new List<EditableChunk>(),
            microChunks: null,
            rawLeafBody: rawBody);
    }

    /// <summary>
    /// Writes this chunk to <paramref name="output"/>. A clean chunk emits its
    /// captured original bytes verbatim; a dirty one rebuilds its body and
    /// re-stamps the 8-byte header with the recomputed size field.
    /// </summary>
    internal void WriteTo(Stream output)
    {
        if (!IsDirty)
        {
            output.Write(_originalChunkBytes);
            return;
        }

        var body = BuildBody();
        Span<byte> header = stackalloc byte[SaveChunk.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Id);
        var sizeField = (uint)body.Length;
        if (HasSubChunks)
        {
            sizeField |= SaveChunk.SubChunkFlag;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], sizeField);
        output.Write(header);
        output.Write(body);
    }

    private byte[] BuildBody()
    {
        if (HasSubChunks)
        {
            using var stream = new MemoryStream();
            foreach (var child in _children)
            {
                child.WriteTo(stream);
            }

            return stream.ToArray();
        }

        if (_microChunks is not null)
        {
            using var stream = new MemoryStream();
            foreach (var micro in _microChunks)
            {
                stream.Write(micro.Serialize());
            }

            return stream.ToArray();
        }

        return _rawLeafBody!;
    }

    private List<MicroChunk> RequireMicroLeaf()
    {
        if (_microChunks is null)
        {
            throw new InvalidOperationException(
                $"chunk {IdHex} is not a micro-chunk leaf; it carries " +
                $"{(HasSubChunks ? "nested sub-chunks" : "raw data")} and has no editable " +
                "micro-chunk stream.");
        }

        return _microChunks;
    }
}
