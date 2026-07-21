using System.Buffers.Binary;
using System.Text;

namespace SwfocTrainer.Savegame;

/// <summary>
/// Reads a SWFOC <c>.PetroglyphFoC64Save</c> file into a chunk tree. This is a
/// C# port of the proven Python reference parser
/// (<c>tools/savegame_parser/parser.py</c>, iter-287): parse the RGMH header,
/// skip the BMP thumbnail screenshot, then walk the chunk stream recursing on
/// bit 31 of each chunk's size field. Leaf chunk bodies are decoded as a
/// micro-chunk stream when they cleanly support it.
/// </summary>
public static class SavegameParser
{
    /// <summary>The 4-byte RGMH file magic.</summary>
    public static ReadOnlySpan<byte> HeaderMagic => "RGMH"u8;

    /// <summary>The 2-byte BMP magic of the thumbnail screenshot.</summary>
    public static ReadOnlySpan<byte> BmpMagic => "BM"u8;

    /// <summary>Minimum header struct size; every observed save uses exactly this value.</summary>
    public const int HeaderStructSize = 0x2028;

    /// <summary>Safety cap on chunk-tree recursion depth.</summary>
    public const int MaxChunkDepth = 8;

    /// <summary>Safety cap on chunk count per nesting level.</summary>
    public const int MaxChunksPerLevel = 4096;

    /// <summary>
    /// Upper bound on a leaf body that is speculatively decoded as a
    /// micro-chunk stream. Mod-context metadata chunks are tiny; larger leaf
    /// bodies are bulk data and are left as raw.
    /// </summary>
    public const int MaxMicroStreamBytes = 0x10000;

    private const int VersionOffset = 4;
    private const int StructSizeOffset = 8;
    private const int UuidOffset = 24;
    private const int UuidLength = 16;
    private const int LabelOffset = 40;
    private const int BmpFileHeaderSize = 14;
    private const int BmpSizeFieldOffset = 2;

    /// <summary>Parses a savegame buffer into its header and top-level chunk list.</summary>
    public static (SavegameHeader Header, IReadOnlyList<SaveChunk> Chunks) Parse(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var header = ParseHeader(buffer);
        var chunks = WalkChunks(buffer, header.ChunkStreamOffset, buffer.Length, depth: 0);
        return (header, chunks);
    }

    /// <summary>Reads a savegame file from disk and parses it.</summary>
    public static (SavegameHeader Header, IReadOnlyList<SaveChunk> Chunks) ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllBytes(path));
    }

    /// <summary>
    /// Parses just the RGMH header and resolves the chunk-stream offset.
    /// Throws <see cref="SavegameFormatException"/> on a bad magic or a buffer
    /// shorter than the header struct size.
    /// </summary>
    public static SavegameHeader ParseHeader(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length < HeaderStructSize)
        {
            throw new SavegameFormatException(
                $"file is {buffer.Length} byte(s); shorter than the RGMH header struct size " +
                $"0x{HeaderStructSize:X}.");
        }

        if (!buffer.AsSpan(0, HeaderMagic.Length).SequenceEqual(HeaderMagic))
        {
            throw new SavegameFormatException(
                $"bad magic: expected \"RGMH\", got \"{DescribeMagic(buffer)}\".");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(VersionOffset));
        var structSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(StructSizeOffset));
        var uuidHex = Convert.ToHexString(buffer, UuidOffset, UuidLength).ToLowerInvariant();
        var label = ReadHeaderLabel(buffer);
        var (chunkStreamOffset, hasBmp) = ResolveChunkStreamOffset(buffer, structSize);

        return new SavegameHeader
        {
            Magic = "RGMH",
            Version = version,
            StructSize = structSize,
            UuidHex = uuidHex,
            Label = label,
            ChunkStreamOffset = chunkStreamOffset,
            HasBmpThumbnail = hasBmp,
        };
    }

    /// <summary>
    /// Parses a buffer and returns a summary report. Format errors are captured
    /// in <see cref="SavegameReport.Error"/> rather than thrown, so callers can
    /// triage a corrupt save without exception handling.
    /// </summary>
    public static SavegameReport Diagnose(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        try
        {
            var (header, chunks) = Parse(buffer);
            var uniqueIds = new SortedSet<string>(StringComparer.Ordinal);
            var total = 0;
            var overflow = false;
            CollectStats(chunks, uniqueIds, ref total, ref overflow);
            return new SavegameReport
            {
                Parsed = true,
                Header = header,
                TopChunkCount = chunks.Count,
                TotalChunkCount = total,
                UniqueChunkIds = uniqueIds.ToArray(),
                HasOverflow = overflow,
            };
        }
        catch (SavegameFormatException ex)
        {
            return new SavegameReport { Parsed = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Throws <see cref="SavegameFormatException"/> when any chunk in the tree
    /// overflowed its region. A clean return means the structure is walkable.
    /// </summary>
    public static void Validate(IEnumerable<SaveChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        foreach (var chunk in chunks)
        {
            if (chunk.IsOverflow)
            {
                throw new SavegameFormatException(
                    $"chunk {chunk.IdHex} at offset 0x{chunk.Offset:X} is malformed: {chunk.Note}");
            }

            Validate(chunk.Children);
        }
    }

    private static IReadOnlyList<SaveChunk> WalkChunks(byte[] buffer, long start, long end, int depth)
    {
        var chunks = new List<SaveChunk>();
        var pos = start;
        var count = 0;
        while (pos + SaveChunk.HeaderSize <= end && count < MaxChunksPerLevel)
        {
            var id = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)pos));
            var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan((int)pos + 4));
            var hasSub = (rawSize & SaveChunk.SubChunkFlag) != 0;
            var dataSize = rawSize & ~SaveChunk.SubChunkFlag;
            var bodyStart = pos + SaveChunk.HeaderSize;
            var bodyEnd = bodyStart + dataSize;

            if (bodyEnd > end)
            {
                chunks.Add(new SaveChunk
                {
                    Offset = pos,
                    Id = id,
                    RawSizeField = rawSize,
                    Note = $"OVERFLOW: body end 0x{bodyEnd:X} exceeds region end 0x{end:X}; " +
                           "stopping walk of this level.",
                });
                break;
            }

            IReadOnlyList<SaveChunk> children = Array.Empty<SaveChunk>();
            IReadOnlyList<MicroChunk> microChunks = Array.Empty<MicroChunk>();
            if (hasSub && depth < MaxChunkDepth)
            {
                children = WalkChunks(buffer, bodyStart, bodyEnd, depth + 1);
            }
            else if (!hasSub && dataSize > 0 && dataSize <= MaxMicroStreamBytes)
            {
                MicroChunk.TryReadStream(
                    buffer.AsSpan((int)bodyStart, (int)dataSize), out microChunks);
            }

            chunks.Add(new SaveChunk
            {
                Offset = pos,
                Id = id,
                RawSizeField = rawSize,
                Children = children,
                MicroChunks = microChunks,
            });
            pos = bodyEnd;
            count++;
        }

        return chunks;
    }

    private static void CollectStats(
        IReadOnlyList<SaveChunk> chunks, SortedSet<string> ids, ref int total, ref bool overflow)
    {
        foreach (var chunk in chunks)
        {
            total++;
            ids.Add(chunk.IdHex);
            if (chunk.IsOverflow)
            {
                overflow = true;
            }

            if (chunk.Children.Count > 0)
            {
                CollectStats(chunk.Children, ids, ref total, ref overflow);
            }
        }
    }

    private static (long Offset, bool HasBmp) ResolveChunkStreamOffset(byte[] buffer, uint structSize)
    {
        long bmpStart = structSize;
        if (bmpStart + BmpFileHeaderSize <= buffer.Length
            && buffer.AsSpan((int)bmpStart, BmpMagic.Length).SequenceEqual(BmpMagic))
        {
            var bmpSize = BinaryPrimitives.ReadUInt32LittleEndian(
                buffer.AsSpan((int)bmpStart + BmpSizeFieldOffset));

            // The BMP must not run past the buffer; equality is valid — a
            // thumbnail can reach exactly EOF (chunk stream then empty).
            if (bmpSize >= BmpFileHeaderSize && bmpSize <= buffer.Length - bmpStart)
            {
                return (bmpStart + bmpSize, true);
            }
        }

        return (structSize, false);
    }

    private static string ReadHeaderLabel(byte[] buffer)
    {
        var end = LabelOffset;
        while (end < HeaderStructSize - 1)
        {
            if (buffer[end] == 0 && buffer[end + 1] == 0)
            {
                break;
            }

            end += 2;
        }

        return Encoding.Unicode.GetString(buffer, LabelOffset, end - LabelOffset);
    }

    private static string DescribeMagic(byte[] buffer)
    {
        Span<char> chars = stackalloc char[HeaderMagic.Length];
        for (var i = 0; i < HeaderMagic.Length; i++)
        {
            var value = buffer[i];
            chars[i] = value is >= 0x20 and <= 0x7E ? (char)value : '?';
        }

        return new string(chars);
    }
}
