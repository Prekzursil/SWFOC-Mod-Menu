using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 58) — pins per-button capability metadata on the
/// Director Mode tab. Originally: SetTimeScale + Start/Step playback all
/// PHASE 2 PENDING; Toggle Hide UI LIVE.
///
/// 2026-05-07 (iter 259 cascading-drift catch from iter-237): SetCameraPos
/// got LIVE-flipped at iter-237 via direct SetTransformMatrix call (see
/// iter237_setcamerapos_bridge_live.md). StartPlayback + StepPlayback
/// derive their badges from SWFOC_SetCameraPos, so they now also report
/// LIVE. SetTimeScale stays PHASE 2 PENDING (its SWFOC_SetGameSpeed wire
/// is still confirmed-defer per iter-131 audit — no engine RVA in the
/// ledger). 3rd-cousin cascade catch from the iter-237 silent-flip:
/// iter-243 caught Phase2PendingEntryCount; iter-258 caught Iter107
/// ScrollCameraToTarget; iter-259 catches the DirectorMode trio. Proves
/// the recursive-cleanup pattern in iter-258 pattern lesson #5.
/// </summary>
public sealed class DirectorModeTabViewModelCapabilityTests
{
    private static DirectorModeTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new DirectorModeTabViewModel(adapter);
    }

    [Fact]
    public void ToggleHideUi_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleHideUi.Badge.Should().Be("LIVE",
            "Hide_HUD routes via SWFOC_DoString — engine global escape hatch is LIVE");
    }

    [Fact]
    public void SetTimeScale_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetTimeScale.Badge.Should().Be("PHASE 2 PENDING",
            "SWFOC_SetGameSpeed remains PHASE 2 PENDING per iter-131 confirmed-defer (no engine RVA in ledger)");
    }

    [Fact]
    public void StartPlayback_BadgeIsLive_PerIter237Cascade()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.StartPlayback.Badge.Should().Be("LIVE",
            "iter-237 promoted SWFOC_SetCameraPos to LIVE via direct SetTransformMatrix call; " +
            "playback fires SetCameraPos per waypoint and now propagates LIVE engine effect");
    }

    [Fact]
    public void StepPlayback_SamePrimitiveAsStart_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.StepPlayback.Badge.Should().Be("LIVE",
            "iter-237 SetCameraPos LIVE flip propagates to single-waypoint Step variant");
    }

    [Fact]
    public void HasPhase2PendingAction_TrueForDirectorTab()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeTrue(
            "SetTimeScale stays PHASE 2 PENDING — at least one Phase-2 action remains");
    }

    [Fact]
    public void Phase2PendingWarning_NamesOnlySetTimeScale_PostIter237Cascade()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Set time scale",
            "SWFOC_SetGameSpeed is the last remaining Phase-2 action in Director Mode tab");
        warning.Should().NotContain("Start playback",
            "iter-237 promoted SetCameraPos to LIVE — Start playback now LIVE");
        warning.Should().NotContain("Step playback",
            "iter-237 promoted SetCameraPos to LIVE — Step playback now LIVE");
        warning.Should().NotContain("Toggle hide UI",
            "Hide_HUD via SWFOC_DoString was LIVE before this cascade");
    }

    [Fact]
    public void AllActions_EnumeratesEveryActionInDeclaredOrder()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(4);
        vm.AllActions[0].Should().BeSameAs(vm.SetTimeScale);
        vm.AllActions[1].Should().BeSameAs(vm.StartPlayback);
        vm.AllActions[2].Should().BeSameAs(vm.StepPlayback);
        vm.AllActions[3].Should().BeSameAs(vm.ToggleHideUi);
    }
}
