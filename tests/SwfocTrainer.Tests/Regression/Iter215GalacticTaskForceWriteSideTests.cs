using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 215) — pins the iter-175 + iter-176 TaskForce write-side
/// LIVE surfacing on the Galactic tab. 8 wires across 3 dispatcher patterns
/// surfaced as 9 buttons (Set_As_Goal_System_Removable on/off pair):
///   - iter-175 Move_To/Reinforce/Launch_Units: 1-arg via iter-154 helper
///   - iter-175 Release_Reinforcements: no-arg via iter-112 helper
///   - iter-176 Attack_Target/Guard_Target/Land_Units: 1-arg via iter-154 helper
///   - iter-176 Set_As_Goal_System_Removable: 1-arg bool via iter-111 helper
///     (iter-204 hardcoded-bool pattern — now 6 iters deep:
///     204→208→211→212→213→215)
///
/// Operator workflow: 9 buttons all anchor on TaskForceLuaExpr (new field).
/// Secondary args use TaskForceTargetLuaExpr (semantically polymorphic —
/// target/planet/unit-type depending on which button is clicked).
/// </summary>
public sealed class Iter215GalacticTaskForceWriteSideTests
{
    [Fact]
    public void CatalogEntries_AllEightRemainLive()
    {
        var entries = new[]
        {
            "SWFOC_TaskForceMoveToLua",
            "SWFOC_TaskForceReinforceLua",
            "SWFOC_TaskForceReleaseReinforcementsLua",
            "SWFOC_TaskForceLaunchUnitsLua",
            "SWFOC_TaskForceAttackTargetLua",
            "SWFOC_TaskForceGuardTargetLua",
            "SWFOC_TaskForceLandUnitsLua",
            "SWFOC_TaskForceSetAsGoalSystemRemovableLua"
        };
        foreach (var key in entries)
        {
            CapabilityStatusCatalog.Entries[key].Status
                .Should().Be(CapabilityStatus.Live, $"{key} should be LIVE");
        }
    }

    [Fact]
    public void CatalogRationale_AllEightEntriesDocumentIter215Surfacing()
    {
        var entries = new[]
        {
            "SWFOC_TaskForceMoveToLua",
            "SWFOC_TaskForceReinforceLua",
            "SWFOC_TaskForceReleaseReinforcementsLua",
            "SWFOC_TaskForceLaunchUnitsLua",
            "SWFOC_TaskForceAttackTargetLua",
            "SWFOC_TaskForceGuardTargetLua",
            "SWFOC_TaskForceLandUnitsLua",
            "SWFOC_TaskForceSetAsGoalSystemRemovableLua"
        };
        foreach (var key in entries)
        {
            var note = CapabilityStatusCatalog.Entries[key].Note;
            note.Should().Contain("Iter 215", $"{key} should be marked iter-215 surfaced");
            note.Should().Contain("Galactic", $"{key} should reference Galactic tab");
        }
    }

    [Fact]
    public void CatalogRationale_BoolPairReferencesIter204LineageSixIters()
    {
        // Pin: Set_As_Goal_System_Removable extends the iter-204 hardcoded-bool
        // on/off lineage to 6 iters: 204→208→211→212→213→215. The catalog
        // rationale documents this lineage explicitly.
        var note = CapabilityStatusCatalog.Entries["SWFOC_TaskForceSetAsGoalSystemRemovableLua"].Note;
        note.Should().Contain("iter-204");
        note.Should().Contain("on/off");
        note.Should().Contain("6 iters");
    }

    [Fact]
    public void CatalogRationale_FieldReuseDocumentedAcrossEntries()
    {
        // Pin: at least 4 of the 8 wires document TaskForceLuaExpr +
        // TaskForceTargetLuaExpr field reuse. Pin field naming so a future
        // rationale cleanup doesn't drop the field-binding documentation.
        var moveTo = CapabilityStatusCatalog.Entries["SWFOC_TaskForceMoveToLua"].Note;
        moveTo.Should().Contain("TaskForceLuaExpr");
        moveTo.Should().Contain("TaskForceTargetLuaExpr");
    }

    [Fact]
    public void Vm_ExposesAllNineCommandsAndCapabilityActionsAndTwoFields()
    {
        // Pin: 9 ICommands (Set_As_Goal pair = 2 commands) + 9 capability
        // actions + 2 new fields (TaskForceLuaExpr + TaskForceTargetLuaExpr).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.GalacticTabViewModel);
        t.GetProperty("TaskForceMoveToLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceReinforceLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceReleaseReinforcementsLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceLaunchUnitsLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceAttackTargetLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceGuardTargetLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceLandUnitsLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceSetAsGoalSystemRemovableOnLuaCommand").Should().NotBeNull();
        t.GetProperty("TaskForceSetAsGoalSystemRemovableOffLuaCommand").Should().NotBeNull();

        t.GetProperty("TaskForceLuaExpr").Should().NotBeNull("TaskForce receiver field");
        t.GetProperty("TaskForceTargetLuaExpr").Should().NotBeNull("TaskForce secondary-arg field");
    }
}
