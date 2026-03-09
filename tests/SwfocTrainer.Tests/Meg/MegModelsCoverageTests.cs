using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

public sealed class MegModelsCoverageTests
{
    [Fact]
    public void MegOpenResultSuccess_ShouldProjectArchiveAndDiagnostics()
    {
        var archive = new MegArchive(
            source: "fixture.meg",
            format: "format1",
            entries: [new MegEntry("Data/XML/Test.xml", 1u, 0, 4, 0)],
            payload: "DATA"u8.ToArray(),
            diagnostics: ["loaded"]);

        var result = MegOpenResult.Success(archive, ["loaded"]);

        result.Succeeded.Should().BeTrue();
        result.Archive.Should().BeSameAs(archive);
        result.ReasonCode.Should().Be("ok");
        result.Message.Should().Be("MEG archive parsed successfully.");
        result.Diagnostics.Should().ContainSingle().Which.Should().Be("loaded");
    }

    [Fact]
    public void MegOpenResultFailOverloads_ShouldPreserveFailureShape()
    {
        var withoutDiagnostics = MegOpenResult.Fail("missing_file", "not found");
        var withDiagnostics = MegOpenResult.Fail("invalid_file_table", "bad table", ["detail"]);

        withoutDiagnostics.Succeeded.Should().BeFalse();
        withoutDiagnostics.Archive.Should().BeNull();
        withoutDiagnostics.ReasonCode.Should().Be("missing_file");
        withoutDiagnostics.Diagnostics.Should().BeEmpty();

        withDiagnostics.Succeeded.Should().BeFalse();
        withDiagnostics.ReasonCode.Should().Be("invalid_file_table");
        withDiagnostics.Message.Should().Be("bad table");
        withDiagnostics.Diagnostics.Should().ContainSingle().Which.Should().Be("detail");
    }

    [Fact]
    public void MegEntry_Record_ShouldRetainConstructorValues()
    {
        var entry = new MegEntry(
            Path: "Data/XML/Test.xml",
            Crc32: 123u,
            Index: 2,
            SizeBytes: 16,
            StartOffset: 8,
            Flags: 7);

        entry.Path.Should().Be("Data/XML/Test.xml");
        entry.Crc32.Should().Be(123u);
        entry.Index.Should().Be(2);
        entry.SizeBytes.Should().Be(16);
        entry.StartOffset.Should().Be(8);
        entry.Flags.Should().Be(7);
    }
}
