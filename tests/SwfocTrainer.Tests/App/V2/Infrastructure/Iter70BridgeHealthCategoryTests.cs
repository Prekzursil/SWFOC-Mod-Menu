using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-04-28 (iter 70) — pins the bucket boundaries of
/// <see cref="BridgeActivityStats.HealthCategory"/> that drives the
/// bottom-bar bridge-health dot.
/// </summary>
public sealed class Iter70BridgeHealthCategoryTests
{
    private static BridgeActivityStats Stats(int total, int success) =>
        new(
            TotalCalls: total,
            SuccessCount: success,
            FailureCount: total - success,
            AverageDurationMs: 1.0,
            TopCommand: "X",
            TopCommandCount: 1);

    [Fact]
    public void Health_ZeroCalls_StaysHealthyByDefault()
    {
        // Empty ring buffer = nothing to judge; default to green.
        Stats(0, 0).HealthCategory.Should().Be("Healthy");
    }

    [Fact]
    public void Health_FewerThanFiveCalls_StaysHealthy()
    {
        // Floor: don't flag red on 1 unlucky failure with sample of 1.
        Stats(1, 0).HealthCategory.Should().Be("Healthy");
        Stats(4, 0).HealthCategory.Should().Be("Healthy",
            "floor of 5 calls before going non-green");
    }

    [Fact]
    public void Health_AboveFiveCalls_AllSuccess_Healthy()
    {
        Stats(5, 5).HealthCategory.Should().Be("Healthy");
        Stats(50, 50).HealthCategory.Should().Be("Healthy");
    }

    [Fact]
    public void Health_5PercentFailRate_Degraded()
    {
        // 1 fail / 20 = 5%
        Stats(20, 19).HealthCategory.Should().Be("Degraded",
            ">=5% fail rate trips the amber threshold");
    }

    [Theory]
    [InlineData(20, 19, "Degraded")] // 5% fail
    [InlineData(20, 18, "Degraded")] // 10% fail
    [InlineData(100, 86, "Degraded")] // 14% fail
    public void Health_5To15PercentFailRate_Degraded(int total, int success, string expected)
    {
        Stats(total, success).HealthCategory.Should().Be(expected);
    }

    [Theory]
    [InlineData(20, 17, "Failing")] // 15% fail
    [InlineData(20, 10, "Failing")] // 50% fail
    [InlineData(20, 0, "Failing")]  // 100% fail
    public void Health_15PercentOrMoreFailRate_Failing(int total, int success, string expected)
    {
        Stats(total, success).HealthCategory.Should().Be(expected);
    }

    [Fact]
    public void Health_Boundary_5_5Calls_NotEnoughToDegradeOnSingleFail()
    {
        // 1 fail / 5 = 20% fail rate, which is above the 15% threshold.
        // The floor was met (>= 5 calls), so this DOES go red.
        Stats(5, 4).HealthCategory.Should().Be("Failing",
            "the 5-call floor only protects from 1-of-1 noise; 1-of-5 = 20% is real");
    }
}
