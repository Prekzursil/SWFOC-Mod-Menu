using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 173) — pins arg-getter LIVE batch shipped via NEW
/// Lua_DispatchUnitGetterArg helper (7th in dispatcher set; mirror of
/// iter-167 but for `(obj):method(arg)` shape with return-value capture).
/// LIVE flips #104-107; master loop now at 107 LIVE wires.
/// Is_Ability_Active forms read-after-write pair with iter-156 Activate_Ability.
/// </summary>
public sealed class Iter173UnitGetterArgBatchTests
{
    [Theory]
    [InlineData("SWFOC_IsAbilityActiveLua")]
    [InlineData("SWFOC_HasPropertyLua")]
    [InlineData("SWFOC_IsCategoryLua")]
    [InlineData("SWFOC_GetDistanceLua")]
    public void UnitGetterArgBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void IsAbilityActive_NotePinsAsFirstWireViaNewHelper()
    {
        // Pin: catalog rationale should call out that this is the FIRST
        // wire shipped via the iter-173 arg-getter helper. Future readers
        // need to know to use this helper (not iter-167 no-arg) for
        // arg-taking getters.
        CapabilityStatusCatalog.Entries["SWFOC_IsAbilityActiveLua"].Note
            .Should().Contain("NEW iter-173");
    }

    [Fact]
    public void IsAbilityActive_NotePairsWithIter156Writer()
    {
        // Pin: read-after-write pair with iter-156 Activate_Ability.
        CapabilityStatusCatalog.Entries["SWFOC_IsAbilityActiveLua"].Note
            .Should().Contain("ActivateAbility");
    }

    [Fact]
    public void GetDistance_NoteMentionsRangeCheckUseCase()
    {
        // Pin: operator-facing rationale (range-check before attack).
        CapabilityStatusCatalog.Entries["SWFOC_GetDistanceLua"].Note
            .Should().Contain("range-check");
    }

    [Fact]
    public void UnitGetterArgBatch_AllReuseIter173Helper()
    {
        var iter173Entries = new[]
        {
            "SWFOC_IsAbilityActiveLua",
            "SWFOC_HasPropertyLua",
            "SWFOC_IsCategoryLua",
            "SWFOC_GetDistanceLua",
        };
        foreach (var name in iter173Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-173",
                    $"{name} should reference iter-173 unit-getter-with-arg helper");
        }
    }

    [Fact]
    public void UnitGetterArgBatch_AllTaggedIter173()
    {
        var iter173Entries = new[]
        {
            "SWFOC_IsAbilityActiveLua",
            "SWFOC_HasPropertyLua",
            "SWFOC_IsCategoryLua",
            "SWFOC_GetDistanceLua",
        };
        foreach (var name in iter173Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 173 LIVE",
                    $"{name} should be tagged as iter 173 LIVE in catalog rationale");
        }
    }
}
