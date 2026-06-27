using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27 (iter 65) — pins the duration-bucket logic on
/// <see cref="BridgeActivityEntry.DurationCategory"/> that powers the
/// activity DataGrid color-coding.
/// </summary>
public sealed class Iter65DurationCategoryTests
{
    private static BridgeActivityEntry NewEntry(long durationMs) =>
        new(
            Timestamp: DateTimeOffset.UtcNow,
            LuaCommand: "return SWFOC_GetVersion()",
            Succeeded: true,
            ResponseOrError: "1.0",
            DurationMs: durationMs);

    [Theory]
    [InlineData(0, "Fast")]
    [InlineData(1, "Fast")]
    [InlineData(49, "Fast")]
    public void Duration_Under50ms_BucketsAsFast(long ms, string expected)
    {
        NewEntry(ms).DurationCategory.Should().Be(expected);
    }

    [Theory]
    [InlineData(50, "Normal")]
    [InlineData(100, "Normal")]
    [InlineData(199, "Normal")]
    public void Duration_50To199ms_BucketsAsNormal(long ms, string expected)
    {
        NewEntry(ms).DurationCategory.Should().Be(expected);
    }

    [Theory]
    [InlineData(200, "Slow")]
    [InlineData(300, "Slow")]
    [InlineData(499, "Slow")]
    public void Duration_200To499ms_BucketsAsSlow(long ms, string expected)
    {
        NewEntry(ms).DurationCategory.Should().Be(expected);
    }

    [Theory]
    [InlineData(500, "VerySlow")]
    [InlineData(1000, "VerySlow")]
    [InlineData(60_000, "VerySlow")]
    public void Duration_500msAndAbove_BucketsAsVerySlow(long ms, string expected)
    {
        NewEntry(ms).DurationCategory.Should().Be(expected);
    }

    [Fact]
    public void DurationCategory_OnlyFourBucketsExist()
    {
        // Forward-orphan check on the bucket set — the converter has 4
        // expected categories. If a 5th is added, the XAML resource lookup
        // needs to grow too.
        var allCategories = new[]
        {
            NewEntry(0).DurationCategory,
            NewEntry(50).DurationCategory,
            NewEntry(200).DurationCategory,
            NewEntry(500).DurationCategory,
        };
        allCategories.Should().BeEquivalentTo(
            new[] { "Fast", "Normal", "Slow", "VerySlow" });
    }
}
