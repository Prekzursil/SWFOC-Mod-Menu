using System.Buffers.Binary;
using System.Reflection;
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

    [Fact]
    public void Open_ShouldReturnParseException_WhenDistinctEntriesCollapseToSameNormalizedPath()
    {
        var payload = BuildFormat1Archive(
        [
            new FixtureEntry("Data/XML/Test.xml", "A"u8.ToArray()),
            new FixtureEntry("Data\\XML\\Test.xml", "B"u8.ToArray())
        ]);

        var result = new MegArchiveReader().Open(payload, "duplicate-normalized-paths.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("parse_exception");
        result.Diagnostics.Should().Contain(x => x.Contains("same key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Open_ReadOnlyMemoryOverload_ShouldUseMemorySource()
    {
        var payload = BuildFormat1Archive(
        [
            new FixtureEntry("Data/XML/Test.xml", "<test />"u8.ToArray())
        ]);

        var result = new MegArchiveReader().Open((ReadOnlyMemory<byte>)payload);

        result.Succeeded.Should().BeTrue(result.Message);
        result.Archive.Should().NotBeNull();
        result.Archive!.Source.Should().Be("<memory>");
        result.Archive.Format.Should().Be("format1");
    }

    [Fact]
    public void Open_ShouldFail_WhenEntryPointsToMissingNameIndex()
    {
        var payload = BuildFormat2ArchiveWithNameIndex(nameIndex: 2);

        var result = new MegArchiveReader().Open(payload, "missing-name-index.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
        result.Diagnostics.Should().Contain(x => x.Contains("missing nameIndex=2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3DataStartIsSmallerThanHeaderFootprint()
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 24u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0u);

        var result = new MegArchiveReader().Open(payload, "format3-small-datastart.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
        result.Message.Should().ContainEquivalentOf("dataStart");
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3NameTableSizeExceedsPayloadLength()
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 24u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 8u);

        var result = new MegArchiveReader().Open(payload, "format3-oversized-nametable.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
        result.Message.Should().ContainEquivalentOf("name table size");
    }

    [Fact]
    public void TryEnsureRange_ShouldRejectNegativeOffset()
    {
        object?[] args = [8, -1, 4u, string.Empty];

        var ok = (bool)InvokePrivateStatic("TryEnsureRange", args)!;

        ok.Should().BeFalse();
        args[3].Should().Be("offset -1 is outside length 8.");
    }

    private static byte[] BuildFormat1Archive(IReadOnlyList<FixtureEntry> entries)
    {
        var nameTable = BuildNameTable(entries);
        var fileTableOffset = 8 + nameTable.Length;
        var dataOffset = fileTableOffset + (entries.Count * 20);
        var totalDataBytes = entries.Sum(x => x.Bytes.Length);

        using var stream = new MemoryStream(capacity: dataOffset + totalDataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((uint)entries.Count);
        writer.Write((uint)entries.Count);
        writer.Write(nameTable);

        var cursor = dataOffset;
        for (var i = 0; i < entries.Count; i++)
        {
            writer.Write((uint)0);
            writer.Write((uint)i);
            writer.Write((uint)entries[i].Bytes.Length);
            writer.Write((uint)cursor);
            writer.Write((uint)i);
            cursor += entries[i].Bytes.Length;
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Bytes);
        }

        return stream.ToArray();
    }

    private static byte[] BuildNameTable(IReadOnlyList<FixtureEntry> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        foreach (var entry in entries)
        {
            var encoded = Encoding.ASCII.GetBytes(entry.Path);
            writer.Write((ushort)encoded.Length);
            writer.Write((ushort)0);
            writer.Write(encoded);
        }

        return stream.ToArray();
    }

    private static byte[] BuildFormat2ArchiveWithNameIndex(uint nameIndex)
    {
        var name = Encoding.ASCII.GetBytes("Data/XML/Test.xml");
        var content = "<test />"u8.ToArray();
        var nameTableLength = 4 + name.Length;
        var fileTableOffset = 20 + nameTableLength;
        var dataOffset = fileTableOffset + 20;

        using var stream = new MemoryStream(capacity: dataOffset + content.Length);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write((uint)dataOffset);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write((ushort)name.Length);
        writer.Write((ushort)0);
        writer.Write(name);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)content.Length);
        writer.Write((uint)dataOffset);
        writer.Write(nameIndex);
        writer.Write(content);

        return stream.ToArray();
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

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(MegArchiveReader).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected private static method {methodName}");
        return method!.Invoke(null, args);
    }

    private sealed record FixtureEntry(string Path, byte[] Bytes);
}
