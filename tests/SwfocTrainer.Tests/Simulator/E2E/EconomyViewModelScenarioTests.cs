using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 26 — Phase D continued) — VM-driven scenarios for the
/// EconomyTabViewModel. Validates that setting credits / tech / income
/// multiplier through the operator UI mutates the simulated game state
/// the way the live engine would.
/// </summary>
public sealed class EconomyViewModelScenarioTests
{
    private static (SwfocSimulator sim, EconomyTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new EconomyTabViewModel(adapter), state);
    }

    [Fact]
    public async Task SetCredits_RoutesToCorrectSlot()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.Slot = 1;
        vm.CreditsAmount = 75000;
        await AsyncCommandPump.PumpAsync(vm.SetCreditsCommand);

        state.GetPlayer(1)!.Credits.Should().Be(75000);
        state.GetPlayer(0)!.Credits.Should().Be(5000, "other slots untouched");
    }

    [Fact]
    public async Task DrainEnemy_PreservesOperatorSlotAndZeroesOthers()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.Slot = 0;
        await AsyncCommandPump.PumpAsync(vm.DrainEnemyCommand);

        state.GetPlayer(0)!.Credits.Should().Be(5000, "operator's own slot preserved");
        state.GetPlayer(1)!.Credits.Should().Be(0);
        state.GetPlayer(2)!.Credits.Should().Be(0);
    }

    [Fact]
    public async Task UncapCredits_FlipsMaxToMinusOne()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        state.MaxCredits.Should().Be(999999);
        await AsyncCommandPump.PumpAsync(vm.UncapCreditsCommand);
        state.MaxCredits.Should().Be(-1);
    }

    [Fact]
    public async Task SetCreditsFreezeOn_UsesLiveGlobalFreezeFlag()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.SetCreditsFreezeOnCommand);
        state.GlobalCreditsFreeze.Should().BeTrue();
    }

    [Fact]
    public void ToggleFreeBuild_IsDisabledUntilLiveHookExists()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.ToggleFreeBuildCommand.CanExecute(null).Should().BeFalse();
        state.FreeBuildEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task SetTech_RoutesToCorrectSlot()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.Slot = 0;
        vm.TechLevel = 5;
        await AsyncCommandPump.PumpAsync(vm.SetTechCommand);

        // The dispatcher emits SWFOC_SetTechForSlot when slot >= 0.
        // Simulator drops the value into PerSlotTechLevel via SetTechForSlot
        // OR enqueues a TECH_SET event when only the legacy form is hit.
        // Either way we should observe one of those mutations.
        var techWasSet = state.PerSlotTechLevel.ContainsKey(0)
            || state.EventQueue.Count > 0;
        techWasSet.Should().BeTrue("the SetTech command must mutate observable state");
    }

    [Fact]
    public void SetIncomeMultiplier_IsDisabledUntilLivePerSlotHookExists()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.Slot = 1;
        vm.IncomeMultiplier = 3.5f;

        vm.SetIncomeMultCommand.CanExecute(null).Should().BeFalse();
        state.PerSlotIncomeMultiplier.Should().NotContainKey(1);
        state.PerFactionIncome.Should().BeEmpty();
    }
}
