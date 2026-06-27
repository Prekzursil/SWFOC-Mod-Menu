using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 77) — pins the Speed per-faction and per-unit preset
/// values. Forward-looking: SWFOC_SetPerFactionSpeedMultiplier and
/// SWFOC_SetUnitSpeed are PHASE 2 PENDING, but the bound multipliers +
/// simulator wire-format adoption is verifiable today. Sibling of
/// Iter76CombatPresetTests.
/// </summary>
public sealed class Iter77SpeedPresetTests
{
    private static (SwfocSimulator sim, SpeedTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new SpeedTabViewModel(adapter));
    }

    // -----------------------------------------------------------------
    // Per-faction presets
    // -----------------------------------------------------------------

    [Fact]
    public async Task ApplyFactionSnailPreset_SetsQuarterSpeedMultiplier()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.FactionSlot = 0;

        await vm.ApplyFactionPresetAsync(0.25f);

        vm.FactionMoveSpeedMultiplier.Should().Be(0.25f);
    }

    [Fact]
    public async Task ApplyFactionSlowPreset_SetsHalfSpeedMultiplier()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.FactionSlot = 0;

        await vm.ApplyFactionPresetAsync(0.5f);

        vm.FactionMoveSpeedMultiplier.Should().Be(0.5f);
    }

    [Fact]
    public async Task ApplyFactionNormalPreset_ResetsMultiplierToOne()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.FactionSlot = 0;
        // Set non-default first to verify the preset truly resets.
        vm.FactionMoveSpeedMultiplier = 7.3f;

        await vm.ApplyFactionPresetAsync(1.0f);

        vm.FactionMoveSpeedMultiplier.Should().Be(1.0f);
    }

    [Fact]
    public async Task ApplyFactionFastPreset_SetsDoubleSpeedMultiplier()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.FactionSlot = 0;

        await vm.ApplyFactionPresetAsync(2.0f);

        vm.FactionMoveSpeedMultiplier.Should().Be(2.0f);
    }

    // -----------------------------------------------------------------
    // Per-unit presets
    // -----------------------------------------------------------------

    [Fact]
    public async Task ApplyUnitSlowPreset_SetsTwoPointFiveSpeed()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.SelectedObjAddr = 0x1000;

        await vm.ApplyUnitPresetAsync(2.5f);

        vm.UnitSpeed.Should().Be(2.5f);
    }

    [Fact]
    public async Task ApplyUnitNormalPreset_ResetsUnitSpeedToFive()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.SelectedObjAddr = 0x1000;
        vm.UnitSpeed = 99.0f;

        await vm.ApplyUnitPresetAsync(5.0f);

        vm.UnitSpeed.Should().Be(5.0f);
    }

    [Fact]
    public async Task ApplyUnitFastPreset_SetsTenSpeed()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.SelectedObjAddr = 0x1000;

        await vm.ApplyUnitPresetAsync(10.0f);

        vm.UnitSpeed.Should().Be(10.0f);
    }

    [Fact]
    public async Task ApplyUnitSprintPreset_SetsTwentySpeed()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.SelectedObjAddr = 0x1000;

        await vm.ApplyUnitPresetAsync(20.0f);

        vm.UnitSpeed.Should().Be(20.0f);
    }

    // -----------------------------------------------------------------
    // Command exposure & wire-format checks
    // -----------------------------------------------------------------

    [Fact]
    public void PresetCommands_AllExposed()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        vm.ApplyFactionSnailCommand.Should().NotBeNull();
        vm.ApplyFactionSlowCommand.Should().NotBeNull();
        vm.ApplyFactionNormalCommand.Should().NotBeNull();
        vm.ApplyFactionFastCommand.Should().NotBeNull();

        vm.ApplyUnitSlowCommand.Should().NotBeNull();
        vm.ApplyUnitNormalCommand.Should().NotBeNull();
        vm.ApplyUnitFastCommand.Should().NotBeNull();
        vm.ApplyUnitSprintCommand.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyFactionPreset_FiresPerFactionSpeedCommand_RecordsSingleEntry()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var localVm = new SpeedTabViewModel(adapter);
        localVm.FactionSlot = 1;

        await localVm.ApplyFactionPresetAsync(2.0f);

        adapter.RecentCalls.Should().HaveCount(1);
        adapter.RecentCalls[0].LuaCommand.Should().Contain("SetPerFactionSpeedMultiplier");
    }

    [Fact]
    public async Task ApplyUnitPreset_FiresSetUnitSpeedCommand_RecordsSingleEntry()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var localVm = new SpeedTabViewModel(adapter);
        localVm.SelectedObjAddr = 0x1000;

        await localVm.ApplyUnitPresetAsync(10.0f);

        adapter.RecentCalls.Should().HaveCount(1);
        adapter.RecentCalls[0].LuaCommand.Should().Contain("SetUnitSpeed");
    }

    [Fact]
    public async Task ApplyFactionPreset_DoesNotChangeGlobalGameSpeedOrUnitSpeed()
    {
        // Per-faction preset is scoped — it must not bleed into the
        // global-game-speed surface or the per-unit surface. Operator
        // still owns those independently.
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.FactionSlot = 0;
        vm.GlobalGameSpeed = 0.5f;
        vm.UnitSpeed = 7.0f;

        await vm.ApplyFactionPresetAsync(2.0f);

        vm.GlobalGameSpeed.Should().Be(0.5f, "faction preset must not touch global game speed");
        vm.UnitSpeed.Should().Be(7.0f, "faction preset must not touch per-unit speed");
    }

    [Fact]
    public async Task ApplyUnitPreset_DoesNotChangeGlobalGameSpeedOrFactionMultiplier()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.SelectedObjAddr = 0x1000;
        vm.GlobalGameSpeed = 0.5f;
        vm.FactionMoveSpeedMultiplier = 1.5f;

        await vm.ApplyUnitPresetAsync(10.0f);

        vm.GlobalGameSpeed.Should().Be(0.5f, "unit preset must not touch global game speed");
        vm.FactionMoveSpeedMultiplier.Should().Be(1.5f, "unit preset must not touch per-faction multiplier");
    }
}
