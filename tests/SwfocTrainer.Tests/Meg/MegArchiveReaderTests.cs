using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

public sealed class MegArchiveReaderTests
{
    [Fact]
    public void Open_ShouldParseFormat1Archive_AndReadEntryBytes()
    {
        var payload = BuildFormat1Archive([
            new MegFixtureEntry("Data/XML/GameConstants.xml", "<constants />"u8.ToArray()),
            new MegFixtureEntry("Data/Scripts/Test.lua", "return true"u8.ToArray())
        ]);
        var reader = new MegArchiveReader();

        var result = reader.Open(payload, "fixture-format1.meg");

        result.Succeeded.Should().BeTrue(result.Message);
        result.Archive.Should().NotBeNull();
        result.Archive!.Format.Should().Be("format1");
        result.Archive.Entries.Select(x => x.Path).Should().ContainInOrder(
            "Data/XML/GameConstants.xml",
            "Data/Scripts/Test.lua");
        result.Archive.TryReadEntryBytes("Data/Scripts/Test.lua", out var bytes, out var error).Should().BeTrue(error);
        Encoding.ASCII.GetString(bytes).Should().Be("return true");
    }

    [Fact]
    public void Open_ShouldParseFormat2Archive_AndExposeDeterministicEntries()
    {
        var payload = BuildFormat2Archive([
            new MegFixtureEntry("Data/XML/Story/Campaign.xml", "<story />"u8.ToArray()),
            new MegFixtureEntry("Data/Scripts/Story.lua", "function Init() end"u8.ToArray())
        ]);
        var reader = new MegArchiveReader();

        var result = reader.Open(payload, "fixture-format2.meg");

        result.Succeeded.Should().BeTrue(result.Message);
        result.Archive.Should().NotBeNull();
        result.Archive!.Format.Should().Be("format2");
        result.Archive.Entries.Should().HaveCount(2);
        result.Archive.Entries[0].Index.Should().Be(0);
        result.Archive.Entries[1].Index.Should().Be(1);
    }

    [Fact]
    public void Open_ShouldFailForCorruptHeader_WithExplicitReason()
    {
        var reader = new MegArchiveReader();
        var payload = new byte[] { 0x01, 0x02, 0x03 };

        var result = reader.Open(payload, "corrupt.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("invalid_header");
    }

    [Fact]
    public void Open_ShouldFailClosed_WhenFormat3HeaderIndicatesEncryption()
    {
        var payload = BuildFormat3HeaderOnly(encrypted: true);
        var reader = new MegArchiveReader();

        var result = reader.Open(payload, "encrypted.meg");

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("encrypted_archive_unsupported");
    }

    [Fact]
    public void Open_ShouldParseFixtureArchives_WithStableEntryHashes()
    {
        var root = TestPaths.FindRepoRoot();
        var format1Path = Path.Combine(root, "tools", "fixtures", "meg", "sample_format1.meg");
        var format2Path = Path.Combine(root, "tools", "fixtures", "meg", "sample_format2.meg");
        var reader = new MegArchiveReader();

        var format1 = reader.Open(format1Path);
        var format2 = reader.Open(format2Path);

        format1.Succeeded.Should().BeTrue(format1.Message);
        format2.Succeeded.Should().BeTrue(format2.Message);
        format1.Archive!.Entries.Should().HaveCount(2);
        format2.Archive!.Entries.Should().HaveCount(2);
        format1.Archive.TryReadEntryBytes("Data/XML/GameConstants.xml", out var format1Bytes, out _).Should().BeTrue();
        format2.Archive.TryReadEntryBytes("Data/XML/Story/Campaign.xml", out var format2Bytes, out _).Should().BeTrue();
        ComputeSha256(format1Bytes).Should().Be("47f8f58e61fb002964928a33fdff1ebe6acc3d57114ee1c017635ccb90b72cff");
        ComputeSha256(format2Bytes).Should().Be("03ce98b378b17b35c13e59a605f25bba0dce1cd575a6bec12f70c948f0f73ac8");
    }

    private static byte[] BuildFormat1Archive(IReadOnlyList<MegFixtureEntry> entries)
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
            writer.Write((uint)0); // crc
            writer.Write((uint)i); // index
            writer.Write((uint)entries[i].Bytes.Length);
            writer.Write((uint)cursor);
            writer.Write((uint)i); // name index
            cursor += entries[i].Bytes.Length;
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Bytes);
        }

        return stream.ToArray();
    }

    private static byte[] BuildFormat2Archive(IReadOnlyList<MegFixtureEntry> entries)
    {
        var nameTable = BuildNameTable(entries);
        var fileTableOffset = 20 + nameTable.Length;
        var dataOffset = fileTableOffset + (entries.Count * 20);
        var totalDataBytes = entries.Sum(x => x.Bytes.Length);

        using var stream = new MemoryStream(capacity: dataOffset + totalDataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write((uint)dataOffset);
        writer.Write((uint)entries.Count);
        writer.Write((uint)entries.Count);
        writer.Write(nameTable);

        var cursor = dataOffset;
        for (var i = 0; i < entries.Count; i++)
        {
            writer.Write((uint)0); // crc
            writer.Write((uint)i); // index
            writer.Write((uint)entries[i].Bytes.Length);
            writer.Write((uint)cursor);
            writer.Write((uint)i); // name index
            cursor += entries[i].Bytes.Length;
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Bytes);
        }

        return stream.ToArray();
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

    private static byte[] BuildNameTable(IReadOnlyList<MegFixtureEntry> entries)
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

    private static string ComputeSha256(byte[] payload)
    {
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record MegFixtureEntry(string Path, byte[] Bytes);
}
