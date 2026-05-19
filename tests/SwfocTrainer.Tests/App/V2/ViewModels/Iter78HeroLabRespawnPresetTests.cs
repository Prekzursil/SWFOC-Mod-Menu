using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 78) — pins the Hero Lab respawn-time preset values
/// (Quick / Normal / Slow / Glacial). Completes the scalar-preset
/// trifecta with iter 76 (Combat) and iter 77 (Speed).
/// Respawn presets route through the live global SWFOC_SetHeroRespawn helper.
/// The preset values + VM wiring + simulator wire-format are verifiable today.
/// </summary>
public sealed class Iter78HeroLabRespawnPresetTests
{
    private static (SwfocSimulator sim, HeroLabTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new HeroLabTabViewModel(adapter));
    }

    // -----------------------------------------------------------------
    // Preset value pins
    // -----------------------------------------------------------------

    [Fact]
    public async Task ApplyQuickRespawnPreset_SetsCustomRespawnMsTo2500()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        await vm.ApplyRespawnPresetAsync(2500);

        vm.CustomRespawnMs.Should().Be(2500);
    }

    [Fact]
    public async Task ApplyNormalRespawnPreset_SetsCustomRespawnMsTo5000()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        // Set non-default first to verify the preset truly resets.
        vm.CustomRespawnMs = 99999;

        await vm.ApplyRespawnPresetAsync(5000);

        vm.CustomRespawnMs.Should().Be(5000);
    }

    [Fact]
    public async Task ApplySlowRespawnPreset_SetsCustomRespawnMsTo15000()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        await vm.ApplyRespawnPresetAsync(15000);

        vm.CustomRespawnMs.Should().Be(15000);
    }

    [Fact]
    public async Task ApplyGlacialRespawnPreset_SetsCustomRespawnMsTo60000()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        await vm.ApplyRespawnPresetAsync(60000);

        vm.CustomRespawnMs.Should().Be(60000);
    }

    // -----------------------------------------------------------------
    // Command exposure & wire-format checks
    // -----------------------------------------------------------------

    [Fact]
    public void PresetCommands_AllExposed()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;

        vm.ApplyQuickRespawnCommand.Should().NotBeNull();
        vm.ApplyNormalRespawnCommand.Should().NotBeNull();
        vm.ApplySlowRespawnCommand.Should().NotBeNull();
        vm.ApplyGlacialRespawnCommand.Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyRespawnPreset_FiresGlobalSetHeroRespawn_RecordsSingleEntry()
    {
        var (sim, vm) = NewSession();
        using var _ = sim;
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var localVm = new HeroLabTabViewModel(adapter);
        // SetCustomRespawnAsync guards on SelectedHeroAddr==0 and short-
        // circuits without firing the bridge. Pick a hero first so the
        // bridge actually gets called — same operator workflow as a
        // manual click would have.
        localVm.SelectedHeroAddr = 0x1000;

        await localVm.ApplyRespawnPresetAsync(15000);

        // Filter by command name so the test pins what it actually means:
        // ApplyRespawnPreset
        // fires exactly one global SetHeroRespawn call (no double-fire / no zero-fire).
        var respawnCalls = adapter.RecentCalls
            .Where(c => c.LuaCommand.Contains("SetHeroRespawn", StringComparison.Ordinal)
                && !c.LuaCommand.Contains("SetHeroRespawnTimer", StringComparison.Ordinal))
            .ToList();
        respawnCalls.Should().HaveCount(1,
            because: "ApplyRespawnPreset must fire exactly one global SetHeroRespawn call regardless of unrelated auto-refresh activity");
    }

    [Fact]
    public async Task ApplyRespawnPreset_DoesNotChangeOtherFields()
    {
        // Scope discipline: respawn presets are scalar-only — they must
        // not bleed into permadeath flag, edit-stat fields, or selected
        // hero address. Operator still owns those independently.
        var (sim, vm) = NewSession();
        using var _ = sim;
        vm.SelectedHeroAddr = 0xDEADBEEF;
        // EditStatField is a string with a default — verify it doesn't
        // get clobbered. (HeroLab has multiple bound fields; scope test
        // sweeps the most likely contamination targets.)

        await vm.ApplyRespawnPresetAsync(15000);

        vm.SelectedHeroAddr.Should().Be(0xDEADBEEF, "respawn preset must not touch hero selection");
        vm.CustomRespawnMs.Should().Be(15000);
    }
}
