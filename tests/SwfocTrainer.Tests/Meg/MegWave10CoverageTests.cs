using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

public sealed class MegWave10CoverageTests
{
    // ── Open: empty/whitespace path (line 15-17) ──
    [Fact]
    public void Open_EmptyPath_ShouldFail()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(string.Empty);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_path");
    }

    // ── Open(ReadOnlyMemory): too small (line 44-47) ──
    [Fact]
    public void Open_TinyPayload_ShouldFail()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(new byte[4]));
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    // ── Format1 header: simple 1-name, 1-file archive ──
    [Fact]
    public void Open_Format1_ValidArchive_ShouldSucceed()
    {
        var bytes = BuildFormat1Archive("DATA\\TEST.XML", new byte[] { 0xCA, 0xFE });
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes));
        result.Succeeded.Should().BeTrue();
        result.Archive.Should().NotBeNull();
        result.Archive!.Entries.Should().ContainSingle();
        result.Archive.Entries[0].Path.Should().Be("DATA\\TEST.XML");
    }

    // ── Format2 header: valid archive ──
    [Fact]
    public void Open_Format2_ValidArchive_ShouldSucceed()
    {
        var bytes = BuildFormat2Archive("DATA\\UNITS.XML", new byte[] { 0x01, 0x02 });
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes));
        result.Succeeded.Should().BeTrue();
        result.Archive.Should().NotBeNull();
        result.Archive!.Format.Should().Be("format2");
    }

    // ── Format2 header: truncated (line 214-217) ──
    [Fact]
    public void Open_Format2_Truncated_ShouldFail()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 0x3F7D70A4);
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes));
        result.Succeeded.Should().BeFalse();
    }

    // ── Format3 header: encrypted (line 75-78) ──
    [Fact]
    public void Open_Format3_Encrypted_ShouldFailUnsupported()
    {
        var bytes = new byte[200];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 0x3F7D70A4);
        // dataStart
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 100);
        // nameCount = 1, fileCount = 1
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 1);
        // nameTableSize
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), 20);
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes));
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("encrypted_archive_unsupported");
    }

    // ── Format3 header: truncated (line 186-189) ──
    [Fact]
    public void Open_Format3_Truncated_ShouldFail()
    {
        var bytes = new byte[20]; // need 24 minimum for format3
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0x8FFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 0x3F7D70A4);
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes));
        result.Succeeded.Should().BeFalse();
    }

    // ── Format1 header: unreasonable counts (line 320-323) ──
    [Fact]
    public void Open_UnreasonableCounts_ShouldFail()
    {
        var bytes = new byte[100];
        // format1 style: nameCount = 999999, fileCount = 999999
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 999999);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 999999);
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes));
        result.Succeeded.Should().BeFalse();
    }

    // ── Name table: truncated name header (line 341-344) ──
    [Fact]
    public void Open_Format1_TruncatedNameHeader_ShouldFail()
    {
        // format1: nameCount=1, fileCount=1, then no name data
        var bytes = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 1);
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes));
        result.Succeeded.Should().BeFalse();
    }

    // ── Entry: name index out of range (line 389-392) ──
    [Fact]
    public void Open_Format1_BadNameIndex_ShouldFail()
    {
        // Build a format1 archive but set entry nameIndex to point beyond names array
        var name = "TEST.XML";
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var dataPayload = new byte[] { 0x01, 0x02 };

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        // header: 1 name, 1 file
        bw.Write((uint)1);
        bw.Write((uint)1);
        // name table: 1 name
        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0); // padding
        bw.Write(nameBytes);
        // entry record: set nameIndex to 99 (bad)
        var entryStart = (int)ms.Position + 20;
        bw.Write((uint)0); // crc
        bw.Write((uint)0); // index
        bw.Write((uint)dataPayload.Length); // size
        bw.Write((uint)(entryStart + 20)); // start (just make it valid range)
        bw.Write((uint)99); // nameIndex (INVALID - exceeds names count)

        // append data
        bw.Write(dataPayload);
        bw.Flush();

        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(ms.ToArray()));
        result.Succeeded.Should().BeFalse();
    }

    // ── Open with FormatException path (line 60-64) ──
    [Fact]
    public void Open_Memory_WithSourceName_ShouldIncludeInResult()
    {
        var bytes = BuildFormat1Archive("FILE.DAT", new byte[] { 0xFF });
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(bytes), "test-source");
        result.Succeeded.Should().BeTrue();
        result.Archive!.Source.Should().Be("test-source");
    }

    // ── Helper: build a minimal Format1 MEG archive ──
    private static byte[] BuildFormat1Archive(string fileName, byte[] data)
    {
        var nameBytes = Encoding.ASCII.GetBytes(fileName);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // header: 1 name, 1 file
        bw.Write((uint)1);
        bw.Write((uint)1);

        // name table entry: 2-byte length + 2-byte padding + name bytes
        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0);
        bw.Write(nameBytes);

        // compute data start offset
        var dataStart = (int)ms.Position + 20; // entry record is 20 bytes

        // file entry record (format1: no flags)
        bw.Write((uint)0);              // crc32
        bw.Write((uint)0);              // index
        bw.Write((uint)data.Length);     // size
        bw.Write((uint)dataStart);       // start offset
        bw.Write((uint)0);              // nameIndex

        // data payload
        bw.Write(data);
        bw.Flush();

        return ms.ToArray();
    }

    // ── Helper: build a minimal Format2 MEG archive ──
    private static byte[] BuildFormat2Archive(string fileName, byte[] data)
    {
        var nameBytes = Encoding.ASCII.GetBytes(fileName);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // format2 magic
        bw.Write(0xFFFFFFFF);
        bw.Write(0x3F7D70A4);

        // placeholder for dataStart (will patch)
        var dataStartPos = ms.Position;
        bw.Write((uint)0); // dataStart placeholder

        // counts
        bw.Write((uint)1); // nameCount
        bw.Write((uint)1); // fileCount

        // name table
        bw.Write((ushort)nameBytes.Length);
        bw.Write((ushort)0);
        bw.Write(nameBytes);

        // compute data start: after entry record (20 bytes)
        var dataStart = (uint)(ms.Position + 20);

        // file entry record (format2: no flags)
        bw.Write((uint)0);              // crc32
        bw.Write((uint)0);              // index
        bw.Write((uint)data.Length);     // size
        bw.Write(dataStart);             // start offset
        bw.Write((uint)0);              // nameIndex

        // data payload
        bw.Write(data);
        bw.Flush();

        // patch dataStart
        var result = ms.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan((int)dataStartPos, 4), dataStart);

        return result;
    }
}
