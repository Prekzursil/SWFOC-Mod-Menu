using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-20 (iter 467) — pins the iter-467 discovery + diagnostics preset
/// group on the Lua Playground tab. Five read-only LIVE wires
/// (GetAllPlayers / ListFactions / EnumerateUnits / DiagSelfTest /
/// GetBuildInfo) that pre-dated the iter-450 series but were never on the
/// preset dropdown. Closes path-#2 discoverability gap; pair with the
/// iter-461 operator-visible work cadence.
/// </summary>
public sealed class Iter467LuaPlaygroundDiscoveryPresetTests
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
    public void PresetMenu_HasDiscoveryDiagnosticsDivider()
    {
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Should()
                .Contain(p => p.Label.Contains("Discovery + diagnostics"),
                    "iter 467 inserts a divider before the new preset group");
        }
    }

    [Theory]
    [InlineData("return SWFOC_GetAllPlayers()")]
    [InlineData("return SWFOC_ListFactions()")]
    [InlineData("return SWFOC_EnumerateUnits('Rebel')")]
    [InlineData("return SWFOC_DiagSelfTest()")]
    [InlineData("return SWFOC_GetBuildInfo()")]
    public void PresetMenu_ContainsIter467DiscoveryWire(string expectedScript)
    {
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Should()
                .Contain(p => p.Script == expectedScript,
                    $"iter 467 surfaces the read-only LIVE wire {expectedScript} as a preset");
        }
    }

    [Fact]
    public void PresetMenu_Iter467GroupLabels_HaveNoIterCodenameLeak()
    {
        // Honors iter-388 codified rule: operator-visible attribute values
        // (preset Label here) MUST NOT carry "iter N" / "iter-N" tokens.
        // Bracketed [disc] is the tag convention; iter numbers stay in the
        // source comment, not the operator-visible label.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var iter467ScriptPredicates = new[]
            {
                "return SWFOC_GetAllPlayers()",
                "return SWFOC_ListFactions()",
                "return SWFOC_EnumerateUnits('Rebel')",
                "return SWFOC_DiagSelfTest()",
                "return SWFOC_GetBuildInfo()",
            };

            foreach (var script in iter467ScriptPredicates)
            {
                var preset = vm.Iter100to113Presets.SingleOrDefault(p => p.Script == script);
                preset.Should().NotBeNull($"iter-467 should ship preset for {script}");
                var label = preset!.Label;
                System.Text.RegularExpressions.Regex.IsMatch(label, @"iter[ -]?\d+",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    .Should().BeFalse(
                        $"iter-388 codified rule: operator-visible label '{label}' must not surface 'iter N' codenames");
            }
        }
    }

    [Fact]
    public void PresetMenu_TotalCountGrewBy_AtLeast5_AfterIter467()
    {
        // The iter-183 pin asserts >= 80. After iter 467 the count is 129
        // baseline + 5 wires + 1 divider = 135. We assert >= 134 to leave
        // room for future churn.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            vm.Iter100to113Presets.Count.Should().BeGreaterThanOrEqualTo(134,
                "iter 467 adds 5 discovery presets + 1 divider to the preset menu");
        }
    }
}
