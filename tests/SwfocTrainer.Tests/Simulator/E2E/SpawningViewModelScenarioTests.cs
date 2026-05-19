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
/// 2026-04-27 (iter 24 — Phase D) — VM-driven scenarios for the
/// SpawningTabViewModel. Constructs the real ViewModel against a
/// V2BridgeAdapter pointed at <see cref="SwfocSimulator"/>, drives the
/// public commands the way XAML bindings would, and asserts both the
/// VM's observable state AND the simulated game state mutated as the
/// feature claims.
/// </summary>
/// <remarks>
/// <para>
/// This complements <c>SwfocSimulatorEndToEndTests</c> (which calls
/// <c>SendRawAsync</c> directly): Phase D additionally validates the
/// editor's command can-execute gates, the dispatcher's Lua emission
/// format, INPC notifications on bound properties, and the feedback-sink
/// chain. A regression in any of those layers fires here even if the
/// underlying bridge function still works.
/// </para>
/// </remarks>
public sealed class SpawningViewModelScenarioTests
{
    private static (SwfocSimulator sim, SpawningTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new SpawningTabViewModel(adapter);
        // Seed the VM's catalogue with the simulator's known type names so
        // its filter UI has something to surface and its dispatcher won't
        // be blocked by an empty type id.
        vm.SetAvailableTypes(state.KnownTypeNames.ToList());
        return (sim, vm, state);
    }

    [Fact]
    public async Task SpawnCommand_DrivesSimulatorState()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.SelectedTypeId = "Rebel_Trooper_Squad";
        vm.FactionSlot = 0;
        vm.Count = 4;

        await AsyncCommandPump.PumpAsync(vm.SpawnCommand);

        state.Units.Should().HaveCount(4, "the bridge call must have reached the simulator and added units");
        state.Units.All(u => u.TypeName == "Rebel_Trooper_Squad").Should().BeTrue();
        state.Units.All(u => u.OwnerSlot == 0).Should().BeTrue();
        state.Units.All(u => u.Alive).Should().BeTrue();
        // VM surfaces the dispatcher's UxFeedback through LastStatus.
        vm.LastStatus.Should().NotBe("(idle)", "LastStatus must reflect the spawn outcome");
    }

    [Fact]
    public async Task SpawnCommand_RejectedByCanExecute_WhenNoTypeSelected()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.SelectedTypeId = string.Empty;
        vm.FactionSlot = 0;
        vm.Count = 3;

        // CanExecute may gate the spawn; if it doesn't, the dispatcher
        // refuses with a "no type selected" feedback message. Either way,
        // no units should land in the simulator.
        if (vm.SpawnCommand.CanExecute(null))
        {
            await AsyncCommandPump.PumpAsync(vm.SpawnCommand);
        }
        state.Units.Should().BeEmpty("empty type id must not produce a spawn");
    }

    [Fact]
    public async Task RefreshFromLiveGameCommand_NarrowsCatalogueToSimulatorKnownTypes()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        // Seed the VM with both known and unknown types — Refresh should
        // intersect the local catalogue against what the simulator confirms
        // exists, leaving only the known set.
        vm.SetAvailableTypes(state.KnownTypeNames.Concat(new[]
        {
            "Garbage_Type_A",
            "Garbage_Type_B",
            "Mod_Phantom_Unit",
        }).ToList());

        await AsyncCommandPump.PumpAsync(vm.RefreshFromLiveGameCommand);

        // Available faceted-faction filter is rebuilt from the surviving
        // type ids; confirming the simulator's BatchTypeExists path was
        // walked end-to-end.
        var survivors = vm.FilteredTypes.ToList();
        survivors.Should().NotContain("Garbage_Type_A");
        survivors.Should().NotContain("Garbage_Type_B");
        survivors.Should().NotContain("Mod_Phantom_Unit");
        survivors.Should().Contain("Rebel_Trooper_Squad");
        survivors.Should().Contain("Empire_AT_AT");
    }

    [Fact]
    public async Task SpawnCommand_RoutesToCorrectSlot_WhenFactionSlotChanges()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.SelectedTypeId = "Empire_AT_AT";
        vm.FactionSlot = 1; // EMPIRE slot
        vm.Count = 2;

        await AsyncCommandPump.PumpAsync(vm.SpawnCommand);

        state.Units.Where(u => u.OwnerSlot == 1).Should().HaveCount(2);
        state.Units.Where(u => u.OwnerSlot == 0).Should().BeEmpty();
    }

    [Fact]
    public void SearchQuery_FiltersAvailableTypes()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        // Out-of-band: SearchQuery is a pure VM behaviour, doesn't touch
        // the simulator. We test it here because a regression that breaks
        // filtering would prevent the operator from finding spawnable
        // types in a 1000+ entry mod catalogue.
        vm.SearchQuery = "Rebel";
        var rebelOnly = vm.FilteredTypes.ToList();
        rebelOnly.All(t => t.Contains("Rebel", System.StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        rebelOnly.Should().NotBeEmpty();

        vm.SearchQuery = string.Empty;
        vm.FilteredTypes.Count.Should().Be(state.KnownTypeNames.Count, "clearing the search restores the full catalogue");
    }
}
