using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 185) — pins the iter-185 spawn-variant batch shipping
/// 3 wires via iter-184's 3-arg helper at marginal cost (~3 LoC each).
/// LIVE flips #139-141; master loop now at 141 LIVE wires.
///
/// CRITICAL gotcha-pin: SWFOC_CreateGenericObjectLua takes (type, position, player)
/// — DIFFERENT from Spawn_Unit's (player, type, position). The catalog rationale
/// must flag this loudly so future readers don't assume parity.
/// </summary>
public sealed class Iter185SpawnVariantBatchTests
{
    [Theory]
    [InlineData("SWFOC_ReinforceUnitLua")]
    [InlineData("SWFOC_SpawnFromReinforcementPoolLua")]
    [InlineData("SWFOC_CreateGenericObjectLua")]
    public void SpawnVariantBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void SpawnVariantBatch_AllReuseIter184Helper()
    {
        var iter185Entries = new[]
        {
            "SWFOC_ReinforceUnitLua",
            "SWFOC_SpawnFromReinforcementPoolLua",
            "SWFOC_CreateGenericObjectLua",
        };
        foreach (var name in iter185Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-184",
                    $"{name} should reference iter-184 3-arg helper");
        }
    }

    [Fact]
    public void ReinforceUnit_NoteFlagsAlternativeToIter109()
    {
        // Pin: ReinforceUnit is the reinforcement-pool alternative to
        // iter-109 SWFOC_SpawnUnitLua (direct spawn). Catalog should make
        // the distinction explicit.
        var note = CapabilityStatusCatalog.Entries["SWFOC_ReinforceUnitLua"].Note;
        note.Should().Contain("iter-109");
        note.Should().Contain("reinforcement");
    }

    [Fact]
    public void SpawnFromReinforcementPool_FlagsAlternativeEntrypoint()
    {
        // Pin: per docs, this is documented as an alternative reinforcement
        // spawn to Reinforce_Unit. Catalog rationale should explain that
        // both names exist.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SpawnFromReinforcementPoolLua"].Note;
        note.Should().Contain("alternative");
        note.Should().Contain("Reinforce_Unit");
    }

    [Fact]
    public void CreateGenericObject_FlagsParamOrderGotcha()
    {
        // CRITICAL pin: Create_Generic_Object's param order is DIFFERENT
        // from Spawn_Unit. Catalog MUST flag this loudly. The test fails
        // if the gotcha is removed from the rationale.
        var note = CapabilityStatusCatalog.Entries["SWFOC_CreateGenericObjectLua"].Note;
        note.Should().Contain("GOTCHA");
        note.Should().Contain("param order differs");
        note.Should().Contain("Spawn_Unit");
    }

    [Fact]
    public void CreateGenericObject_FlagsNonUnitObjectUseCase()
    {
        // Pin: catalog should explain WHY this exists separately from Spawn_Unit
        // (props, particle emitters, etc.) — the use case justifies the duplication.
        var note = CapabilityStatusCatalog.Entries["SWFOC_CreateGenericObjectLua"].Note;
        note.Should().Contain("non-unit");
    }

    [Fact]
    public void SpawnVariantBatch_AllTaggedIter185Live()
    {
        var iter185Entries = new[]
        {
            "SWFOC_ReinforceUnitLua",
            "SWFOC_SpawnFromReinforcementPoolLua",
            "SWFOC_CreateGenericObjectLua",
        };
        foreach (var name in iter185Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 185 LIVE",
                    $"{name} should be tagged as iter 185 LIVE");
        }
    }
}
