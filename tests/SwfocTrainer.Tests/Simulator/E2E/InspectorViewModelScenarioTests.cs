using System.Globalization;
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
/// 2026-04-27 (iter 25 — Phase D continued) — VM-driven scenarios for the
/// InspectorTabViewModel. Validates that typing an obj-address into the
/// input box and clicking Refresh produces a snapshot from the simulator's
/// fake unit.
/// </summary>
public sealed class InspectorViewModelScenarioTests
{
    [Fact]
    public async Task RefreshCommand_PullsSnapshotForTypedObjAddress()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit
        {
            TypeName = "Han_Solo",
            OwnerSlot = 0,
            IsHero = true,
            MaxHull = 250,
            CurrentHull = 200,
            Invulnerable = true,
            DeathPrevented = true,
        };
        state.Units.Add(unit);

        using var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new InspectorTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));

        // Type the obj address into the bound input. The VM parses decimal
        // (or hex) and sets the SelectedUnit.
        vm.ObjAddrInput = unit.Id.ToString(CultureInfo.InvariantCulture);

        await AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        // VM exposes the snapshot through its derived properties.
        vm.SnapshotHull.Should().Be("200.00");
        vm.SnapshotOwner.Should().Be("0");
        vm.SnapshotInvuln.Should().Be("True");
        vm.SnapshotPreventDeath.Should().Be("True");

        // The simulator marked the unit as selected too.
        state.SelectedUnitId.Should().Be(unit.Id);
    }

    [Fact]
    public async Task RefreshCommand_HandlesUnknownAddressGracefully()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        using var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new InspectorTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));

        // Address that has no matching unit in the simulator.
        vm.ObjAddrInput = "9999";

        await AsyncCommandPump.PumpAsync(vm.RefreshCommand);

        // Snapshot should not have been populated (the bridge replied ERR).
        vm.SnapshotHull.Should().Be("—");
        vm.SnapshotOwner.Should().Be("—");
    }

    [Fact]
    public void ClearCommand_WipesSnapshotState()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        using var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new InspectorTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));

        vm.ObjAddrInput = "42";
        vm.ClearCommand.Execute(null);

        vm.ObjAddrInput.Should().BeOneOf("0", string.Empty);
    }
}
