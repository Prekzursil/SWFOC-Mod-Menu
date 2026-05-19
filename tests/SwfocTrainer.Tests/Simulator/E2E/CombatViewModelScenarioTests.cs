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
/// CombatTabViewModel. Validates that toggling GodMode / OHK / area damage
/// from the operator UI actually flips the simulated game-state flags
/// AND applies per-unit invulnerability where the engine semantics demand
/// it.
/// </summary>
public sealed class CombatViewModelScenarioTests
{
    private static (SwfocSimulator sim, CombatTabViewModel vm, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // Seed with a few units so god-mode invulnerization has something to do.
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1 });
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new CombatTabViewModel(adapter, new V2UnitMutationDispatcher(adapter)), state);
    }

    [Fact]
    public async Task ToggleGodMode_FlipsStateFlagAndAlsoUnitInvulnerability()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.ToggleGodModeCommand);
        state.GodModeEnabled.Should().BeTrue();
        state.Units.Should().AllSatisfy(u => u.Invulnerable.Should().BeTrue());

        await AsyncCommandPump.PumpAsync(vm.ToggleGodModeCommand);
        state.GodModeEnabled.Should().BeFalse();
        state.Units.Should().AllSatisfy(u => u.Invulnerable.Should().BeFalse());
    }

    [Fact]
    public async Task ToggleOhk_FlipsStateFlag()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.ToggleOhkCommand);
        state.OneHitKillEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleAreaDamage_FlipsStateFlag()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        await AsyncCommandPump.PumpAsync(vm.ToggleAreaDamageCommand);
        state.AreaDamageEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SetDamageMultiplier_AppliesToOwningSlotUnits()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.Slot = 0;
        vm.DamageMultiplier = 4f;
        await AsyncCommandPump.PumpAsync(vm.SetDamageMultiplierCommand);

        state.PerSlotDamageMultiplier.Should().ContainKey(0);
        state.PerSlotDamageMultiplier[0].Should().Be(4f);
        state.Units.Find(u => u.OwnerSlot == 0)!.DamageScalar.Should().Be(4f);
        state.Units.Find(u => u.OwnerSlot == 1)!.DamageScalar.Should().Be(1f, "other slots untouched");
    }

    [Fact]
    public async Task SetFireRate_AppliesToOwningSlotUnits()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.Slot = 1;
        vm.FireRateMultiplier = 2.5f;
        await AsyncCommandPump.PumpAsync(vm.SetFireRateCommand);

        state.PerSlotFireRateMultiplier[1].Should().Be(2.5f);
        state.Units.Find(u => u.OwnerSlot == 1)!.FireRateScalar.Should().Be(2.5f);
    }

    [Fact]
    public async Task SetTargetFilter_StoresPerSlotMaskFromTickboxes()
    {
        var (sim, vm, state) = NewSession();
        using var _ = sim;

        vm.Slot = 0;
        vm.FilterIncludesEnemy = true;
        vm.FilterIncludesFriendly = false;
        vm.FilterIncludesNeutral = false;
        await AsyncCommandPump.PumpAsync(vm.SetTargetFilterCommand);

        state.PerSlotTargetFilter.Should().ContainKey(0);
        // Bitmask is non-zero because at least one filter dimension was set.
        state.PerSlotTargetFilter[0].Should().NotBe(0);
    }
}
