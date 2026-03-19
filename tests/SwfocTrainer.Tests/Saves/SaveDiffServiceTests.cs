using FluentAssertions;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.Saves;

public sealed class SaveDiffServiceTests
{
    [Fact]
    public void BuildDiffPreview_ShouldReturnEmpty_WhenBuffersMatch()
    {
        var original = new byte[] { 1, 2, 3, 4 };
        var current = new byte[] { 1, 2, 3, 4 };

        var diff = SaveDiffService.BuildDiffPreview(original, current, maxEntries: 10);

        diff.Should().BeEmpty();
    }

    [Fact]
    public void BuildDiffPreview_ShouldIncludeLengthChange_WhenLengthsDiffer()
    {
        var original = new byte[] { 1, 2, 3 };
        var current = new byte[] { 1, 2, 3, 4 };

        var diff = SaveDiffService.BuildDiffPreview(original, current, maxEntries: 10);

        diff.Should().ContainSingle();
        diff[0].Should().Be("Length changed: 3 -> 4");
    }

    [Fact]
    public void BuildDiffPreview_ShouldRespectMaxEntries()
    {
        var original = Enumerable.Repeat((byte)0x00, 32).ToArray();
        var current = Enumerable.Repeat((byte)0x01, 32).ToArray();

        var diff = SaveDiffService.BuildDiffPreview(original, current, maxEntries: 5);

        diff.Should().HaveCount(5);
        diff.Last().Should().Contain("0x00000004");
    }

    [Fact]
    public void BuildDiffPreview_DefaultOverload_ShouldUseDefaultMaxEntries()
    {
        var original = Enumerable.Repeat((byte)0x00, 256).ToArray();
        var current = Enumerable.Repeat((byte)0x01, 256).ToArray();

        var diff = SaveDiffService.BuildDiffPreview(original, current);

        diff.Should().HaveCount(200);
        diff.Last().Should().Contain("0x000000C7");
    }

    [Fact]
    public void BuildDiffPreview_ShouldIncludeEntriesAndLengthChange()
    {
        var original = new byte[] { 0x10, 0x20, 0x30 };
        var current = new byte[] { 0x10, 0xAA };

        var diff = SaveDiffService.BuildDiffPreview(original, current, maxEntries: 10);

        diff.Should().HaveCount(2);
        diff[0].Should().Be("0x00000001: 20 -> AA");
        diff[1].Should().Be("Length changed: 3 -> 2");
    }
}
