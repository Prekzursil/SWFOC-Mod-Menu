using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 157) — pins 6-wire unit-method mega-batch.
/// All wires reuse iter-111/112/154 helpers; ~3 LoC bridge per wire.
/// LIVE flips #47-52. Master loop now at 52 LIVE wires.
/// </summary>
public sealed class Iter157UnitMethodMegaBatchTests
{
    [Theory]
    [InlineData("SWFOC_SetInLimboLua")]
    [InlineData("SWFOC_SetCheckContestedSpaceLua")]
    [InlineData("SWFOC_SellUnitLua")]
    [InlineData("SWFOC_BribeLua")]
    [InlineData("SWFOC_MoveToLua")]
    [InlineData("SWFOC_FireSpecialWeaponLua")]
    public void AllSix_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void AllSix_BadgeReportsLive()
    {
        CapabilityStatusCatalog.ComposeBadge(
            "SWFOC_SetInLimboLua",
            "SWFOC_SetCheckContestedSpaceLua",
            "SWFOC_SellUnitLua",
            "SWFOC_BribeLua",
            "SWFOC_MoveToLua",
            "SWFOC_FireSpecialWeaponLua")
            .Should().Be("LIVE",
                "all 6 iter-157 wires must compose to a uniform LIVE badge");
    }
}
