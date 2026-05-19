using System.Linq;
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
/// 2026-04-27 (iter 27 — Phase D continued) — VM-driven scenarios for the
/// GalacticTabViewModel. Validates planet refresh / change-owner / reveal-all
/// / diplomacy paths against the simulator's stateful planet+diplomacy
/// model (now using faction-string ownership to match the real
/// BridgeGalacticDispatcher wire format).
/// </summary>
public sealed class GalacticViewModelScenarioTests
{
    private static (SwfocSimulator sim, GalacticTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new GalacticTabViewModel(adapter, new V2UnitMutationDispatcher(adapter)), state);
    }

    [Fact]
    public async Task RefreshPlanets_PopulatesBoundCollection()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.RefreshPlanetsCommand);

        vm.Planets.Should().NotBeEmpty();
        vm.Planets.Should().Contain(p => p.PlanetId == "Yavin");
        vm.Planets.Should().Contain(p => p.PlanetId == "Coruscant");
    }

    [Fact]
    public async Task ChangeOwner_IsDisabledUntilLiveHookExists()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.RefreshPlanetsCommand);
        vm.SelectedPlanetId = "Hoth";
        vm.NewOwnerFaction = "EMPIRE";

        vm.ChangeOwnerCommand.CanExecute(null).Should().BeFalse();

        var hoth = state.Planets.First(p => p.Name == "Hoth");
        hoth.OwnerFaction.Should().Be("REBEL");
        hoth.OwnerSlot.Should().Be(0, "the disabled replay-only owner change must not mutate state");
    }

    [Fact]
    public async Task ToggleRevealAll_FlipsIsRevealedFlagOnEveryPlanet()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        state.Planets.All(p => !p.IsRevealed).Should().BeTrue("planets start hidden");

        await AsyncCommandPump.PumpAsync(vm.ToggleRevealAllCommand);

        state.Planets.All(p => p.IsRevealed).Should().BeTrue("RevealAll flips every planet");
    }

    [Fact]
    public async Task ChangeOwnerConvertCommand_IsDisabledUntilLiveHookExists()
    {
        // 2026-04-27 (iter 33) — VM-level proof that the operator clicking
        // "Flip & convert" routes all the way through:
        //   GalacticTabViewModel.ChangeOwnerConvertCommand →
        //     GalacticTabState.ChangePlanetOwnerWithModeAsync(Convert) →
        //       BridgeGalacticDispatcher.ChangePlanetOwnerWithModeAsync →
        //         SWFOC_ChangePlanetOwnerWithMode('...', '...', 'convert') →
        //           SwfocSimulator.HandleChangePlanetOwnerWithMode →
        //             FakeUnit.OwnerSlot mutated for every garrison unit.
        var (sim, vm, state) = NewSession();
        using var _ = sim;
        // Place a Rebel garrison on Hoth (Hoth is REBEL-owned in seed).
        for (var i = 0; i < 3; i++)
        {
            state.Units.Add(new FakeUnit
            {
                TypeName = "Rebel_Trooper_Squad",
                OwnerSlot = 0,
                OnPlanet = "Hoth",
            });
        }

        await AsyncCommandPump.PumpAsync(vm.RefreshPlanetsCommand);
        vm.SelectedPlanetId = "Hoth";
        vm.NewOwnerFaction = "EMPIRE";

        vm.ChangeOwnerConvertCommand.CanExecute(null).Should().BeFalse();

        var hoth = state.Planets.First(p => p.Name == "Hoth");
        hoth.OwnerFaction.Should().Be("REBEL");
        // Disabled owner-convert must leave the seeded Rebel garrison unchanged.
        var garrison = state.Units.Where(u => u.OnPlanet == "Hoth").ToList();
        garrison.Should().HaveCount(3);
        garrison.All(u => u.OwnerSlot == 0).Should().BeTrue("disabled convert must not re-team the garrison");
    }

    [Fact]
    public async Task ChangeOwnerPureKickCommand_IsDisabledUntilLiveHookExists()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;
        for (var i = 0; i < 4; i++)
        {
            state.Units.Add(new FakeUnit
            {
                TypeName = "Rebel_Trooper_Squad",
                OwnerSlot = 0,
                OnPlanet = "Hoth",
            });
        }

        await AsyncCommandPump.PumpAsync(vm.RefreshPlanetsCommand);
        vm.SelectedPlanetId = "Hoth";
        vm.NewOwnerFaction = "EMPIRE";

        vm.ChangeOwnerPureKickCommand.CanExecute(null).Should().BeFalse();

        state.Units.Where(u => u.OnPlanet == "Hoth").Should().HaveCount(4,
            "disabled pure-kick must not remove the garrison");
    }

    [Fact]
    public async Task SpawnAsStoryArrivalCommand_IsDisabledUntilLiveHookExists()
    {
        // 2026-04-27 (iter 34) — VM-level proof that "Story-arrival spawn"
        // routes click → BridgeGalacticDispatcher.SpawnAsStoryArrivalAsync
        // → SWFOC_SpawnAsStoryArrival(...) → simulator state.
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        // Seed a recognized type so the simulator accepts the spawn.
        state.KnownTypeNames.Add("Rebel_T2A_Tank");
        var initialUnits = state.Units.Count;

        await AsyncCommandPump.PumpAsync(vm.RefreshPlanetsCommand);
        vm.StoryArrivalTypeId = "Rebel_T2A_Tank";
        vm.StoryArrivalPlanetId = "Yavin";
        vm.StoryArrivalFaction = "REBEL";

        vm.SpawnAsStoryArrivalCommand.CanExecute(null).Should().BeFalse();

        state.Units.Count.Should().Be(initialUnits, "disabled story-arrival spawn must not mutate state");
        state.EventQueue.Should().NotContain(e =>
            e.StartsWith("STORY_ARRIVAL:Rebel_T2A_Tank@Yavin"));
    }

    [Fact]
    public void SpawnAsStoryArrivalCommand_RemainsDisabledForEmptyType()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.StoryArrivalTypeId = ""; // empty
        vm.StoryArrivalPlanetId = "Yavin";
        vm.StoryArrivalFaction = "REBEL";
        var initialUnits = state.Units.Count;

        vm.SpawnAsStoryArrivalCommand.CanExecute(null).Should().BeFalse();

        state.Units.Count.Should().Be(initialUnits);
    }

    [Fact]
    public async Task SetDiplomacy_StoresFactionPairRelation()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.DiplomacySlotA = "REBEL";
        vm.DiplomacySlotB = "EMPIRE";
        vm.DiplomacyRelation = SwfocTrainer.Core.V2Vm.DiplomacyRelation.Allied;

        await AsyncCommandPump.PumpAsync(vm.SetDiplomacyCommand);

        state.Diplomacy.Should().ContainKey("REBEL:EMPIRE");
        state.Diplomacy["REBEL:EMPIRE"].Should().Be("Allied");
    }
}
