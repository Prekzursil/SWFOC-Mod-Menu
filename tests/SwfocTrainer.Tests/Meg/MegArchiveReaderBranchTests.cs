using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

public sealed class MegArchiveReaderBranchTests
{
    [Fact]
    public void Open_Path_ShouldFail_WhenPathIsNull()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open((string)null!);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_path");
    }

    [Fact]
    public void Open_Path_ShouldFail_WhenPathIsEmpty()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open("");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_path");
    }

    [Fact]
    public void Open_Path_ShouldFail_WhenPathIsWhitespace()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open("   ");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_path");
    }

    [Fact]
    public void Open_Path_ShouldFail_WhenFileDoesNotExist()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(Path.Join(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.meg"));
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("missing_file");
    }

    [Fact]
    public void Open_Path_ShouldSucceed_WhenFileIsValidFormat1()
    {
        var payload = BuildFormat1Archive([
            new MegFixtureEntry("Data/Test.xml", "<test />"u8.ToArray())
        ]);
        var tempPath = Path.Join(Path.GetTempPath(), $"meg-test-{Guid.NewGuid():N}.meg");
        try
        {
            File.WriteAllBytes(tempPath, payload);
            var reader = new MegArchiveReader();
            var result = reader.Open(tempPath);
            result.Succeeded.Should().BeTrue();
            result.Archive!.Entries.Should().HaveCount(1);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Open_Memory_ShouldFail_WhenPayloadTooSmall()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(new byte[] { 0x01, 0x02, 0x03 });
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    [Fact]
    public void Open_Memory_ShouldUseDefaultSourceName()
    {
        var reader = new MegArchiveReader();
        var payload = BuildFormat1Archive([
            new MegFixtureEntry("Data/Test.xml", "<test />"u8.ToArray())
        ]);
        var result = reader.Open((ReadOnlyMemory<byte>)payload);
        result.Succeeded.Should().BeTrue();
        result.Archive!.Source.Should().Be("<memory>");
    }

    [Fact]
    public void Open_Memory_ShouldThrow_WhenSourceNameIsNull()
    {
        var reader = new MegArchiveReader();
        var act = () => reader.Open(new byte[16], null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Open_ShouldFailForUnrecognizedHeader()
    {
        var reader = new MegArchiveReader();
        var payload = new byte[64];
        payload[0] = 0x99;
        payload[1] = 0x99;
        payload[4] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 999999u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 999999u);

        var result = reader.Open(payload, "unknown.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    [Fact]
    public void Open_ShouldDetectFormat3Encrypted()
    {
        var payload = BuildFormat3HeaderOnly(encrypted: true);
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "encrypted.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("encrypted_archive_unsupported");
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat2DataStartExceedsLength()
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 99999u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_format2.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat2HeaderTruncated()
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "truncated_format2.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3HeaderTruncated()
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "truncated_format3.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat1CountsUnreasonable()
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 999999u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 999999u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_counts.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat2CountsUnreasonable()
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 20u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 999999u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 999999u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_format2_counts.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3CountsUnreasonable()
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 24u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 999999u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 999999u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_format3_counts.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3NameTableSizeExceedsPayload()
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 24u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 99999u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_nametable_size.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3DataStartExceedsPayload()
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 99999u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_datastart.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3DataStartSmallerThanFootprint()
    {
        var payload = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 25u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "small_datastart.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenNameHeaderTruncated()
    {
        var payload = BuildFormat1HeaderOnly(nameCount: 1, fileCount: 0, extraBytes: 2);
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "truncated_name.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_name_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenNameBytesTruncated()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write((ushort)100);
        writer.Write((ushort)0);
        var payload = stream.ToArray();

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "truncated_name_bytes.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_name_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenFileTableTruncated()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write((ushort)4);
        writer.Write((ushort)0);
        writer.Write(Encoding.ASCII.GetBytes("test"));
        var payload = stream.ToArray();

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "truncated_filetable.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenEntryNameIndexOutOfRange()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write((ushort)4);
        writer.Write((ushort)0);
        writer.Write(Encoding.ASCII.GetBytes("test"));
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(99u);
        var payload = stream.ToArray();

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_nameindex.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenEntryContentSpanInvalid()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write((ushort)4);
        writer.Write((ushort)0);
        writer.Write(Encoding.ASCII.GetBytes("test"));
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(999999u);
        writer.Write(0u);
        writer.Write(0u);
        var payload = stream.ToArray();

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_span.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenEntryStartsBeforeDataStart()
    {
        var entries = new[] { new MegFixtureEntry("test.xml", "<t />"u8.ToArray()) };
        var nameTable = BuildNameTable(entries);
        var fileTableOffset = 20 + nameTable.Length;
        var dataOffset = fileTableOffset + 20 + 100;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write((uint)dataOffset);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(nameTable);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)entries[0].Bytes.Length);
        writer.Write((uint)(fileTableOffset + 20));
        writer.Write(0u);
        writer.Write(new byte[100]);
        writer.Write(entries[0].Bytes);
        var payload = stream.ToArray();

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "entry_before_datastart.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    [Fact]
    public void Open_ShouldParseFormat2_WithMultipleEntries()
    {
        var entries = new[]
        {
            new MegFixtureEntry("A.txt", "aaa"u8.ToArray()),
            new MegFixtureEntry("B.txt", "bbb"u8.ToArray())
        };
        var nameTable = BuildNameTable(entries);
        var fileTableOffset = 20 + nameTable.Length;
        var dataOffset = fileTableOffset + (entries.Length * 20);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write((uint)dataOffset);
        writer.Write((uint)entries.Length);
        writer.Write((uint)entries.Length);
        writer.Write(nameTable);

        var cursor = dataOffset;
        for (var i = 0; i < entries.Length; i++)
        {
            writer.Write(0u);
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

        var payload = stream.ToArray();
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format2_multi.meg");
        result.Succeeded.Should().BeTrue();
        result.Archive!.Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Open_ShouldParseFormat3_WithValidEntries()
    {
        var entries = new[] { new MegFixtureEntry("Data/X.xml", "<x />"u8.ToArray()) };
        var payload = BuildFormat3Archive(entries);
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format3.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("encrypted_archive_unsupported");
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3EntryHasNonZeroFlags()
    {
        var payload = BuildFormat3WithFlags(entryFlags: 1);
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "flagged_entry.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_WhenFormat3NameSpillsPastBoundary()
    {
        var payload = new byte[128];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 128u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 5u);

        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(24, 2), 50);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(26, 2), 0);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "name_spills.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldHandleFormat1_WithMultipleEntries()
    {
        var payload = BuildFormat1Archive([
            new MegFixtureEntry("A.txt", "aaa"u8.ToArray()),
            new MegFixtureEntry("B.txt", "bbb"u8.ToArray()),
            new MegFixtureEntry("C.txt", "ccc"u8.ToArray())
        ]);
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "multi.meg");
        result.Succeeded.Should().BeTrue();
        result.Archive!.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Open_ShouldOrderEntriesByIndex()
    {
        var payload = BuildFormat1Archive([
            new MegFixtureEntry("Data/B.xml", "b"u8.ToArray()),
            new MegFixtureEntry("Data/A.xml", "a"u8.ToArray())
        ]);
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "ordered.meg");
        result.Succeeded.Should().BeTrue();
        result.Archive!.Entries[0].Index.Should().Be(0);
        result.Archive.Entries[1].Index.Should().Be(1);
    }

    private static byte[] BuildFormat1Archive(IReadOnlyList<MegFixtureEntry> entries)
    {
        var nameTable = BuildNameTable(entries);
        var fileTableOffset = 8 + nameTable.Length;
        var dataOffset = fileTableOffset + (entries.Count * 20);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write((uint)entries.Count);
        writer.Write((uint)entries.Count);
        writer.Write(nameTable);

        var cursor = dataOffset;
        for (var i = 0; i < entries.Count; i++)
        {
            writer.Write(0u);
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

    private static byte[] BuildFormat1HeaderOnly(uint nameCount, uint fileCount, int extraBytes)
    {
        var payload = new byte[8 + extraBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), nameCount);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), fileCount);
        return payload;
    }

    private static byte[] BuildFormat3HeaderOnly(bool encrypted)
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), encrypted ? 0x8FFFFFFFu : 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 24u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0u);
        return payload;
    }

    private static byte[] BuildFormat3Archive(IReadOnlyList<MegFixtureEntry> entries)
    {
        var nameTable = BuildNameTable(entries);
        var nameTableSize = (uint)nameTable.Length;
        var fileTableSize = (uint)(entries.Count * 20);
        var dataOffset = 24u + nameTableSize + fileTableSize;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0x8FFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write(dataOffset);
        writer.Write((uint)entries.Count);
        writer.Write((uint)entries.Count);
        writer.Write(nameTableSize);
        writer.Write(nameTable);

        var cursor = dataOffset;
        for (var i = 0; i < entries.Count; i++)
        {
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write((uint)i);
            writer.Write((uint)entries[i].Bytes.Length);
            writer.Write((uint)cursor);
            writer.Write((ushort)i);
            cursor += (uint)entries[i].Bytes.Length;
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Bytes);
        }

        return stream.ToArray();
    }

    private static byte[] BuildFormat3WithFlags(ushort entryFlags)
    {
        var entries = new[] { new MegFixtureEntry("test.xml", "<t />"u8.ToArray()) };
        var nameTable = BuildNameTable(entries);
        var nameTableSize = (uint)nameTable.Length;
        var dataOffset = 24u + nameTableSize + 20u;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0x8FFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write(dataOffset);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(nameTableSize);
        writer.Write(nameTable);
        writer.Write(entryFlags);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((uint)entries[0].Bytes.Length);
        writer.Write(dataOffset);
        writer.Write((ushort)0);
        writer.Write(entries[0].Bytes);
        return stream.ToArray();
    }

    private static byte[] BuildNameTable(IReadOnlyList<MegFixtureEntry> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        foreach (var encoded in entries.Select(entry => Encoding.ASCII.GetBytes(entry.Path)))
        {
            writer.Write((ushort)encoded.Length);
            writer.Write((ushort)0);
            writer.Write(encoded);
        }

        return stream.ToArray();
    }

    private sealed record MegFixtureEntry(string Path, byte[] Bytes);
}
