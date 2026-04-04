using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

/// <summary>
/// Wave 8 coverage: remaining branches in MegArchiveReader and MegArchive —
/// null/whitespace/missing paths, payload too small, all format variants,
/// truncated headers, unreasonable counts, name table truncation,
/// entry validation, MegArchive stream/read operations, MegOpenResult guards.
/// </summary>
public sealed class MegWave8CoverageTests
{
    #region MegArchiveReader — null/whitespace/missing path

    [Fact]
    public void Open_ShouldFail_WhenPathIsNull()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open((string)null!);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_path");
    }

    [Fact]
    public void Open_ShouldFail_WhenPathIsEmpty()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(string.Empty);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_path");
    }

    [Fact]
    public void Open_ShouldFail_WhenPathIsWhitespace()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open("   ");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_path");
    }

    [Fact]
    public void Open_ShouldFail_WhenFileDoesNotExist()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(Path.Join(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.meg"));
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("missing_file");
    }

    #endregion

    #region MegArchiveReader — payload too small

    [Fact]
    public void Open_ShouldFail_WhenPayloadIsTooSmall()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(new byte[4], "tiny.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    [Fact]
    public void Open_ShouldFail_WhenPayloadIsExactly7Bytes()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(new byte[7], "tiny7.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    #endregion

    #region MegArchiveReader — format1

    [Fact]
    public void Open_ShouldSucceed_ForFormat1_WithSingleEntry()
    {
        var nameBytes = Encoding.ASCII.GetBytes("Test.xml");
        var contentBytes = "<f1/>"u8.ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // format1: first two words are nameCount and fileCount
        writer.Write(1u); // nameCount
        writer.Write(1u); // fileCount

        // name table: 2-byte length + 2-byte pad + name bytes
        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write(nameBytes);

        var dataStart = (int)stream.Position + 20;
        // file entry (no flags): crc, index, size, start, nameIndex (uint)
        writer.Write(0u); // crc
        writer.Write(0u); // index
        writer.Write((uint)contentBytes.Length); // size
        writer.Write((uint)dataStart); // start
        writer.Write(0u); // nameIndex

        writer.Write(contentBytes);

        var payload = stream.ToArray();
        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format1.meg");

        result.Succeeded.Should().BeTrue();
        result.Archive!.Format.Should().Be("format1");
        result.Archive.Entries.Should().ContainSingle();
        result.Archive.Entries[0].Path.Should().Be("Test.xml");
    }

    [Fact]
    public void Open_ShouldFail_ForFormat1_WhenCountsExceedMaxReasonable()
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 999999u); // nameCount way too high
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 999999u); // fileCount way too high

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_counts_f1.meg");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region MegArchiveReader — format2

    [Fact]
    public void Open_ShouldSucceed_ForFormat2_WithZeroFiles()
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 20u); // dataStart
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0u); // nameCount
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u); // fileCount

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format2_zero.meg");

        result.Succeeded.Should().BeTrue();
        result.Archive!.Format.Should().Be("format2");
        result.Archive.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Open_ShouldFail_ForFormat2_WhenTruncated()
    {
        var payload = new byte[12]; // format2 needs at least 20
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 20u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format2_trunc.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_ForFormat2_WhenCountsAreUnreasonable()
    {
        var payload = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 20u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 999999u); // unreasonable
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 999999u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format2_unreasonable.meg");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region MegArchiveReader — format3

    [Fact]
    public void Open_ShouldFail_ForFormat3Encrypted()
    {
        var payload = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 64u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 0u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format3_enc.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("encrypted_archive_unsupported");
    }

    [Fact]
    public void Open_ShouldFail_ForFormat3_WhenHeaderIsTruncated()
    {
        var payload = new byte[16]; // needs 24 for format3
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 24u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 0u);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format3_trunc.meg");
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_ShouldFail_ForFormat3_WhenNameTableSizeExceedsPayload()
    {
        var payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0x8FFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 64u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20, 4), 200u); // nameTableSize > payload

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "format3_big_nametable.meg");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region MegArchiveReader — name table truncation

    [Fact]
    public void Open_ShouldFail_WhenNameHeaderIsTruncated()
    {
        // Format1 with 1 name but payload cut short before name length bytes
        // Format1 header: nameCount(4) + fileCount(4) = 8 bytes, then only 2 more (need 4 for name header)
        var payload = new byte[10]; // header(8) + 2 (need 4 for name header)
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 1u); // nameCount = 1
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0u); // fileCount = 0

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "name_trunc.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_name_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenNameBytesTruncated()
    {
        // Format2 with 1 name, length claims 50 bytes but only 2 available
        var payload = new byte[26];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), 0xFFFFFFFFu);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 0x3F7D70A4u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), 100u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 0u);
        // Name header: length = 50 (too large for payload)
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(20, 2), 50);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "name_bytes_trunc.meg");
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region MegArchiveReader — entry validation (nameIndex exceeds, content span overflow, starts before dataStart)

    [Fact]
    public void Open_ShouldFail_WhenEntryNameIndexExceedsNameCount()
    {
        var nameBytes = Encoding.ASCII.GetBytes("X");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        var dataStartPos = stream.Position;
        writer.Write(0u); // placeholder dataStart
        writer.Write(1u); // nameCount
        writer.Write(1u); // fileCount

        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write(nameBytes);

        var fileTableStart = (int)stream.Position;
        var dataStart = fileTableStart + 20;
        // entry with nameIndex = 5 (exceeds nameCount of 1)
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write((uint)dataStart);
        writer.Write(5u); // invalid nameIndex

        var payload = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan((int)dataStartPos, 4), (uint)dataStart);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "bad_nameindex.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenEntryContentSpanOverflows()
    {
        var nameBytes = Encoding.ASCII.GetBytes("Y");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        var dataStartPos = stream.Position;
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(1u);

        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write(nameBytes);

        var fileTableStart = (int)stream.Position;
        var dataStart = fileTableStart + 20;
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(999999u); // size way too large
        writer.Write((uint)dataStart);
        writer.Write(0u); // nameIndex = 0

        var payload = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan((int)dataStartPos, 4), (uint)dataStart);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "overflow_entry.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    [Fact]
    public void Open_ShouldFail_WhenEntryStartsBeforeDataStart()
    {
        var nameBytes = Encoding.ASCII.GetBytes("Z");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        var dataStartPos = stream.Position;
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(1u);

        writer.Write((ushort)nameBytes.Length);
        writer.Write((ushort)0);
        writer.Write(nameBytes);

        var fileTableStart = (int)stream.Position;
        var dataStart = fileTableStart + 20 + 4; // put data start beyond entry data
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u); // size = 1
        writer.Write(2u); // start = 2 (before dataStart)
        writer.Write(0u);

        writer.Write(new byte[8]); // padding

        var payload = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan((int)dataStartPos, 4), (uint)dataStart);

        var reader = new MegArchiveReader();
        var result = reader.Open(payload, "before_datastart.meg");
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_file_table");
    }

    #endregion

    #region MegArchive — TryOpenEntryStream / TryReadEntryBytes

    [Fact]
    public void TryOpenEntryStream_ShouldReturnTrue_ForValidEntry()
    {
        var archive = BuildArchiveWithSingleEntry("Data/Unit.xml", "<unit/>"u8.ToArray());
        var found = archive.TryOpenEntryStream("Data/Unit.xml", out var stream, out var error);

        found.Should().BeTrue();
        error.Should().BeNull();
        stream.Should().NotBeNull();

        using (stream!)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            Encoding.UTF8.GetString(ms.ToArray()).Should().Be("<unit/>");
        }
    }

    [Fact]
    public void TryOpenEntryStream_ShouldReturnFalse_ForMissingEntry()
    {
        var archive = BuildArchiveWithSingleEntry("Data/Unit.xml", "<unit/>"u8.ToArray());
        var found = archive.TryOpenEntryStream("Data/DoesNotExist.xml", out var stream, out var error);

        found.Should().BeFalse();
        stream.Should().BeNull();
        error.Should().Contain("not found");
    }

    [Fact]
    public void TryOpenEntryStream_ShouldReturnFalse_ForInvalidRange()
    {
        // Create archive with entry that has invalid startOffset/size
        var entry = new MegEntry("Data/Bad.xml", 0, 0, SizeBytes: -1, StartOffset: 0);
        var archive = new MegArchive("test", "format1", new[] { entry }, new byte[10], Array.Empty<string>());

        var found = archive.TryOpenEntryStream("Data/Bad.xml", out var stream, out var error);
        found.Should().BeFalse();
        stream.Should().BeNull();
        error.Should().Contain("invalid range");
    }

    [Fact]
    public void TryOpenEntryStream_ShouldThrow_WhenEntryPathIsNull()
    {
        var archive = BuildArchiveWithSingleEntry("test.dat", new byte[] { 1 });
        var act = () => archive.TryOpenEntryStream(null!, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryReadEntryBytes_ShouldReturnTrue_ForValidEntry()
    {
        var content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var archive = BuildArchiveWithSingleEntry("Data/Hex.bin", content);

        var found = archive.TryReadEntryBytes("Data/Hex.bin", out var bytes, out var error);
        found.Should().BeTrue();
        bytes.Should().Equal(content);
        error.Should().BeNull();
    }

    [Fact]
    public void TryReadEntryBytes_ShouldReturnFalse_ForMissingEntry()
    {
        var archive = BuildArchiveWithSingleEntry("Data/A.bin", new byte[] { 1 });

        var found = archive.TryReadEntryBytes("Data/Missing.bin", out var bytes, out var error);
        found.Should().BeFalse();
        bytes.Should().BeEmpty();
        error.Should().Contain("not found");
    }

    [Fact]
    public void TryReadEntryBytes_ShouldThrow_WhenEntryPathIsNull()
    {
        var archive = BuildArchiveWithSingleEntry("test.dat", new byte[] { 1 });
        var act = () => archive.TryReadEntryBytes(null!, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region MegArchive — path normalization

    [Fact]
    public void MegArchive_ShouldNormalizePaths_WithBackslashToForwardSlash()
    {
        var archive = BuildArchiveWithSingleEntry(@"Data\Units\Unit.xml", new byte[] { 1 });
        var found = archive.TryOpenEntryStream("Data/Units/Unit.xml", out var stream, out _);
        found.Should().BeTrue();
        stream?.Dispose();
    }

    #endregion

    #region MegOpenResult — null guards

    [Fact]
    public void MegOpenResult_Success_ShouldThrow_WhenArchiveIsNull()
    {
        var act = () => MegOpenResult.Success(null!, Array.Empty<string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MegOpenResult_Success_ShouldThrow_WhenDiagnosticsIsNull()
    {
        var archive = BuildArchiveWithSingleEntry("t.bin", new byte[] { 1 });
        var act = () => MegOpenResult.Success(archive, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MegOpenResult_Fail_ShouldThrow_WhenReasonCodeIsNull()
    {
        var act = () => MegOpenResult.Fail(null!, "message");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MegOpenResult_Fail_ShouldThrow_WhenMessageIsNull()
    {
        var act = () => MegOpenResult.Fail("code", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MegOpenResult_FailWithDiagnostics_ShouldThrow_WhenDiagnosticsIsNull()
    {
        var act = () => MegOpenResult.Fail("code", "msg", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region MegEntry — properties

    [Fact]
    public void MegEntry_ShouldExposeProperties()
    {
        var entry = new MegEntry("Data/Test.xml", 12345L, 0, 100, 50, 0);
        entry.Path.Should().Be("Data/Test.xml");
        entry.Crc32.Should().Be(12345L);
        entry.Index.Should().Be(0);
        entry.SizeBytes.Should().Be(100);
        entry.StartOffset.Should().Be(50);
        entry.Flags.Should().Be(0);
    }

    #endregion

    #region MegArchiveReader — Memory overload

    [Fact]
    public void Open_MemoryOverload_ShouldFail_WhenPayloadTooSmall()
    {
        var reader = new MegArchiveReader();
        var result = reader.Open(new ReadOnlyMemory<byte>(new byte[4]));
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Open_MemoryOverload_ShouldThrow_WhenSourceNameIsNull()
    {
        var reader = new MegArchiveReader();
        var act = () => reader.Open(new ReadOnlyMemory<byte>(new byte[8]), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Helpers

    private static MegArchive BuildArchiveWithSingleEntry(string path, byte[] content)
    {
        var offset = 0;
        var entry = new MegEntry(path, 0, 0, content.Length, offset);
        return new MegArchive("test.meg", "format1", new[] { entry }, content, Array.Empty<string>());
    }

    #endregion
}
