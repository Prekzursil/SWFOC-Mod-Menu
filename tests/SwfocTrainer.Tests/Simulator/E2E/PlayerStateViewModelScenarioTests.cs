using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.App;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 29 — Phase E) — VM-driven scenarios for the central
/// <c>PlayerStateTabViewModel</c>, the most-trafficked tab in the editor.
/// Wires up REAL services (<c>EconomyService</c>, <c>HeroRespawnService</c>,
/// <c>FactionSwitchService</c>) against the simulator's bridge — every
/// service operation runs through actual production Lua emission code.
/// </summary>
/// <remarks>
/// <para>
/// Earlier phases tested the simulator's handlers directly against
/// <c>SendRawAsync</c>. Phase E goes one layer up: the real services are
/// the things the live editor ships with, so a mismatch between the
/// service's Lua emission and the bridge's parser is the most likely
/// shipping bug. These tests catch that whole class of issue.
/// </para>
/// </remarks>
[Collection(RuntimeModeSerialCollection.Name)]
public sealed class PlayerStateViewModelScenarioTests
{
    private const string ProfileId = "default";

    private static (SwfocSimulator sim, PlayerStateTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        var adapter = new V2BridgeAdapter(pipe);

        // Real services — same code paths the editor uses against
        // powrprof.dll, just pointed at our simulator pipe.
        var economy = new EconomyService(adapter, NullLogger<EconomyService>.Instance);
        var heroRespawn = new HeroRespawnService(adapter, NullLogger<HeroRespawnService>.Instance);
        var factionSwitch = new FactionSwitchService(adapter, NullLogger<FactionSwitchService>.Instance);
        var unitMutator = new V2UnitMutationDispatcher(adapter);
        var factionRegistry = new V2FactionRegistry();
        var settings = new V2Settings();

        var vm = new PlayerStateTabViewModel(
            adapter, settings, economy, heroRespawn, factionSwitch, unitMutator, factionRegistry);
        return (sim, vm, state);
    }

    [Fact]
    public async Task RefreshSlotMap_PopulatesFactionRegistry_AndSlotLabels()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        // Refresh fires SWFOC_GetAllPlayers and parses the rows into
        // (slot, faction, credits, isHuman, isAi, isLocal, unitCount).
        // The simulator returns rows for slots 0/1/2/7 of the seeded skirmish.
        await vm.RefreshSlotMapAsync();

        // Slot entries should be re-labelled with the live faction names.
        // The dropdown shows DisplayLabel like "Slot 0 — REBEL".
        vm.Slots.Should().NotBeEmpty();
        vm.Slots.Any(entry => entry.FactionName.Contains("REBEL", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("RefreshSlotMap must populate the REBEL faction onto its slot");
        vm.Slots.Any(entry => entry.FactionName.Contains("EMPIRE", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("RefreshSlotMap must populate the EMPIRE faction onto its slot");
    }

    [Fact]
    public async Task SwitchToSlot_FlipsLocalHumanInSimulator()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await vm.RefreshSlotMapAsync();

        // Operator selects slot 1 (EMPIRE) and clicks Switch.
        vm.SelectedSlot = 1;
        await AsyncCommandPump.PumpAsync(vm.SwitchToSlotCommand);

        // Simulator state: slot 0 should no longer be the local human.
        state.GetPlayer(0)!.IsLocal.Should().BeFalse();
        state.GetPlayer(1)!.IsLocal.Should().BeTrue();
        state.GetPlayer(1)!.IsHuman.Should().BeTrue();
    }

    [Fact]
    public async Task NullAiBrain_StripsAiFromTargetSlot()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await vm.RefreshSlotMapAsync();
        state.GetPlayer(1)!.HasAiBrain.Should().BeTrue("EMPIRE starts as AI");

        vm.SelectedSlot = 1;
        await AsyncCommandPump.PumpAsync(vm.NullAiBrainCommand);

        state.GetPlayer(1)!.HasAiBrain.Should().BeFalse();
    }

    [Fact]
    public async Task AttachAiBrain_RestoresAiToTargetSlot()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        // Slot 0 starts as human (no AI brain). Attaching AI brain to it
        // simulates "give me back to the AI so I can spectate".
        state.GetPlayer(0)!.HasAiBrain.Should().BeFalse();

        await vm.RefreshSlotMapAsync();
        vm.SelectedSlot = 0;
        await AsyncCommandPump.PumpAsync(vm.AttachAiBrainCommand);

        state.GetPlayer(0)!.HasAiBrain.Should().BeTrue();
    }
}
