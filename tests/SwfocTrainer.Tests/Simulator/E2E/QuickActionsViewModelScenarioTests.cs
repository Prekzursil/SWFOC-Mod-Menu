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
/// 2026-04-27 (iter 53) — VM-driven scenarios for the new
/// <see cref="QuickActionsTabViewModel"/>. Each composite is verified
/// against the simulator's stateful flags so regressions in the bridge-call
/// sequencing are caught at unit-test speed.
/// </summary>
public sealed class QuickActionsViewModelScenarioTests
{
    private static (SwfocSimulator sim, QuickActionsTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // Seed a unit so HealAllLocal has something to act on.
        state.Units.Add(new FakeUnit
        {
            TypeName = "Rebel_Trooper_Squad",
            OwnerSlot = 0,
            MaxHull = 100,
            CurrentHull = 30,
        });
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new QuickActionsTabViewModel(adapter), state);
    }

    [Fact]
    public async Task OperatorGodMode_FlipsAllThreeStateFlagsAndHealsLocal()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.OperatorGodModeCommand);

        state.GodModeEnabled.Should().BeTrue("composite call 1");
        state.MaxCredits.Should().Be(-1, "composite call 3 — UncapCredits sets MaxCredits to -1");
        state.Units[0].CurrentHull.Should().Be(state.Units[0].MaxHull,
            "composite call 2 — HealAllLocal restored the local unit to full");
        vm.LastStatus.Should().StartWith("Operator god mode: 3/3 OK");
    }

    [Fact]
    public async Task DrainEnemies_ZeroesAllNonLocalCreditsViaComposite()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        var startingCredits = state.GetPlayer(1)!.Credits;
        startingCredits.Should().BeGreaterThan(0, "fixture pre-condition");

        await AsyncCommandPump.PumpAsync(vm.DrainEnemiesCommand);

        state.GetPlayer(0)!.Credits.Should().BeGreaterThan(0,
            "operator's own credits preserved");
        state.GetPlayer(1)!.Credits.Should().Be(0, "EMPIRE drained");
        state.GetPlayer(2)!.Credits.Should().Be(0, "UNDERWORLD drained");
        vm.LastStatus.Should().StartWith("Drain enemies: 1/1 OK");
    }

    [Fact]
    public async Task RevealGalaxy_FlipsRevealedFlagsViaComposite()
    {
        // Switch to galactic state so the planets list is non-empty.
        var galacticState = FakeGameState.NewGalacticCampaign();
        using var sim = new SwfocSimulator(galacticState);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new QuickActionsTabViewModel(adapter);

        galacticState.Planets.Should().NotBeEmpty();
        galacticState.Planets.Should().AllSatisfy(p => p.IsRevealed.Should().BeFalse());

        await AsyncCommandPump.PumpAsync(vm.RevealGalaxyCommand);

        galacticState.Planets.Should().AllSatisfy(p => p.IsRevealed.Should().BeTrue(
            "RevealAll(1) is the first call in the composite"));
        vm.LastStatus.Should().StartWith("Reveal galaxy: 3/3 OK");
    }

    [Fact]
    public async Task ResetToggles_TurnsOffLiveGlobalTogglesOnly()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        // Turn everything ON first so we can verify Reset turns them OFF.
        state.GodModeEnabled = true;
        state.OneHitKillEnabled = true;
        state.GlobalCreditsFreeze = true;
        state.AiEnabled = false; // Suspend_AI(0) resumes AI

        await AsyncCommandPump.PumpAsync(vm.ResetTogglesCommand);

        state.GodModeEnabled.Should().BeFalse();
        state.OneHitKillEnabled.Should().BeFalse();
        state.GlobalCreditsFreeze.Should().BeFalse();
        state.AiEnabled.Should().BeTrue("Suspend_AI(0) resumes AI");
        vm.LastStatus.Should().StartWith("Reset toggles: 4/4 OK");
    }
}
