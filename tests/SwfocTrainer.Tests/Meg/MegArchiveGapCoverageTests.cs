using System.Reflection;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

public sealed class MegArchiveGapCoverageTests
{
    [Fact]
    public void TryOpenEntryStream_ShouldNormalizePaths_AndRejectInvalidRanges()
    {
        var payload = Encoding.ASCII.GetBytes("DATA");
        var archive = new MegArchive(
            source: "fixture.meg",
            format: "format1",
            entries:
            [
                new MegEntry("Data\\XML\\Test.xml", 0, 0, 4, 0),
                new MegEntry("Broken.bin", 0, 1, 8, 3)
            ],
            payload: payload,
            diagnostics: Array.Empty<string>());

        archive.TryReadEntryBytes("Data/XML/Test.xml", out var bytes, out var okError).Should().BeTrue(okError);
        Encoding.ASCII.GetString(bytes).Should().Be("DATA");

        archive.TryOpenEntryStream("Broken.bin", out var brokenStream, out var brokenError).Should().BeFalse();
        brokenStream.Should().BeNull();
        brokenError.Should().Contain("invalid range");

        archive.TryReadEntryBytes("Missing.bin", out var missingBytes, out var missingError).Should().BeFalse();
        missingBytes.Should().BeEmpty();
        missingError.Should().Contain("not found");
    }

    [Fact]
    public void TryParseEntryRecord_ShouldRejectUnsupportedFlags_ForFormat3Entries()
    {
        var bytes = new byte[20];
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, 0);

        var diagnostics = new List<string>();
        var header = CreateParsedHeader(supportsEntryFlags: true, dataStartOffset: 0);
        var parameters = new object?[] { bytes, header, 0, diagnostics, 0, null };

        var ok = (bool)InvokeReaderPrivateStatic("TryParseEntryRecord", parameters)!;

        ok.Should().BeFalse();
        diagnostics.Should().ContainSingle(x => x.Contains("unsupported encrypted/compressed flags", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsEntryRangeValid_ShouldRejectOutOfBoundsAndDataStartViolations()
    {
        var diagnostics = new List<string>();
        var outOfBounds = CreateParsedEntry(entryFlags: 0, crc: 0, index: 0, size: 8, start: 10, nameIndex: 0);

        var rangeOk = (bool)InvokeReaderPrivateStatic(
            "IsEntryRangeValid",
            outOfBounds,
            12,
            0u,
            diagnostics,
            0)!;

        rangeOk.Should().BeFalse();
        diagnostics.Should().ContainSingle(x => x.Contains("invalid content span", StringComparison.OrdinalIgnoreCase));

        diagnostics.Clear();
        var startsBeforeData = CreateParsedEntry(entryFlags: 0, crc: 0, index: 0, size: 2, start: 3, nameIndex: 0);
        rangeOk = (bool)InvokeReaderPrivateStatic(
            "IsEntryRangeValid",
            startsBeforeData,
            16,
            4u,
            diagnostics,
            1)!;

        rangeOk.Should().BeFalse();
        diagnostics.Should().ContainSingle(x => x.Contains("starts before header dataStart offset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseNames_ShouldRejectBoundaryOverflow_AndReportTrailingBytes()
    {
        var overflowBytes = new byte[32];
        BitConverter.GetBytes((ushort)3).CopyTo(overflowBytes, 24);
        Encoding.ASCII.GetBytes("ABC").CopyTo(overflowBytes, 28);
        var overflowHeader = CreateParsedHeader(supportsEntryFlags: false, dataStartOffset: 0, nameCount: 1, nameTableSize: 6);
        var diagnostics = new List<string>();
        var overflowArgs = new object?[] { overflowBytes, overflowHeader, 24, diagnostics };

        var overflowNames = InvokeReaderPrivateStatic("ParseNames", overflowArgs);

        overflowNames.Should().BeNull();
        diagnostics.Should().ContainSingle(x => x.Contains("spills past format3 name table boundary", StringComparison.OrdinalIgnoreCase));

        var trailingBytes = new byte[40];
        BitConverter.GetBytes((ushort)1).CopyTo(trailingBytes, 24);
        trailingBytes[28] = (byte)'Z';
        trailingBytes[29] = 0x7F;
        var trailingHeader = CreateParsedHeader(supportsEntryFlags: false, dataStartOffset: 0, nameCount: 1, nameTableSize: 8);
        diagnostics.Clear();
        var trailingArgs = new object?[] { trailingBytes, trailingHeader, 24, diagnostics };

        var trailingNames = InvokeReaderPrivateStatic("ParseNames", trailingArgs);

        trailingNames.Should().BeAssignableTo<IReadOnlyList<string>>();
        ((IReadOnlyList<string>)trailingNames!).Should().ContainSingle().Which.Should().Be("Z");
        diagnostics.Should().ContainSingle(x => x.Contains("trailing bytes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryResolveNames_ShouldFallbackToFormat2_WhenFormat3NameTableParsingFails()
    {
        var bytes = new byte[32];
        BitConverter.GetBytes(24u).CopyTo(bytes, 8);
        BitConverter.GetBytes(1u).CopyTo(bytes, 12);
        BitConverter.GetBytes(0u).CopyTo(bytes, 16);
        bytes[20] = 1;
        bytes[24] = (byte)'A';

        var diagnostics = new List<string>();
        var header = CreateParsedHeader(
            supportsEntryFlags: true,
            dataStartOffset: 24,
            nameCount: 1,
            nameTableSize: 1,
            format: "format3");
        var cursor = 24;
        var parameters = new object?[] { bytes, diagnostics, header, cursor, null };

        var ok = (bool)InvokeReaderPrivateStatic("TryResolveNames", parameters)!;

        ok.Should().BeTrue();
        parameters[2].Should().NotBeNull();
        parameters[3].Should().Be(25);
        ((IReadOnlyList<string>)parameters[4]!).Should().ContainSingle().Which.Should().Be("A");
        diagnostics.Should().Contain(x => x.Contains("format3 parse fallback succeeded as format2", StringComparison.OrdinalIgnoreCase));
    }

    private static object CreateParsedHeader(
        bool supportsEntryFlags,
        uint dataStartOffset,
        uint nameCount = 1,
        uint? nameTableSize = null,
        string format = "format-test")
    {
        var type = typeof(MegArchiveReader).GetNestedType("ParsedHeader", BindingFlags.NonPublic);
        type.Should().NotBeNull();
        var ctor = type!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(x => x.GetParameters().Length == 9);
        return ctor.Invoke(
        [
            true,
            null,
            format,
            nameCount,
            1u,
            dataStartOffset,
            nameTableSize,
            false,
            supportsEntryFlags
        ]);
    }

    private static object CreateParsedEntry(
        ushort entryFlags,
        uint crc,
        uint index,
        uint size,
        uint start,
        uint nameIndex)
    {
        var type = typeof(MegArchiveReader).GetNestedType("ParsedEntry", BindingFlags.NonPublic);
        type.Should().NotBeNull();
        var ctor = type!.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(x => x.GetParameters().Length == 6);
        return ctor.Invoke([entryFlags, crc, index, size, start, nameIndex]);
    }

    private static object? InvokeReaderPrivateStatic(string name, params object?[] args)
    {
        var method = typeof(MegArchiveReader).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull($"Expected MegArchiveReader private static method {name}");
        return method!.Invoke(null, args);
    }
}
