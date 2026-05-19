using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 63) — pins the
/// <see cref="CapabilitySurfaceReport.SurfaceRollup"/> aggregator that
/// powers the editor's bottom-status-bar capability indicator. Pure-data
/// tests; no simulator needed.
/// </summary>
public sealed class Iter63CapabilitySurfaceRollupTests
{
    [Fact]
    public void ComputeRollup_EmptyInput_AllZeros()
    {
        var rollup = CapabilitySurfaceReport.ComputeRollup(
            Array.Empty<(string, IReadOnlyList<CapabilityAwareAction>)>());
        rollup.TotalActions.Should().Be(0);
        rollup.LiveCount.Should().Be(0);
        rollup.LivePercent.Should().Be(0,
            "empty input yields 0% — must not divide by zero");
    }

    [Fact]
    public void ComputeRollup_SingleLiveAction_LivePercent100()
    {
        var rollup = CapabilitySurfaceReport.ComputeRollup(new[]
        {
            ("Tab", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("X", "SWFOC_GodMode"),
            }),
        });
        rollup.TotalActions.Should().Be(1);
        rollup.LiveCount.Should().Be(1);
        rollup.Phase2PendingCount.Should().Be(0);
        rollup.LivePercent.Should().Be(100);
    }

    [Fact]
    public void ComputeRollup_MixedTabs_BucketsCorrectly()
    {
        var rollup = CapabilitySurfaceReport.ComputeRollup(new[]
        {
            ("TabA", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("Live1", "SWFOC_GodMode"),
                new CapabilityAwareAction("Live2", "SWFOC_OneHitKill"),
                new CapabilityAwareAction("Pending1", "SWFOC_FreezeAI"),
            }),
            ("TabB", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("Pending2", "SWFOC_FreezeCredits"),
                new CapabilityAwareAction("LiveOnly1", "SWFOC_InspectUnit"),
                new CapabilityAwareAction("Mixed1",
                    "SWFOC_GodMode", "SWFOC_FreezeAI"),
            }),
        });
        rollup.TotalActions.Should().Be(6);
        rollup.LiveCount.Should().Be(2);
        rollup.LiveOnlyCount.Should().Be(1);
        rollup.Phase2PendingCount.Should().Be(2);
        rollup.MixedCount.Should().Be(1);
        rollup.OtherCount.Should().Be(0);
        // (LIVE + LIVE ONLY) / total = 3/6 = 50%
        rollup.LivePercent.Should().Be(50);
    }

    [Fact]
    public void SummaryLine_FormatsForBottomStatusBar()
    {
        var rollup = new CapabilitySurfaceReport.SurfaceRollup(
            TotalActions: 94,
            LiveCount: 52,
            LiveOnlyCount: 3,
            Phase2PendingCount: 37,
            MixedCount: 2,
            OtherCount: 0);
        rollup.SummaryLine.Should().Be(
            "Capability: 52 LIVE / 37 PHASE 2 / 3 LIVE ONLY · 94 actions · 59% engine-effective",
            "operator headline format — checked verbatim because the bottom status bar binds it directly");
    }

    [Fact]
    public void LivePercent_RoundsCorrectly()
    {
        // 1/3 = 33.33% → rounds to 33
        var oneOfThree = new CapabilitySurfaceReport.SurfaceRollup(3, 1, 0, 2, 0, 0);
        oneOfThree.LivePercent.Should().Be(33);

        // 2/3 = 66.67% → rounds to 67
        var twoOfThree = new CapabilitySurfaceReport.SurfaceRollup(3, 2, 0, 1, 0, 0);
        twoOfThree.LivePercent.Should().Be(67);

        // 1/2 = 50% exactly
        var half = new CapabilitySurfaceReport.SurfaceRollup(2, 1, 0, 1, 0, 0);
        half.LivePercent.Should().Be(50);
    }

    [Fact]
    public void ComputeRollup_UnknownHelperBucketsAsOther()
    {
        var rollup = CapabilitySurfaceReport.ComputeRollup(new[]
        {
            ("Tab", (IReadOnlyList<CapabilityAwareAction>)new[]
            {
                new CapabilityAwareAction("Unknown", "SWFOC_NotInCatalog_Iter63"),
            }),
        });
        rollup.OtherCount.Should().Be(1,
            "UNAVAILABLE / unknown helpers go in the Other bucket so they don't inflate the LIVE percentage");
        rollup.LivePercent.Should().Be(0);
    }
}
