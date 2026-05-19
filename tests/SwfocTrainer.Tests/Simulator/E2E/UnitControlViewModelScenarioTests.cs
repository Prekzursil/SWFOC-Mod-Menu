using System.Globalization;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using SwfocTrainer.Tests.V2;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 31 — Phase E continued) — VM-driven scenarios for the
/// UnitControlTabViewModel. Wires the 9-dependency constructor with REAL
/// services (GodMode, OneHitKill, UnitInspector, Hardpoint, EnhancedSpawn)
/// + V2UnitMutationDispatcher + V2FactionRegistry + V2Settings, all
/// pointed at the simulator's bridge.
/// </summary>
public sealed class UnitControlViewModelScenarioTests
{
    private static (SwfocSimulator sim, UnitControlTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // Pre-seed with a unit the operator can address.
        state.Units.Add(new FakeUnit
        {
            TypeName = "Rebel_Trooper_Squad",
            OwnerSlot = 0,
            MaxHull = 100,
            CurrentHull = 100,
        });
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var settings = new V2Settings();
        var godMode = new GodModeService(adapter, NullLogger<GodModeService>.Instance);
        var oneHitKill = new OneHitKillService(adapter, NullLogger<OneHitKillService>.Instance);
        var unitInspector = new UnitInspectorService(adapter, NullLogger<UnitInspectorService>.Instance);
        var hardpoints = new HardpointService(adapter, NullLogger<HardpointService>.Instance);
        var enhancedSpawn = new EnhancedSpawnService(adapter, NullLogger<EnhancedSpawnService>.Instance);
        var unitMutator = new V2UnitMutationDispatcher(adapter);
        var factionRegistry = new V2FactionRegistry();

        var vm = new UnitControlTabViewModel(
            adapter, settings, godMode, oneHitKill, unitInspector, hardpoints, enhancedSpawn,
            unitMutator, factionRegistry);
        return (sim, vm, state);
    }

    [Fact]
    public async Task EnableUnitInvuln_FlipsFlagOnAddressedUnit()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        var unit = state.Units[0];
        vm.AutoUseSelected = false; // address comes from ObjAddrInput, not SWFOC_GetSelectedUnit
                                    // UnitControlTabViewModel.TryParseObjAddr always parses as hex
                                    // (game obj addrs are 64-bit pointers shown as hex in the engine).
        vm.ObjAddrInput = unit.Id.ToString("X", CultureInfo.InvariantCulture);

        await AsyncCommandPump.PumpAsync(vm.EnableUnitInvulnCommand);

        unit.Invulnerable.Should().BeTrue();
    }

    [Fact]
    public async Task DisableUnitInvuln_ClearsFlagOnAddressedUnit()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        var unit = state.Units[0];
        unit.Invulnerable = true;
        vm.AutoUseSelected = false;
        // UnitControlTabViewModel.TryParseObjAddr always parses as hex
        // (game obj addrs are 64-bit pointers shown as hex in the engine).
        vm.ObjAddrInput = unit.Id.ToString("X", CultureInfo.InvariantCulture);

        await AsyncCommandPump.PumpAsync(vm.DisableUnitInvulnCommand);

        unit.Invulnerable.Should().BeFalse();
    }

    [Fact]
    public async Task EnablePreventDeath_FlipsDeathPreventedFlag()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        var unit = state.Units[0];
        vm.AutoUseSelected = false; // address comes from ObjAddrInput, not SWFOC_GetSelectedUnit
                                    // UnitControlTabViewModel.TryParseObjAddr always parses as hex
                                    // (game obj addrs are 64-bit pointers shown as hex in the engine).
        vm.ObjAddrInput = unit.Id.ToString("X", CultureInfo.InvariantCulture);

        await AsyncCommandPump.PumpAsync(vm.EnablePreventDeathCommand);

        unit.DeathPrevented.Should().BeTrue();
    }

    [Fact]
    public async Task SetUnitHull_UpdatesHullValue()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        var unit = state.Units[0];
        unit.CurrentHull = 100;
        vm.AutoUseSelected = false;
        // UnitControlTabViewModel.TryParseObjAddr always parses as hex
        // (game obj addrs are 64-bit pointers shown as hex in the engine).
        vm.ObjAddrInput = unit.Id.ToString("X", CultureInfo.InvariantCulture);
        vm.HullHpInput = "1500";

        await AsyncCommandPump.PumpAsync(vm.SetUnitHullCommand);

        unit.CurrentHull.Should().Be(1500f);
    }

    [Fact]
    public async Task EnableGodMode_FlipsGameStateAndAllUnits()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.EnableGodModeCommand);

        state.GodModeEnabled.Should().BeTrue();
        state.Units[0].Invulnerable.Should().BeTrue();
    }

    [Fact]
    public async Task DisableGodMode_ClearsFlags()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        // Turn on first.
        await AsyncCommandPump.PumpAsync(vm.EnableGodModeCommand);
        state.GodModeEnabled.Should().BeTrue();

        // Then off.
        await AsyncCommandPump.PumpAsync(vm.DisableGodModeCommand);
        state.GodModeEnabled.Should().BeFalse();
        state.Units[0].Invulnerable.Should().BeFalse();
    }

    [Fact]
    public async Task GetHardpoints_PopulatesLastInspectOrHardpoint()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        var unit = state.Units[0];
        vm.AutoUseSelected = false; // address comes from ObjAddrInput, not SWFOC_GetSelectedUnit
                                    // UnitControlTabViewModel.TryParseObjAddr always parses as hex
                                    // (game obj addrs are 64-bit pointers shown as hex in the engine).
        vm.ObjAddrInput = unit.Id.ToString("X", CultureInfo.InvariantCulture);

        await AsyncCommandPump.PumpAsync(vm.GetHardpointsCommand);

        // The simulator's HandleGetHardpoints returns 3 pieces: MAIN_GUN, SECONDARY_GUN, ENGINE.
        vm.LastInspectOrHardpoint.Should().NotBeNullOrWhiteSpace();
        vm.LastInspectOrHardpoint.Should().Contain("MAIN_GUN");
    }
}
