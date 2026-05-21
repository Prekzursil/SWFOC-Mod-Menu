using System.Buffers.Binary;
using System.Text;

namespace SwfocTrainer.Savegame;

/// <summary>
/// One node in the savegame chunk tree: an 8-byte header (uint32 id + uint32
/// size) followed by a body. Bit 31 of the size field flags a container whose
/// body is itself a chunk stream; leaf chunks carry raw data that may decode
/// as a micro-chunk stream.
/// </summary>
public sealed class SaveChunk
{
    /// <summary>Bit 31 of the raw size field — set when the body is a nested chunk stream.</summary>
    public const uint SubChunkFlag = 0x80000000u;

    /// <summary>Size in bytes of the chunk header (uint32 id + uint32 size).</summary>
    public const int HeaderSize = 8;

    /// <summary>Absolute file offset of this chunk's 8-byte header.</summary>
    public required long Offset { get; init; }

    /// <summary>The 32-bit chunk id.</summary>
    public required uint Id { get; init; }

    /// <summary>The raw size field, including the bit-31 sub-chunk flag.</summary>
    public required uint RawSizeField { get; init; }

    /// <summary>Child chunks, populated when <see cref="HasSubChunks"/> is true.</summary>
    public IReadOnlyList<SaveChunk> Children { get; init; } = Array.Empty<SaveChunk>();

    /// <summary>
    /// Micro-chunks decoded from a leaf chunk body — populated only when the
    /// body decodes cleanly as a contiguous micro-chunk stream.
    /// </summary>
    public IReadOnlyList<MicroChunk> MicroChunks { get; init; } = Array.Empty<MicroChunk>();

    /// <summary>Diagnostic note; non-null on a malformed (overflowing) chunk.</summary>
    public string? Note { get; init; }

    /// <summary>True when bit 31 of the size field is set.</summary>
    public bool HasSubChunks => (RawSizeField & SubChunkFlag) != 0;

    /// <summary>Body size in bytes, with the bit-31 flag masked off.</summary>
    public long DataSize => RawSizeField & ~SubChunkFlag;

    /// <summary>Absolute file offset of this chunk's body — just past the 8-byte header.</summary>
    public long BodyOffset => Offset + HeaderSize;

    /// <summary>True when this chunk overflowed the region it was walked in.</summary>
    public bool IsOverflow => Note is not null
        && Note.Contains("OVERFLOW", StringComparison.Ordinal);

    /// <summary>The chunk id rendered as <c>0xXXXXXXXX</c>.</summary>
    public string IdHex => $"0x{Id:X8}";

    /// <summary>
    /// The chunk id decoded as four little-endian ASCII bytes, or an empty
    /// string when any byte is non-printable. Numeric ids such as 0x3E8
    /// decode to empty; FourCC ids such as <c>"NONE"</c> decode to text.
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
}
