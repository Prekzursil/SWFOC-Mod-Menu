using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 183) — pins the Lua Playground preset menu expansion
/// to cover iter 100-182 LIVE wires (~100 presets total). Previously the
/// menu (iter 116/147) only covered iter 100-113 + iter 143-145 cameras
/// (~30 entries); iter 153-182's ~70 wires had no preset access.
///
/// Operator-facing improvement: zero bridge changes, pure VM/XAML.
/// </summary>
public sealed class Iter183LuaPlaygroundPresetExpansionTests
{
    private static (SwfocSimulator sim, LuaPlaygroundTabViewModel vm) CreateVm()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new LuaPlaygroundTabViewModel(adapter));
    }

    [Fact]
    public void PresetMenu_Covers100to182Wires_AtLeast80Presets()
    {
        // Pre-iter-183 the menu had ~30 entries (iter 100-113 + iter 143-145).
        // After iter 183 the menu has 83 entries covering iter 100-182 wires.
        // We assert >= 80 to leave room for future churn without breaking
        // on minor preset removals.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Count.Should().BeGreaterThanOrEqualTo(80,
                "iter 183 expanded the preset menu to cover all iter 100-182 LIVE wires");
        }
    }

    [Theory]
    [InlineData("[150]")]
    [InlineData("[151]")]
    [InlineData("[154]")]
    [InlineData("[157]")]
    [InlineData("[166]")]
    [InlineData("[167]")]
    [InlineData("[173]")]
    [InlineData("[177]")]
    [InlineData("[178]")]
    [InlineData("[180]")]
    [InlineData("[182]")]
    public void PresetMenu_HasAtLeastOnePresetForIter(string iterTag)
    {
        // Pin: each major iter post-145 must have at least one preset entry
        // so operators can discover it via the dropdown.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Should().Contain(p => p.Label.Contains(iterTag),
                $"iter 183 should have added at least one preset tagged {iterTag}");
        }
    }

    [Fact]
    public void PresetMenu_Iter180_IncludesNamespacedFOWManagerExample()
    {
        // Pin: namespaced functions (FOWManager.X) should show up in the
        // preset list so operators don't have to grep docs to discover them.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Should()
                .Contain(p => p.Script.Contains("FOWRevealAll"),
                    "iter 180 FOW namespaced wire should be discoverable");
        }
    }

    [Fact]
    public void PresetMenu_Iter181_FlagsEngineTypo()
    {
        // Pin: SFXManager.Allow_Unit_Reponse_VO has a known engine typo.
        // The preset label should warn about it so the operator doesn't
        // think the menu has a typo.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Should()
                .Contain(p => p.Label.Contains("[181]") && p.Label.Contains("typo"),
                    "iter 181 SFXManager preset should flag the engine typo");
        }
    }

    [Fact]
    public void PresetMenu_Iter182_HasGlobalFormDistinction()
    {
        // Pin: iter-182 wires are global-form alternatives to iter-161
        // obj-receiver versions. Preset labels should use "global-form"
        // language so operators understand the distinction.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Should()
                .Contain(p => p.Label.Contains("[182]") && p.Label.Contains("global-form"),
                    "iter 182 Make_Ally/Make_Enemy presets should clarify global-form");
        }
    }
}
