using System.Text;
using FluentAssertions;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.Meg;

public sealed class MegModelCoverageTests
{
    [Fact]
    public void TryOpenEntryStream_ShouldTrimRequestedPath_AndExposeBytes()
    {
        var payload = Encoding.ASCII.GetBytes("ABCD");
        var archive = new MegArchive(
            source: "fixture.meg",
            format: "format2",
            entries:
            [
                new MegEntry(" Data\\XML\\Test.xml ", 1, 0, 4, 0)
            ],
            payload: payload,
            diagnostics: Array.Empty<string>());

        archive.TryOpenEntryStream(" Data/XML/Test.xml ", out var stream, out var error).Should().BeTrue(error);
        using var reader = new StreamReader(stream!);
        reader.ReadToEnd().Should().Be("ABCD");
    }

    [Fact]
    public void MegOpenResult_FactoryMethods_ShouldPopulateExpectedFields()
    {
        var archive = new MegArchive(
            source: "fixture.meg",
            format: "format2",
            entries: Array.Empty<MegEntry>(),
            payload: Array.Empty<byte>(),
            diagnostics: ["warn"]);

        var success = MegOpenResult.Success(archive, ["diag"]);
        var fail = MegOpenResult.Fail("bad_header", "invalid");
        var failWithDiagnostics = MegOpenResult.Fail("bad_entry", "entry invalid", ["detail"]);

        success.Succeeded.Should().BeTrue();
        success.ReasonCode.Should().Be("ok");
        success.Archive.Should().BeSameAs(archive);
        success.Diagnostics.Should().ContainSingle("diag");

        fail.Succeeded.Should().BeFalse();
        fail.Archive.Should().BeNull();
        fail.Diagnostics.Should().BeEmpty();

        failWithDiagnostics.Diagnostics.Should().ContainSingle("detail");
        failWithDiagnostics.Message.Should().Be("entry invalid");
    }
}
