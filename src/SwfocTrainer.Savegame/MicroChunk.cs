using System.Buffers.Binary;

namespace SwfocTrainer.Savegame;

/// <summary>
/// A sub-unit inside a leaf chunk body. Wire format: a 1-byte type code, a
/// 1-byte length, then that many data bytes (so a single micro-chunk caps at
/// 255 data bytes). The codec is empirically confirmed against the 0x3E8
/// mod-context chunk hex dump captured in iter-288.
/// </summary>
public sealed class MicroChunk
{
    /// <summary>Size in bytes of the micro-chunk header (type byte + length byte).</summary>
    public const int HeaderSize = 2;

    /// <summary>Maximum data length — the length field is a single byte.</summary>
    public const int MaxDataLength = byte.MaxValue;

    /// <summary>Type code 0x00 — raw serialized data.</summary>
    public const byte TypeRaw = 0x00;

    /// <summary>Type codes 0x01-0x04 — individual int32 fields.</summary>
    public const byte TypeInt32First = 0x01;

    /// <summary>Type codes 0x01-0x04 — individual int32 fields.</summary>
    public const byte TypeInt32Last = 0x04;

    /// <summary>Type code 0x05 — variable-length string / blob.</summary>
    public const byte TypeStringBlob = 0x05;

    /// <summary>Type code 0x06 — bulk integer array.</summary>
    public const byte TypeIntArray = 0x06;

    /// <summary>The 1-byte type code (0x00-0x06 for known types).</summary>
    public required byte TypeCode { get; init; }

    /// <summary>The 1-byte declared length of <see cref="Data"/>.</summary>
    public required byte Length { get; init; }

    /// <summary>The micro-chunk payload bytes.</summary>
    public required byte[] Data { get; init; }

    /// <summary>Total on-disk size — the 2-byte header plus the data.</summary>
    public int TotalSize => HeaderSize + Data.Length;

    /// <summary>True when the type code is one of the int32 field codes 0x01-0x04.</summary>
    public bool IsInt32Field => TypeCode is >= TypeInt32First and <= TypeInt32Last;

    /// <summary>Builds a micro-chunk from a type code and payload, deriving the length field.</summary>
    public static MicroChunk Create(byte typeCode, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaxDataLength)
        {
            throw new ArgumentException(
                $"micro-chunk data is {data.Length} bytes; the length field caps at {MaxDataLength}.",
                nameof(data));
        }

        return new MicroChunk { TypeCode = typeCode, Length = (byte)data.Length, Data = data };
    }

    /// <summary>Serializes the micro-chunk back to its on-disk 2-byte-header form.</summary>
    public byte[] Serialize()
    {
        var buffer = new byte[TotalSize];
        buffer[0] = TypeCode;
        buffer[1] = Length;
        Data.CopyTo(buffer, HeaderSize);
        return buffer;
    }

    /// <summary>Interprets the payload as a little-endian int32 — valid for type codes 0x01-0x04.</summary>
    public int AsInt32()
    {
        if (Data.Length < sizeof(int))
        {
            throw new InvalidOperationException(
                $"micro-chunk type 0x{TypeCode:X2} has {Data.Length} data byte(s); " +
                $"need {sizeof(int)} for an int32.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(Data);
    }

    /// <summary>
    /// Decodes a leaf chunk body as a contiguous micro-chunk stream. Throws
    /// <see cref="SavegameFormatException"/> when a declared length runs past
    /// the body or leaves trailing bytes unconsumed.
    /// </summary>
    public static IReadOnlyList<MicroChunk> ReadStream(ReadOnlySpan<byte> body)
    {
        var chunks = new List<MicroChunk>();
        var pos = 0;
        while (pos + HeaderSize <= body.Length)
        {
            var typeCode = body[pos];
            var length = body[pos + 1];
            var dataStart = pos + HeaderSize;
            var dataEnd = dataStart + length;
            if (dataEnd > body.Length)
            {
                throw new SavegameFormatException(
                    $"micro-chunk at offset {pos} declares {length} data byte(s) but only " +
                    $"{body.Length - dataStart} remain in the body.");
            }

            chunks.Add(new MicroChunk
            {
                TypeCode = typeCode,
                Length = length,
                Data = body.Slice(dataStart, length).ToArray(),
            });
            pos = dataEnd;
        }

        if (pos != body.Length)
        {
            throw new SavegameFormatException(
                $"micro-chunk stream left {body.Length - pos} trailing byte(s) unconsumed.");
        }

        return chunks;
    }

    /// <summary>
    /// Best-effort micro-chunk decode. Returns true only when the body decodes
    /// into one or more micro-chunks that consume it exactly; otherwise yields
    /// an empty list. Used to populate <see cref="SaveChunk.MicroChunks"/>.
    /// </summary>
    public static bool TryReadStream(ReadOnlySpan<byte> body, out IReadOnlyList<MicroChunk> chunks)
    {
        if (body.Length < HeaderSize)
        {
            chunks = Array.Empty<MicroChunk>();
            return false;
        }

        try
        {
            chunks = ReadStream(body);
            return chunks.Count > 0;
        }
        catch (SavegameFormatException)
        {
            chunks = Array.Empty<MicroChunk>();
            return false;
        }
    }
}
