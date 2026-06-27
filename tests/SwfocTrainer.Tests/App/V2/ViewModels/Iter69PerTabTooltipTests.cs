using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 69) — pins the per-tab tooltip format on
/// <see cref="MainViewModelV2.FormatTabTooltip"/>. Pure-data test;
/// doesn't construct the full VM (which needs 17 services).
/// </summary>
public sealed class Iter69PerTabTooltipTests
{
    [Fact]
    public void Format_AllLiveTab_RendersHundredPercent()
    {
        var rollup = new CapabilitySurfaceReport.SurfaceRollup(
            TotalActions: 5, LiveCount: 5, LiveOnlyCount: 0,
            Phase2PendingCount: 0, MixedCount: 0, OtherCount: 0);
        var tooltip = MainViewModelV2.FormatTabTooltip("Battle Control", rollup);
        tooltip.Should().Be(
            "Battle Control · 5 LIVE · 0 PHASE 2 · 5 actions · 100% engine-effective");
    }

    [Fact]
    public void Format_AllPhase2Tab_RendersZeroPercent()
    {
        var rollup = new CapabilitySurfaceReport.SurfaceRollup(
            TotalActions: 3, LiveCount: 0, LiveOnlyCount: 0,
            Phase2PendingCount: 3, MixedCount: 0, OtherCount: 0);
        var tooltip = MainViewModelV2.FormatTabTooltip("Speed", rollup);
        tooltip.Should().Be(
            "Speed · 0 LIVE · 3 PHASE 2 · 3 actions · 0% engine-effective");
    }

    [Fact]
    public void Format_MixedTab_RendersComputedPercent()
    {
        var rollup = new CapabilitySurfaceReport.SurfaceRollup(
            TotalActions: 8, LiveCount: 2, LiveOnlyCount: 0,
            Phase2PendingCount: 6, MixedCount: 0, OtherCount: 0);
        // (LIVE + LIVE ONLY) / total = 2/8 = 25%
        var tooltip = MainViewModelV2.FormatTabTooltip("Combat", rollup);
        tooltip.Should().Contain("Combat");
        tooltip.Should().Contain("2 LIVE");
        tooltip.Should().Contain("6 PHASE 2");
        tooltip.Should().Contain("8 actions");
        tooltip.Should().Contain("25% engine-effective");
    }

    [Fact]
    public void Format_LiveOnlyCountsTowardEngineEffectivePercent()
    {
        // Inspector — single action, LIVE ONLY (RequiresLiveSwfoc).
        // Should still register as 100% engine-effective: the gate is
        // "needs running game", not "no engine effect".
        var rollup = new CapabilitySurfaceReport.SurfaceRollup(
            TotalActions: 1, LiveCount: 0, LiveOnlyCount: 1,
            Phase2PendingCount: 0, MixedCount: 0, OtherCount: 0);
        var tooltip = MainViewModelV2.FormatTabTooltip("Inspector", rollup);
        tooltip.Should().Contain("100% engine-effective",
            "LIVE + LIVE ONLY both count toward engine-effective");
    }

    [Fact]
    public void Format_ZeroActions_RendersZeroPercent()
    {
        var rollup = new CapabilitySurfaceReport.SurfaceRollup(0, 0, 0, 0, 0, 0);
        var tooltip = MainViewModelV2.FormatTabTooltip("Empty", rollup);
        tooltip.Should().Contain("0 actions");
        tooltip.Should().Contain("0% engine-effective");
    }
}
