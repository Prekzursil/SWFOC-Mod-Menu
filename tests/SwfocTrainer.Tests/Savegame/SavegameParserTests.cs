using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Savegame;
using Xunit;

namespace SwfocTrainer.Tests.Savegame;

/// <summary>
/// Pin tests for the <see cref="SavegameParser"/> C# port of the proven Python
/// reference parser (<c>tools/savegame_parser/parser.py</c>, spec iter-287).
/// Covers RGMH header parsing, BMP-thumbnail skipping, recursive chunk
/// enumeration, overflow detection, and the 7-code micro-chunk codec —
/// the last validated against the real 0x3E8 mod-context hex dump from iter-288.
/// </summary>
[Trait("Category", "Savegame")]
public sealed class SavegameParserTests
{
    private const uint TestStructSize = SavegameParser.HeaderStructSize;
    private const string ExpectedLabel = "Forces of Corruption game";

    /// <summary>
    /// The vanilla 0x3E8 mod-context chunk body, lifted byte-for-byte from the
    /// iter-288 hex dump. Decodes into 7 micro-chunks (types 5,4,3,1,2,0,6);
    /// the type-1 micro-chunk carries mod CRC32 0x8AF30372.
    /// </summary>
    private static readonly byte[] Vanilla3E8Body =
    {
        0x05, 0x01, 0x00,
        0x04, 0x04, 0x01, 0x00, 0x00, 0x00,
        0x03, 0x04, 0x01, 0x00, 0x00, 0x00,
        0x01, 0x04, 0x72, 0x03, 0xF3, 0x8A,
        0x02, 0x04, 0xE0, 0xD7, 0x7E, 0x40,
        0x00, 0x04, 0x61, 0x00, 0x00, 0x00,
        0x06, 0x04, 0xE4, 0x9C, 0x0C, 0x00,
    };

    [Fact]
    public void ParseHeader_ReadsRgmhHeaderFields()
    {
        var save = BuildHeader();

        var header = SavegameParser.ParseHeader(save);

        header.Magic.Should().Be("RGMH");
        header.Version.Should().Be(1u);
        header.StructSize.Should().Be(TestStructSize);
        header.Label.Should().Be(ExpectedLabel);
        header.UuidHex.Should().HaveLength(32);
        header.HasBmpThumbnail.Should().BeFalse();
        header.ChunkStreamOffset.Should().Be(TestStructSize);
    }

    [Fact]
    public void ParseHeader_SkipsBmpThumbnail()
    {
        const int bmpSize = 96;
        var save = Concat(BuildHeader(), BuildBmpBlock(bmpSize));

        var header = SavegameParser.ParseHeader(save);

        header.HasBmpThumbnail.Should().BeTrue();
        header.ChunkStreamOffset.Should().Be(TestStructSize + bmpSize);
    }

    [Fact]
    public void ParseHeader_RejectsBadMagic()
    {
        var save = BuildHeader();
        save[0] = (byte)'X';

        var act = () => SavegameParser.ParseHeader(save);

        act.Should().Throw<SavegameFormatException>().WithMessage("*magic*");
    }

    [Fact]
    public void ParseHeader_RejectsTooShortBuffer()
    {
        var act = () => SavegameParser.ParseHeader(new byte[16]);

        act.Should().Throw<SavegameFormatException>().WithMessage("*shorter*");
    }

    [Fact]
    public void Parse_EnumeratesFlatTopLevelChunks()
    {
        var save = Concat(
            BuildHeader(),
            ChunkHeader(0x3E8, 4), new byte[] { 1, 2, 3, 4 },
            ChunkHeader(0x3E9, 2), new byte[] { 9, 9 },
            ChunkHeader(0x3EC, 0));

        var (_, chunks) = SavegameParser.Parse(save);

        chunks.Should().HaveCount(3);
        chunks[0].Id.Should().Be(0x3E8u);
        chunks[0].DataSize.Should().Be(4);
        chunks[0].HasSubChunks.Should().BeFalse();
        chunks[1].Id.Should().Be(0x3E9u);
        chunks[2].Id.Should().Be(0x3ECu);
        chunks[2].DataSize.Should().Be(0);
    }

    [Fact]
    public void Parse_RecursesIntoSubChunkContainers()
    {
        var childA = Concat(ChunkHeader(0x111, 1), new byte[] { 0xAA });
        var childB = ChunkHeader(0x222, 0);
        var parentBody = Concat(childA, childB);
        var save = Concat(
            BuildHeader(),
            ChunkHeader(0x3E9, (uint)parentBody.Length | SaveChunk.SubChunkFlag),
            parentBody);

        var (_, chunks) = SavegameParser.Parse(save);

        chunks.Should().ContainSingle();
        chunks[0].HasSubChunks.Should().BeTrue();
        chunks[0].Children.Should().HaveCount(2);
        chunks[0].Children[0].Id.Should().Be(0x111u);
        chunks[0].Children[1].Id.Should().Be(0x222u);
    }

    [Fact]
    public void Parse_FlagsOverflowingChunk()
    {
        // Declares a 0x100-byte body but only 4 bytes follow the header.
        var save = Concat(
            BuildHeader(),
            ChunkHeader(0x999, 0x100), new byte[] { 1, 2, 3, 4 });

        var (_, chunks) = SavegameParser.Parse(save);

        chunks.Should().ContainSingle();
        chunks[0].IsOverflow.Should().BeTrue();
        chunks[0].Note.Should().Contain("OVERFLOW");
    }

    [Fact]
    public void Parse_DecodesVanilla3E8MicroChunkStream()
    {
        var save = Concat(
            BuildHeader(),
            ChunkHeader(0x3E8, (uint)Vanilla3E8Body.Length),
            Vanilla3E8Body);

        var (_, chunks) = SavegameParser.Parse(save);

        chunks.Should().ContainSingle();
        var micro = chunks[0].MicroChunks;
        micro.Should().HaveCount(7);
        micro.Select(m => m.TypeCode).Should().Equal(0x05, 0x04, 0x03, 0x01, 0x02, 0x00, 0x06);

        var modCrc = micro.Single(m => m.TypeCode == MicroChunk.TypeInt32First);
        modCrc.AsInt32().Should().Be(unchecked((int)0x8AF30372u));
    }

    [Theory]
    [InlineData(MicroChunk.TypeRaw)]
    [InlineData((byte)0x01)]
    [InlineData((byte)0x02)]
    [InlineData((byte)0x03)]
    [InlineData((byte)0x04)]
    [InlineData(MicroChunk.TypeStringBlob)]
    [InlineData(MicroChunk.TypeIntArray)]
    public void MicroChunk_RoundTripsEveryKnownTypeCode(byte typeCode)
    {
        var data = DataForType(typeCode);

        var micro = MicroChunk.Create(typeCode, data);
        var serialized = micro.Serialize();

        serialized[0].Should().Be(typeCode);
        serialized[1].Should().Be((byte)data.Length);

        var decoded = MicroChunk.ReadStream(serialized);
        decoded.Should().ContainSingle();
        decoded[0].TypeCode.Should().Be(typeCode);
        decoded[0].Length.Should().Be((byte)data.Length);
        decoded[0].Data.Should().Equal(data);

        if (decoded[0].IsInt32Field)
        {
            decoded[0].AsInt32().Should().Be(unchecked((int)0xEFBEADDEu));
        }
    }

    [Fact]
    public void MicroChunk_Create_RejectsDataLongerThan255Bytes()
    {
        var act = () => MicroChunk.Create(MicroChunk.TypeRaw, new byte[256]);

        act.Should().Throw<ArgumentException>().WithMessage("*255*");
    }

    [Fact]
    public void MicroChunk_ReadStream_RejectsTrailingBytes()
    {
        // A valid type-0 micro-chunk of 1 data byte, then a stray trailing byte.
        var act = () => MicroChunk.ReadStream(new byte[] { 0x00, 0x01, 0xFF, 0x42 });

        act.Should().Throw<SavegameFormatException>().WithMessage("*trailing*");
    }

    [Fact]
    public void SaveChunk_IdFourCc_DecodesPrintableFourCcAndBlanksNumericIds()
    {
        var fourCc = new SaveChunk { Offset = 0, Id = 0x454E4F4Eu, RawSizeField = 0 };
        var numeric = new SaveChunk { Offset = 0, Id = 0x3E8u, RawSizeField = 0 };

        fourCc.IdFourCc.Should().Be("NONE");
        numeric.IdFourCc.Should().BeEmpty();
        numeric.IdHex.Should().Be("0x000003E8");
    }

    [Fact]
    public void Diagnose_SummarizesChunkInventory()
    {
        var save = Concat(
            BuildHeader(),
            ChunkHeader(0x3E8, 0),
            ChunkHeader(0x3E9, 0));

        var report = SavegameParser.Diagnose(save);

        report.Parsed.Should().BeTrue();
        report.Error.Should().BeNull();
        report.TopChunkCount.Should().Be(2);
        report.TotalChunkCount.Should().Be(2);
        report.HasOverflow.Should().BeFalse();
        report.UniqueChunkIds.Should().Equal("0x000003E8", "0x000003E9");
    }

    [Fact]
    public void Diagnose_CapturesErrorWithoutThrowing()
    {
        var report = SavegameParser.Diagnose(new byte[32]);

        report.Parsed.Should().BeFalse();
        report.Error.Should().Contain("shorter");
        report.Header.Should().BeNull();
    }

    [Fact]
    public void Validate_ThrowsOnOverflowChunk()
    {
        var save = Concat(
            BuildHeader(),
            ChunkHeader(0x999, 0x100), new byte[] { 1, 2, 3, 4 });
        var (_, chunks) = SavegameParser.Parse(save);

        var act = () => SavegameParser.Validate(chunks);

        act.Should().Throw<SavegameFormatException>().WithMessage("*malformed*");
    }

    private static byte[] DataForType(byte typeCode) => typeCode switch
    {
        >= MicroChunk.TypeInt32First and <= MicroChunk.TypeInt32Last
            => new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
        MicroChunk.TypeStringBlob => Encoding.Unicode.GetBytes("mod"),
        MicroChunk.TypeIntArray => new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 },
        _ => new byte[] { 0x11, 0x22, 0x33 },
    };

    private static byte[] BuildHeader(uint version = 1)
    {
        var header = new byte[TestStructSize];
        "RGMH"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), TestStructSize);
        for (var i = 0; i < 16; i++)
        {
            header[24 + i] = (byte)(i + 1);
        }

        Encoding.Unicode.GetBytes(ExpectedLabel).CopyTo(header, 40);
        return header;
    }

    private static byte[] BuildBmpBlock(int totalSize)
    {
        var block = new byte[totalSize];
        "BM"u8.CopyTo(block);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(2), (uint)totalSize);
        return block;
    }

    private static byte[] ChunkHeader(uint id, uint rawSizeField)
    {
        var header = new byte[SaveChunk.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, id);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), rawSizeField);
        return header;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var pos = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, pos);
            pos += part.Length;
        }

        return result;
    }
}
