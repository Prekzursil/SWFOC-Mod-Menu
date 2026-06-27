using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 88) — pins the top-3 PHASE 2 PENDING tab formatter
/// used in the bottom-bar capability tooltip. Tests drive the static
/// helper directly with synthetic tabs/actions, bypassing the heavy
/// MainViewModelV2 construction.
/// </summary>
public sealed class Iter88TopPhase2PendingTabsTests
{
    private static IReadOnlyList<CapabilityAwareAction> Actions(params string[] helpers) =>
        new[] { new CapabilityAwareAction("synthetic action", helpers) };

    [Fact]
    public void Format_NoPendingTabs_ReturnsEmptyString()
    {
        // Tab with only LIVE helpers → no entry in the top-3 list.
        var tabs = new[]
        {
            ("All-Live Tab", Actions("SWFOC_GodMode", "SWFOC_KillUnit")),
        };

        MainViewModelV2.FormatTopPhase2PendingTabs(tabs).Should().BeEmpty(
            "tabs with no PHASE 2 PENDING helpers don't contribute to the top-3 list");
    }

    [Fact]
    public void Format_OnePendingTab_ShowsBulletWithCount()
    {
        // Tab with 2 PHASE 2 PENDING helpers (per the catalog).
        var tabs = new[]
        {
            ("Combat", Actions("SWFOC_SetDamageMultiplier", "SWFOC_SetFireRate")),
        };

        var result = MainViewModelV2.FormatTopPhase2PendingTabs(tabs);

        result.Should().Contain("Combat");
        result.Should().Contain("2 PHASE 2 PENDING");
        result.Should().StartWith("  •");
    }

    [Fact]
    public void Format_RanksByCountDescending()
    {
        var tabs = new[]
        {
            ("LowPending", Actions("SWFOC_SetDamageMultiplier")),  // 1 pending
            ("HighPending", Actions("SWFOC_SetDamageMultiplier", "SWFOC_SetFireRate", "SWFOC_SetUnitShield")),  // 3 pending
            ("MediumPending", Actions("SWFOC_SetDamageMultiplier", "SWFOC_SetFireRate")),  // 2 pending
        };

        var result = MainViewModelV2.FormatTopPhase2PendingTabs(tabs);

        // HighPending must come first, MediumPending second, LowPending third.
        var highIdx = result.IndexOf("HighPending", StringComparison.Ordinal);
        var medIdx = result.IndexOf("MediumPending", StringComparison.Ordinal);
        var lowIdx = result.IndexOf("LowPending", StringComparison.Ordinal);
        highIdx.Should().BeLessThan(medIdx);
        medIdx.Should().BeLessThan(lowIdx);
    }

    [Fact]
    public void Format_CapsAtTopThree()
    {
        // 5 tabs all with pending; only the top-3 by count should appear.
        // Use multi-character names so substring assertions don't collide
        // with words like "PENDING" (which contains "D" + "E").
        var tabs = new[]
        {
            ("AlphaTab", Actions("SWFOC_SetDamageMultiplier", "SWFOC_SetFireRate", "SWFOC_SetUnitShield", "SWFOC_SetGameSpeed")),  // 4
            ("BetaTab", Actions("SWFOC_SetDamageMultiplier", "SWFOC_SetFireRate", "SWFOC_SetUnitShield")),  // 3
            ("GammaTab", Actions("SWFOC_SetDamageMultiplier", "SWFOC_SetFireRate")),  // 2
            ("DeltaTab", Actions("SWFOC_SetDamageMultiplier")),  // 1
            ("EpsilonTab", Actions("SWFOC_SetFireRate")),  // 1
        };

        var result = MainViewModelV2.FormatTopPhase2PendingTabs(tabs);

        result.Should().Contain("AlphaTab");
        result.Should().Contain("BetaTab");
        result.Should().Contain("GammaTab");
        result.Should().NotContain("DeltaTab", "only top-3 by pending count are surfaced");
        result.Should().NotContain("EpsilonTab");
    }

    [Fact]
    public void Format_EmptyTabsList_ReturnsEmptyString()
    {
        MainViewModelV2.FormatTopPhase2PendingTabs(Array.Empty<(string, IReadOnlyList<CapabilityAwareAction>)>())
            .Should().BeEmpty();
    }

    [Fact]
    public void Format_NullArgument_Throws()
    {
        Action act = () => MainViewModelV2.FormatTopPhase2PendingTabs(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
