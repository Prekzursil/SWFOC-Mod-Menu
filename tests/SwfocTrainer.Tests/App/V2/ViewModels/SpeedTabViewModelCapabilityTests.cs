using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 56) — pins per-button capability metadata on the
/// Speed tab. 2026-04-28 (iter 100, master ralph loop): SetUnitSpeed and
/// SetFactionSpeed flipped LIVE via the engine's SetSpeedOverride helper
/// at RVA 0x3A8C90. Only SetGameSpeed remains PHASE 2 PENDING (the
/// global game-speed multiplier needs a different chokepoint).
/// </summary>
public sealed class SpeedTabViewModelCapabilityTests
{
    private static SpeedTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new SpeedTabViewModel(adapter);
    }

    [Fact]
    public void SetGameSpeed_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetGameSpeed.Badge.Should().Be("PHASE 2 PENDING",
            "SWFOC_SetGameSpeed is catalogued PHASE 2 PENDING (Phase-1-mirror only)");
    }

    [Fact]
    public void SetFactionSpeed_BadgeIsLive()
    {
        // 2026-04-28 (iter 100): per-faction LIVE-wired via SetSpeedOverride.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetFactionSpeed.Badge.Should().Be("LIVE",
            "iter 100 wired SetSpeedOverride per-unit enumeration; LIVE");
    }

    [Fact]
    public void SetUnitSpeed_BadgeIsLive()
    {
        // 2026-04-28 (iter 100): per-unit LIVE-wired via SetSpeedOverride.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetUnitSpeed.Badge.Should().Be("LIVE",
            "iter 100 wired SetSpeedOverride engine call; LIVE");
    }

    [Fact]
    public void HasPhase2PendingAction_TrueForSpeedTab()
    {
        // SetGameSpeed remains PHASE 2 PENDING — the global-speed
        // chokepoint isn't pinned yet.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeTrue(
            "SetGameSpeed (global game-speed) still PHASE 2 PENDING after iter 100");
    }

    [Fact]
    public void Phase2PendingWarning_NamesGameSpeedAfterIter100()
    {
        // After iter 100, only SetGameSpeed remains in the warning text.
        var vm = NewVm(out var sim); using var _ = sim;
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Set global game speed",
            "SetGameSpeed is the only PHASE 2 PENDING action on Speed tab");
        warning.Should().NotContain("Set faction move-speed multiplier",
            "iter 100 flipped SetFactionSpeed to LIVE");
        warning.Should().NotContain("Set unit speed",
            "iter 100 flipped SetUnitSpeed to LIVE");
    }

    [Fact]
    public void Phase2PendingWarning_ExplainsDisabledPendingAction()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.Phase2PendingWarning.Should().Contain("disabled");
        vm.Phase2PendingWarning.Should().Contain("live engine hook");
        vm.SetGameSpeedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AllActions_EnumeratesEveryActionInDeclaredOrder()
    {
        // 2026-04-28 (iter 100): added ClearUnitSpeedOverride — 4 actions total.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(4);
        vm.AllActions[0].Should().BeSameAs(vm.SetGameSpeed);
        vm.AllActions[1].Should().BeSameAs(vm.SetFactionSpeed);
        vm.AllActions[2].Should().BeSameAs(vm.SetUnitSpeed);
        vm.AllActions[3].Should().BeSameAs(vm.ClearUnitSpeedOverride);
    }

    [Fact]
    public void ClearUnitSpeedOverride_BadgeIsLive()
    {
        // 2026-04-28 (iter 100): revert helper LIVE-wired alongside SetUnitSpeed.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ClearUnitSpeedOverride.Badge.Should().Be("LIVE",
            "iter 100 wired ClearSpeedOverride engine call; LIVE");
    }
}
