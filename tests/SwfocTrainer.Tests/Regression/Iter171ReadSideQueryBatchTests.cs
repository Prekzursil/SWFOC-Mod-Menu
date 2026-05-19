using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-30 (iter 171) — pins read-side query LIVE batch via iter-167
/// helper. LIVE flips #96-99; master loop now at 99 LIVE wires.
/// Get_Position / Get_Parent_Object / Get_Attack_Target return handles;
/// Get_Damage_Modifier forms read-after-write pair with iter-154
/// SetDamageModifier writer.
/// </summary>
public sealed class Iter171ReadSideQueryBatchTests
{
    [Theory]
    [InlineData("SWFOC_GetPositionLua")]
    [InlineData("SWFOC_GetParentObjectLua")]
    [InlineData("SWFOC_GetAttackTargetLua")]
    [InlineData("SWFOC_GetDamageModifierLua")]
    public void ReadSideQueryBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GetAttackTarget_NotePairsWithIter168HasAttackTarget()
    {
        // Pin: chainability with iter-168 predicate must survive.
        CapabilityStatusCatalog.Entries["SWFOC_GetAttackTargetLua"].Note
            .Should().Contain("Has_Attack_Target");
    }

    [Fact]
    public void GetDamageModifier_NotePairsWithIter154Writer()
    {
        CapabilityStatusCatalog.Entries["SWFOC_GetDamageModifierLua"].Note
            .Should().Contain("SetDamageModifier");
    }

    [Fact]
    public void ReadSideQueryBatch_AllReuseIter167Helper()
    {
        var iter171Entries = new[]
        {
            "SWFOC_GetPositionLua",
            "SWFOC_GetParentObjectLua",
            "SWFOC_GetAttackTargetLua",
            "SWFOC_GetDamageModifierLua",
        };
        foreach (var name in iter171Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-167",
                    $"{name} should reference reuse of iter-167 helper");
        }
    }

    [Fact]
    public void ReadSideQueryBatch_AllTaggedIter171()
    {
        var iter171Entries = new[]
        {
            "SWFOC_GetPositionLua",
            "SWFOC_GetParentObjectLua",
            "SWFOC_GetAttackTargetLua",
            "SWFOC_GetDamageModifierLua",
        };
        foreach (var name in iter171Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 171 LIVE",
                    $"{name} should be tagged as iter 171 LIVE in catalog rationale");
        }
    }
}
