using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// VM-driven Battle Control scenarios against the named-pipe simulator.
/// These tests pin operator-facing buttons to live bridge semantics without
/// launching the real game.
/// </summary>
public sealed class BattleControlViewModelScenarioTests
{
    private static (SwfocSimulator Sim, BattleControlTabViewModel Vm, FakeGameState State) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new BattleControlTabViewModel(adapter), state);
    }

    [Fact]
    public async Task ToggleFreezeAi_UsesLiveSuspendAiRoute()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        state.AiEnabled.Should().BeTrue();

        await AsyncCommandPump.PumpAsync(vm.ToggleFreezeAiCommand);

        state.AiEnabled.Should().BeFalse();
        vm.IsFreezeAiEnabled.Should().BeTrue();

        await AsyncCommandPump.PumpAsync(vm.ToggleFreezeAiCommand);

        state.AiEnabled.Should().BeTrue();
        vm.IsFreezeAiEnabled.Should().BeFalse();
    }
}
