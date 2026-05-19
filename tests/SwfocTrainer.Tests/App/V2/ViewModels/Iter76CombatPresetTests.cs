using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 76) — pins the Combat scalar preset values for
/// Easy / Normal / Hard / Hardcore. Forward-looking: the underlying
/// SWFOC_SetDamageMultiplier / SWFOC_SetFireRate are PHASE 2 PENDING,
/// but the bound multipliers + simulator wire-format is verifiable
/// today.
/// </summary>
public sealed class Iter76CombatPresetTests
{
    private static (SwfocSimulator sim, CombatTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new CombatTabViewModel(adapter, new V2UnitMutationDispatcher(adapter)));
    }

    [Fact]
    public async Task ApplyEasyPreset_SetsHalfDamageAndThreeQuarterFireRate()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        await vm.ApplyDifficultyPresetAsync(damageMult: 0.5f, fireRateMult: 0.75f);

        vm.DamageMultiplier.Should().Be(0.5f);
        vm.FireRateMultiplier.Should().Be(0.75f);
    }

    [Fact]
    public async Task ApplyNormalPreset_ResetsMultipliersToOne()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        // Set non-default values first to verify the preset truly resets.
        vm.DamageMultiplier = 3.7f;
        vm.FireRateMultiplier = 0.1f;

        await vm.ApplyDifficultyPresetAsync(damageMult: 1.0f, fireRateMult: 1.0f);

        vm.DamageMultiplier.Should().Be(1.0f);
        vm.FireRateMultiplier.Should().Be(1.0f);
    }

    [Fact]
    public async Task ApplyHardPreset_SetsOneAndAHalfDamageAndStandardFireRate()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        await vm.ApplyDifficultyPresetAsync(damageMult: 1.5f, fireRateMult: 1.25f);

        vm.DamageMultiplier.Should().Be(1.5f);
        vm.FireRateMultiplier.Should().Be(1.25f);
    }

    [Fact]
    public async Task ApplyHardcorePreset_SetsTwoPointFiveDamageAndHalfMoreFireRate()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        await vm.ApplyDifficultyPresetAsync(damageMult: 2.5f, fireRateMult: 1.5f);

        vm.DamageMultiplier.Should().Be(2.5f);
        vm.FireRateMultiplier.Should().Be(1.5f);
    }

    [Fact]
    public void PresetCommands_AllExposed()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        vm.ApplyEasyPresetCommand.Should().NotBeNull();
        vm.ApplyNormalPresetCommand.Should().NotBeNull();
        vm.ApplyHardPresetCommand.Should().NotBeNull();
        vm.ApplyHardcorePresetCommand.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyDifficultyPreset_FiresBothSetCommands_RecordsTwoActivityEntries()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        // Re-create VM around the new adapter so we can inspect it.
        var localVm = new CombatTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));
        // Set the obj_addr first so SetUnitShield doesn't get fired
        // (the preset doesn't touch shield, but the existing setter
        // path is consistent).
        localVm.SelectedObjAddr = 0x1000;

        await localVm.ApplyDifficultyPresetAsync(damageMult: 2.0f, fireRateMult: 0.8f);

        // Both Set commands fire — adapter records 2 entries.
        adapter.RecentCalls.Should().HaveCount(2);
        adapter.RecentCalls.Should().Contain(e => e.LuaCommand.Contains("SetDamageMultiplier"));
        adapter.RecentCalls.Should().Contain(e => e.LuaCommand.Contains("SetFireRate"));
    }

    [Fact]
    public async Task ApplyDifficultyPreset_DoesNotChangeShieldOrTargetFilter()
    {
        // Presets only touch the two multipliers — operator still
        // owns ShieldValue / TargetFilterBitmask manually.
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.ShieldValue = 999f;
        vm.TargetFilterBitmask = 0x3;

        await vm.ApplyDifficultyPresetAsync(damageMult: 1.5f, fireRateMult: 1.25f);

        vm.ShieldValue.Should().Be(999f, "presets must not touch shield value");
        vm.TargetFilterBitmask.Should().Be(0x3, "presets must not touch target filter");
    }
}
