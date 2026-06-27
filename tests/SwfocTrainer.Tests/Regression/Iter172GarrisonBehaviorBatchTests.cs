using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-30 (iter 172) — pins read-side garrison/behavior batch.
/// **100 LIVE wire milestone**: master loop crosses 100 LIVE wires
/// during this iter (LIVE flips #100-103). Get_Rate_Of_Fire_Modifier
/// pairs with iter-154 SetRateOfFireModifier writer. All 4 reuse
/// iter-167 helper.
/// </summary>
public sealed class Iter172GarrisonBehaviorBatchTests
{
    [Theory]
    [InlineData("SWFOC_GetGarrisonUnitsLua")]
    [InlineData("SWFOC_GetContainedObjectCountLua")]
    [InlineData("SWFOC_GetBehaviorIdLua")]
    [InlineData("SWFOC_GetRateOfFireModifierLua")]
    public void GarrisonBehaviorBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GetRateOfFireModifier_NotePairsWithIter154Writer()
    {
        // Pin: read-after-write pair with iter-154 SetRateOfFireModifier
        // must survive in catalog rationale.
        CapabilityStatusCatalog.Entries["SWFOC_GetRateOfFireModifierLua"].Note
            .Should().Contain("SetRateOfFireModifier");
    }

    [Fact]
    public void GetContainedObjectCount_NotePairsWithGetGarrisonUnits()
    {
        // Pin: chainability for garrison inspection workflow.
        CapabilityStatusCatalog.Entries["SWFOC_GetContainedObjectCountLua"].Note
            .Should().Contain("Get_Garrison_Units");
    }

    [Fact]
    public void GarrisonBehaviorBatch_AllReuseIter167Helper()
    {
        var iter172Entries = new[]
        {
            "SWFOC_GetGarrisonUnitsLua",
            "SWFOC_GetContainedObjectCountLua",
            "SWFOC_GetBehaviorIdLua",
            "SWFOC_GetRateOfFireModifierLua",
        };
        foreach (var name in iter172Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-167",
                    $"{name} should reference reuse of iter-167 helper");
        }
    }

    [Fact]
    public void GarrisonBehaviorBatch_AllTaggedIter172()
    {
        var iter172Entries = new[]
        {
            "SWFOC_GetGarrisonUnitsLua",
            "SWFOC_GetContainedObjectCountLua",
            "SWFOC_GetBehaviorIdLua",
            "SWFOC_GetRateOfFireModifierLua",
        };
        foreach (var name in iter172Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 172 LIVE",
                    $"{name} should be tagged as iter 172 LIVE in catalog rationale");
        }
    }

    [Fact]
    public void MasterLoop_HasAtLeast100LiveWires()
    {
        // Pin the 100 LIVE wire milestone: iter 172 crosses the
        // threshold. Counting Live entries via SWFOC_*Lua suffix
        // is a rough proxy but catches major regressions.
        var liveCount = CapabilityStatusCatalog.Entries.Values
            .Count(e => e.Status == CapabilityStatus.Live);
        liveCount.Should().BeGreaterThanOrEqualTo(100,
            "master loop iter 100-172 should have at least 100 LIVE-status catalog entries");
    }
}
