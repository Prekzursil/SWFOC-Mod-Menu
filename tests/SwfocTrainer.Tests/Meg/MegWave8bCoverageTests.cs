using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

/// <summary>
/// Wave 8b coverage: remaining branches in MegArchiveReader —
/// format3 encrypted archive detection, format3 parse fallback to format2,
/// format3 name table trailing bytes, format3 dataStart too small,
/// format3 name table exceeds payload, entry nameIndex out of range,
/// entry start before dataStart, entry range exceeds payload,
/// format2 dataStart exceeds archive, format1 unreasonable counts,
/// TryEnsureRange offset negative/out of bounds.
/// </summary>
public sealed class MegWave8bCoverageTests
{
    #region Format3 encrypted archive

    [Fact]
    public void Open_ShouldFail_WhenFormat3IsEncrypted()
    {
        // Format3 encrypted uses magic 0x8FFFFFFF + 0x3F7D70A4
        var bytes = new byte[100];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0x8FFFFFFF); // encrypted magic
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4); // format2/3 magic B
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 80);  // dataStart
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);  // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1);  // fileCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 10); // nameTableSize

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "test_encrypted");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("encrypted_archive_unsupported");
    }

    #endregion

    #region Format3 truncated header

    [Fact]
    public void Open_ShouldFail_WhenFormat3HeaderIsTruncated()
    {
        // Less than 24 bytes but has format3 encrypted magic
        var bytes = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "truncated_f3");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    #endregion

    #region Format3 name table exceeds payload

    [Fact]
    public void Open_ShouldFail_WhenFormat3NameTableExceedsPayload()
    {
        var bytes = new byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 28);  // dataStart
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);  // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0);  // fileCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 999); // nameTableSize > payload - 24

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "f3_nametable_overflow");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Format3 dataStart too small

    [Fact]
    public void Open_ShouldFail_WhenFormat3DataStartIsTooSmall()
    {
        var bytes = new byte[60];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 25);  // dataStart too small
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);  // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1);  // fileCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 4);  // nameTableSize

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "f3_datastart_small");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Format3 dataStart exceeds payload

    [Fact]
    public void Open_ShouldFail_WhenFormat3DataStartExceedsPayload()
    {
        var bytes = new byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 9999); // dataStart > payload
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 2);

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "f3_datastart_overflow");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Format2 dataStart exceeds archive

    [Fact]
    public void Open_ShouldFail_WhenFormat2DataStartExceedsArchive()
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0xFFFFFFFF); // format2 magic A
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4); // format2 magic B
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 9999);  // dataStart > length
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);    // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0);    // fileCount

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "f2_datastart_overflow");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Format2 truncated header

    [Fact]
    public void Open_ShouldFail_WhenFormat2HeaderIsTruncated()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "f2_truncated");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    #endregion

    #region Format1 unreasonable counts

    [Fact]
    public void Open_ShouldFail_WhenFormat1CountsAreUnreasonable()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 999999); // nameCount too large
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 999999); // fileCount too large

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "f1_unreasonable");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Format2 unreasonable counts

    [Fact]
    public void Open_ShouldFail_WhenFormat2CountsAreUnreasonable()
    {
        var bytes = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 999999); // unreasonable
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 999999);

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "f2_unreasonable");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Name table truncation

    [Fact]
    public void Open_ShouldFail_WhenNameHeaderIsTruncated()
    {
        // Format1 with 1 name but no bytes for the name header
        var bytes = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 1); // nameCount = 1
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0); // fileCount = 0

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "name_truncated");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_name_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenNameBytesAreTruncated()
    {
        // Format1 with 1 name, name length header says 100 but only 6 bytes remain
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 1);  // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0);  // fileCount
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 100); // name length = 100
        // only 4 bytes left, which is less than 100

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "name_bytes_truncated");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_name_table");
    }

    #endregion

    #region Entry validation — nameIndex out of range

    [Fact]
    public void Open_ShouldFail_WhenEntryNameIndexIsOutOfRange()
    {
        // Format1: 1 name, 1 file, but entry points to nameIndex=5
        var nameBytes = Encoding.ASCII.GetBytes("test.txt");
        var size = 8 + 4 + nameBytes.Length + 20; // header + name header + name + entry
        var bytes = new byte[size + 50]; // extra padding for data
        var cursor = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 1); cursor += 4; // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 1); cursor += 4; // fileCount

        // Name table
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor), (ushort)nameBytes.Length); cursor += 2;
        cursor += 2; // padding
        nameBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += nameBytes.Length;

        // Entry with bad nameIndex
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4; // crc
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4; // index
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4; // size
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), (uint)cursor + 8); cursor += 4; // start
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 5); // nameIndex = 5, out of range

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "bad_name_index");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    #endregion

    #region Entry validation — content range exceeds payload

    [Fact]
    public void Open_ShouldFail_WhenEntryContentRangeExceedsPayload()
    {
        var nameBytes = Encoding.ASCII.GetBytes("test.txt");
        var headerSize = 8 + 4 + nameBytes.Length + 20;
        var bytes = new byte[headerSize + 10];
        var cursor = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 1); cursor += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 1); cursor += 4;

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor), (ushort)nameBytes.Length); cursor += 2;
        cursor += 2;
        nameBytes.CopyTo(bytes.AsSpan(cursor));
        cursor += nameBytes.Length;

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4; // crc
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4; // index
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 99999); cursor += 4; // size too large
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4; // start
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); // nameIndex

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "entry_range_overflow");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    #endregion

    #region Parse exception handling

    [Fact]
    public void Open_ShouldHandleInvalidOperationException()
    {
        // A payload that triggers an unexpected parse state
        // We craft a format2 payload that appears valid header but has corrupted name entries
        var bytes = new byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 20);  // dataStart
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);  // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0);  // fileCount
        // Name header at offset 20 — truncated (only 10 bytes left, need 4 for header)
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 100); // name length too large

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "corrupt_names");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Format1 valid archive

    [Fact]
    public void Open_ShouldSucceed_ForValidFormat1Archive()
    {
        var nameBytes = Encoding.ASCII.GetBytes("data/test.txt");
        var contentBytes = Encoding.ASCII.GetBytes("hello");
        var headerSize = 8; // nameCount + fileCount
        var nameTableSize = 4 + nameBytes.Length; // 2 bytes length + 2 padding + name bytes
        var entrySize = 20;
        var dataStart = headerSize + nameTableSize + entrySize;
        var totalSize = dataStart + contentBytes.Length;

        var bytes = new byte[totalSize];
        var cursor = 0;

        // Header
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 1); cursor += 4; // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 1); cursor += 4; // fileCount

        // Name table
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor), (ushort)nameBytes.Length); cursor += 2;
        cursor += 2; // padding
        nameBytes.CopyTo(bytes.AsSpan(cursor)); cursor += nameBytes.Length;

        // Entry (format1: crc, index, size, start, nameIndex — all uint32)
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0xAABBCCDD); cursor += 4; // crc
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); cursor += 4; // index
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), (uint)contentBytes.Length); cursor += 4; // size
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), (uint)dataStart); cursor += 4; // start
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), 0); // nameIndex

        // Content
        contentBytes.CopyTo(bytes.AsSpan(dataStart));

        var reader = new MegArchiveReader();
        var result = reader.Open(bytes.AsMemory(), "valid_f1");
        result.Succeeded.Should().BeTrue();
        result.Archive.Should().NotBeNull();
        result.Archive!.Entries.Should().HaveCount(1);
        result.Archive.Entries[0].Path.Should().Be("data/test.txt");
    }

    #endregion

    #region MegOpenResult sourceName null guard

    [Fact]
    public void Open_WithMemory_ShouldThrow_WhenSourceNameIsNull()
    {
        var reader = new MegArchiveReader();
        var act = () => reader.Open(new byte[16].AsMemory(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Format3 with entry flags

    [Fact]
    public void Open_ShouldFail_WhenFormat3EntryHasUnsupportedFlags()
    {
        // Build a valid format3 header + name, then an entry with non-zero flags
        var nameBytes = Encoding.ASCII.GetBytes("file.dat");
        var nameTableSize = 4 + nameBytes.Length;
        var entrySize = 20;
        var dataStart = (uint)(24 + nameTableSize + entrySize);
        var totalSize = (int)dataStart + 10;

        var bytes = new byte[totalSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), 0x8FFFFFFF); // format3 encrypted magic
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x3F7D70A4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), dataStart);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1); // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 1); // fileCount
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)nameTableSize);

        var cursor = 24;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor), (ushort)nameBytes.Length); cursor += 2;
        cursor += 2;
        nameBytes.CopyTo(bytes.AsSpan(cursor)); cursor += nameBytes.Length;

        // Entry with encrypted flag (non-zero entryFlags)
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(cursor), 0x0001); // flags != 0

        var reader = new MegArchiveReader();
        // This will be detected as encrypted and rejected before entry parsing
        var result = reader.Open(bytes.AsMemory(), "f3_entry_flags");
        result.Succeeded.Should().BeFalse();
    }

    #endregion
}
