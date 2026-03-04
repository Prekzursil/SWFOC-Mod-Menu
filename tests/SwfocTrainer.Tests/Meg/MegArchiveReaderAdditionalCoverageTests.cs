using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

public sealed class MegArchiveReaderAdditionalCoverageTests
{
    [Fact]
    public void Open_ShouldFailForTruncatedFormat3Header()
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4);

        var result = new MegArchiveReader().Open(payload, "format3-truncated.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    [Fact]
    public void Open_ShouldFailForTruncatedFormat2Header()
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4);

        var result = new MegArchiveReader().Open(payload, "format2-truncated.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    [Fact]
    public void Open_ShouldFailWhenFormat2DataStartExceedsLength()
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 400u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 1u);

        var result = new MegArchiveReader().Open(payload, "format2-datastart.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    [Fact]
    public void Open_ShouldFailForUnreasonableFormat1Counts()
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 300000u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1u);

        var result = new MegArchiveReader().Open(payload, "format1-unreasonable.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
        result.Diagnostics.Should().Contain(x => x.Contains("Unreasonable MEG counts", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] BuildFormat3Archive(
        ushort entryFlags,
        uint headerDataStart,
        uint entryStart,
        byte[] dataBytes,
        byte[]? trailingNameTableBytes = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        var name = Encoding.ASCII.GetBytes("Data/XML/Test.xml");
        trailingNameTableBytes ??= Array.Empty<byte>();

        var nameTableSize = (uint)(4 + name.Length + trailingNameTableBytes.Length);
        var fileCount = 1u;
        var nameCount = 1u;

        writer.Write(0x8FFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write(headerDataStart);
        writer.Write(nameCount);
        writer.Write(fileCount);
        writer.Write(nameTableSize);

        writer.Write((ushort)name.Length);
        writer.Write((ushort)0);
        writer.Write(name);
        writer.Write(trailingNameTableBytes);

        writer.Write(entryFlags);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)dataBytes.Length);
        writer.Write(entryStart);
        writer.Write((ushort)0);

        var requiredPad = (int)entryStart - (int)stream.Length;
        if (requiredPad > 0)
        {
            writer.Write(new byte[requiredPad]);
        }

        writer.Write(dataBytes);

        return stream.ToArray();
    }
}