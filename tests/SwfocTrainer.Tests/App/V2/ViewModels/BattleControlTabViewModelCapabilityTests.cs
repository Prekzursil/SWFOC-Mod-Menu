using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 55) — pins the per-button capability metadata on
/// <see cref="BattleControlTabViewModel"/>:
/// <list type="bullet">
///   <item>ToggleFreezeAi uses the LIVE SuspendAiLua helper.</item>
///   <item>Set/ClearUnitCap are PHASE 2 PENDING and disabled in the UI.</item>
///   <item>Kill/Heal/InstantWin are LIVE.</item>
///   <item>The tab surfaces a <see cref="BattleControlTabViewModel.HasPhase2PendingAction"/>
///         flag + warning text when any button is non-LIVE.</item>
/// </list>
/// </summary>
public sealed class BattleControlTabViewModelCapabilityTests
{
    private static BattleControlTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new BattleControlTabViewModel(adapter);
    }

    [Fact]
    public void ToggleFreezeAi_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleFreezeAi.Badge.Should().Be("LIVE",
            "the Battle Control button now routes through SWFOC_SuspendAiLua");
        vm.ToggleFreezeAi.IsAllLive.Should().BeTrue();
    }

    [Fact]
    public void HealAllLocal_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HealAllLocal.Badge.Should().Be("LIVE");
        vm.HealAllLocal.IsAllLive.Should().BeTrue();
    }

    [Fact]
    public void KillAllEnemies_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.KillAllEnemies.Badge.Should().Be("LIVE",
            "SWFOC_ListTacticalUnits + SWFOC_KillUnit are both catalogued LIVE");
    }

    [Fact]
    public void InstantWin_AllPrimitivesLive_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.InstantWin.Badge.Should().Be("LIVE",
            "Heal + ListUnits + KillUnit are all LIVE — composite is uniformly LIVE");
    }

    [Fact]
    public void SetAndClearUnitCap_BothPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetUnitCap.Badge.Should().Be("PHASE 2 PENDING");
        vm.ClearUnitCap.Badge.Should().Be("PHASE 2 PENDING",
            "SWFOC_SetUnitCapOverride is the same primitive for both apply + clear");
    }

    [Fact]
    public void SetAndClearUnitCapCommands_DisabledUntilLiveHookExists()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetUnitCapCommand.CanExecute(null).Should().BeFalse();
        vm.ClearUnitCapCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void HasPhase2PendingAction_TrueWhenAnyButtonNonLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeTrue(
            "Set/ClearUnitCap are still PHASE 2 PENDING");
    }

    [Fact]
    public void Phase2PendingWarning_NamesEveryNonLiveButton()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Set unit cap override");
        warning.Should().Contain("Clear unit cap override");
        warning.Should().NotContain("Toggle freeze AI",
            "Freeze AI now uses the LIVE SuspendAiLua route");
        warning.Should().NotContain("Heal all local",
            "uniformly LIVE buttons must NOT appear in the warning");
        warning.Should().NotContain("Kill all enemies");
        warning.Should().NotContain("Instant win");
    }

    [Fact]
    public void Phase2PendingWarning_ExplainsMirrorOnlySemantics()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.Phase2PendingWarning.Should().Contain("disabled",
            "warning text must clarify that unsupported controls are not clickable");
        vm.Phase2PendingWarning.Should().Contain("live engine hook");
    }

    [Fact]
    public void AllActions_EnumeratesEveryActionInDeclaredOrder()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(6);
        vm.AllActions[0].Should().BeSameAs(vm.ToggleFreezeAi);
        vm.AllActions[1].Should().BeSameAs(vm.KillAllEnemies);
        vm.AllActions[2].Should().BeSameAs(vm.HealAllLocal);
        vm.AllActions[3].Should().BeSameAs(vm.InstantWin);
        vm.AllActions[4].Should().BeSameAs(vm.SetUnitCap);
        vm.AllActions[5].Should().BeSameAs(vm.ClearUnitCap);
    }
}
