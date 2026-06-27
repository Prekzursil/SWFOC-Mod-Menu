using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 57) — pins per-button capability metadata on the
/// Galactic tab. RevealAll is LIVE; refresh, owner change, convert,
/// pure-kick, story-arrival spawn are PHASE 2 PENDING.
///
/// 2026-04-29 (iter 133) — SetDiplomacy flipped LIVE via MakeAllyEnemy
/// engine writer @ 0x288800. Iter 135 cleanup: this test class now
/// reflects the iter 133 catalog flip (was missed at iter 133 ship).
/// </summary>
public sealed class GalacticTabViewModelCapabilityTests
{
    private static GalacticTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new GalacticTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));
    }

    [Fact]
    public void ToggleRevealAll_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleRevealAll.Badge.Should().Be("LIVE",
            "SWFOC_RevealAll routes through the engine via SWFOC_DoString");
    }

    [Fact]
    public void RefreshPlanets_BadgeIsLive()
    {
        // iter-296 shipped real galactic-mode planet enumeration; previously
        // PHASE 2 PENDING (the count=0 stub). iter-317 planet-icon column is
        // the first UI consumer that depends on this LIVE wire.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.RefreshPlanets.Badge.Should().Be("LIVE",
            "iter-296 promoted SWFOC_GetPlanets to LIVE; iter-317 planet-icon column relies on it");
    }

    [Fact]
    public void ChangeOwner_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ChangeOwner.Badge.Should().Be("PHASE 2 PENDING");
    }

    [Fact]
    public void FlipModes_BothPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ChangeOwnerConvert.Badge.Should().Be("PHASE 2 PENDING");
        vm.ChangeOwnerPureKick.Badge.Should().Be("PHASE 2 PENDING",
            "Convert + PureKick share SWFOC_ChangePlanetOwnerWithMode primitive");
    }

    [Fact]
    public void SetDiplomacy_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetDiplomacy.Badge.Should().Be("LIVE",
            "iter 133 flipped SWFOC_SetDiplomacy LIVE via MakeAllyEnemy engine writer @ 0x288800");
    }

    [Fact]
    public void SpawnAsStoryArrival_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SpawnAsStoryArrival.Badge.Should().Be("PHASE 2 PENDING");
    }

    [Fact]
    public void HasPhase2PendingAction_TrueForGalacticTab()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeTrue(
            "5 of 7 Galactic actions remain PHASE 2 PENDING (refresh, owner-change, convert, pure-kick, story-arrival); RevealAll + SetDiplomacy are LIVE iter 133");
    }

    [Fact]
    public void Phase2PendingWarning_NamesEveryNonLiveAction()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Change planet owner");
        warning.Should().Contain("Flip & convert garrison");
        warning.Should().Contain("Flip & destroy garrison");
        warning.Should().Contain("Story-arrival spawn");
        warning.Should().NotContain("Toggle reveal-all",
            "uniformly LIVE actions must NOT appear in the warning");
        warning.Should().NotContain("Set diplomacy",
            "iter 133 flipped SetDiplomacy LIVE; warning must reflect");
        warning.Should().NotContain("Refresh planets",
            "iter-296 flipped SWFOC_GetPlanets LIVE; warning must reflect");
    }

    [Fact]
    public void AllActions_EnumeratesEveryActionInDeclaredOrder()
    {
        // 2026-05-05 (iter 200): Galactic tab grew from 7 → 10 actions when
        // the Fog-of-War reveal GroupBox shipped.
        // 2026-05-06 (iter 215): grew from 10 → 19 actions when the
        // TaskForce write-side mega-batch shipped (iter-175 + iter-176 = 8 wires
        // surfaced as 9 actions including Set_As_Goal_System_Removable on/off pair).
        // 2026-05-06 (iter 218): grew from 19 → 20 actions with single-wire
        // TaskForceMoveToTarget extension (iter-179 wire — Move_To_Target is
        // distinct from iter-215 Move_To which targets a position).
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(20);
        vm.AllActions[0].Should().BeSameAs(vm.RefreshPlanets);
        vm.AllActions[1].Should().BeSameAs(vm.ChangeOwner);
        vm.AllActions[2].Should().BeSameAs(vm.ChangeOwnerConvert);
        vm.AllActions[3].Should().BeSameAs(vm.ChangeOwnerPureKick);
        vm.AllActions[4].Should().BeSameAs(vm.ToggleRevealAll);
        vm.AllActions[5].Should().BeSameAs(vm.SetDiplomacy);
        vm.AllActions[6].Should().BeSameAs(vm.SpawnAsStoryArrival);
        vm.AllActions[7].Should().BeSameAs(vm.FOWRevealAllLua);
        vm.AllActions[8].Should().BeSameAs(vm.FOWUndoRevealAllLua);
        vm.AllActions[9].Should().BeSameAs(vm.FOWRevealLua);
        // iter 215: TaskForce write-side
        vm.AllActions[10].Should().BeSameAs(vm.TaskForceMoveToLuaAction);
        vm.AllActions[11].Should().BeSameAs(vm.TaskForceReinforceLuaAction);
        vm.AllActions[12].Should().BeSameAs(vm.TaskForceReleaseReinforcementsLuaAction);
        vm.AllActions[13].Should().BeSameAs(vm.TaskForceLaunchUnitsLuaAction);
        vm.AllActions[14].Should().BeSameAs(vm.TaskForceAttackTargetLuaAction);
        vm.AllActions[15].Should().BeSameAs(vm.TaskForceGuardTargetLuaAction);
        vm.AllActions[16].Should().BeSameAs(vm.TaskForceLandUnitsLuaAction);
        vm.AllActions[17].Should().BeSameAs(vm.TaskForceSetAsGoalSystemRemovableOnLuaAction);
        vm.AllActions[18].Should().BeSameAs(vm.TaskForceSetAsGoalSystemRemovableOffLuaAction);
        // iter 218: TaskForceMoveToTarget single-wire extension
        vm.AllActions[19].Should().BeSameAs(vm.TaskForceMoveToTargetLuaAction);
    }
}
