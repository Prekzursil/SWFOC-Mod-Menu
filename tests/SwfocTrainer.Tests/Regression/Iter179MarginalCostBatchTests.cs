using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 179) — first batch post matrix-complete (iter 178).
/// All 4 wires reuse existing helpers — bridge marginal cost is ~3 LoC each.
/// LIVE flips #126-129; master loop now at 129 LIVE wires.
/// Mix of helper reuse demonstrates the matrix is genuinely complete:
///   Is_Enemy / Is_Ally → iter-173 unit-getter-arg (player receiver)
///   Find_All_Objects_Of_Type → iter-177 global-getter-arg
///   TaskForce_Move_To_Target → iter-154 1-arg unit/TaskForce method
/// </summary>
public sealed class Iter179MarginalCostBatchTests
{
    [Theory]
    [InlineData("SWFOC_IsEnemyLua")]
    [InlineData("SWFOC_IsAllyLua")]
    [InlineData("SWFOC_FindAllObjectsOfTypeLua")]
    [InlineData("SWFOC_TaskForceMoveToTargetLua")]
    public void MarginalCostBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void IsEnemy_NotePinsHelperReuse()
    {
        // Pin: catalog rationale should call out the iter-173 helper reuse
        // and the player-receiver shape-agnosticism finding.
        var note = CapabilityStatusCatalog.Entries["SWFOC_IsEnemyLua"].Note;
        note.Should().Contain("iter-173");
        note.Should().Contain("shape-agnostic");
    }

    [Fact]
    public void IsEnemy_NoteIncludesGetLocalPlayerComposition()
    {
        // Pin: operator-facing rationale should pair with iter-178 GetLocalPlayer.
        CapabilityStatusCatalog.Entries["SWFOC_IsEnemyLua"].Note
            .Should().Contain("Get_Local_Player");
    }

    [Fact]
    public void IsAlly_NotePinsModeChangeResetCaveat()
    {
        // Pin: docs flag that Make_Ally/Make_Enemy reset on mode change.
        // Catalog should propagate that warning to Is_Ally readings.
        var note = CapabilityStatusCatalog.Entries["SWFOC_IsAllyLua"].Note;
        note.Should().Contain("RESETS");
        note.Should().Contain("Galactic");
    }

    [Fact]
    public void FindAllObjectsOfType_NotePinsTableHandlingPattern()
    {
        // Pin: helper tostring()s the Lua table; operators iterate via Playground.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindAllObjectsOfTypeLua"].Note;
        note.Should().Contain("table:");
        note.Should().Contain("for i,obj in pairs");
    }

    [Fact]
    public void TaskForceMoveToTarget_NoteDistinguishesFromIter175MoveTo()
    {
        // Pin: iter-175 SWFOC_TaskForceMoveToLua takes position; iter-179
        // SWFOC_TaskForceMoveToTargetLua takes a target unit/object.
        // Catalog should make the distinction explicit.
        var note = CapabilityStatusCatalog.Entries["SWFOC_TaskForceMoveToTargetLua"].Note;
        note.Should().Contain("iter-175");
        note.Should().Contain("position");
    }

    [Fact]
    public void MarginalCostBatch_AllTaggedIter179Live()
    {
        var iter179Entries = new[]
        {
            "SWFOC_IsEnemyLua",
            "SWFOC_IsAllyLua",
            "SWFOC_FindAllObjectsOfTypeLua",
            "SWFOC_TaskForceMoveToTargetLua",
        };
        foreach (var name in iter179Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 179 LIVE",
                    $"{name} should be tagged as iter 179 LIVE");
        }
    }
}
