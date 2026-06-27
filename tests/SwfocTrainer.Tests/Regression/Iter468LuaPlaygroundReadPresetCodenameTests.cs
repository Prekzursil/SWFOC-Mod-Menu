using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-20 (iter 468) — pins the iter-468 relabelling of 4 read-only
/// discovery presets on the Lua Playground tab. Iter-296 (GetPlanets),
/// iter-299 (GetFactionRoster + GetCurrentMod), and iter-300 (ListMods)
/// preset labels were carrying iter-N codenames ([296] / [299] / [300])
/// as their operator-visible prefix; this iter migrates them to the
/// [disc] semantic-category prefix established by iter-467. Extends the
/// iter-388 codified rule's 5th forward application (1st VM-string-literal
/// recategorisation, after iter-467 introduced the [disc] tag for new
/// entries).
///
/// Red-green pair: the regex guard fires on the OLD [296]/[299]/[300]
/// labels and passes on the NEW [disc] labels. Pair lives in this file
/// per the "regression-guard lives next to the rule it pins" discipline.
/// </summary>
public sealed class Iter468LuaPlaygroundReadPresetCodenameTests
{
    private static (SwfocSimulator sim, LuaPlaygroundTabViewModel vm) CreateVm()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new LuaPlaygroundTabViewModel(adapter));
    }

    [Theory]
    [InlineData("return SWFOC_GetPlanets()")]
    [InlineData("return SWFOC_GetFactionRoster('Rebel')")]
    [InlineData("return SWFOC_GetCurrentMod()")]
    [InlineData("return SWFOC_ListMods()")]
    public void PresetMenu_Iter468ReadWire_NowCarriesDiscPrefix(string expectedScript)
    {
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var preset = vm.Iter100to113Presets.SingleOrDefault(p => p.Script == expectedScript);
            preset.Should().NotBeNull(
                $"iter-468 keeps the existing preset for {expectedScript} — only the label changes");
            preset!.Label.Should().StartWith("[disc] ",
                $"iter-468 relabels {expectedScript} from [NNN] codename to [disc] semantic prefix per iter-388 rule");
        }
    }

    [Theory]
    [InlineData("return SWFOC_GetPlanets()")]
    [InlineData("return SWFOC_GetFactionRoster('Rebel')")]
    [InlineData("return SWFOC_GetCurrentMod()")]
    [InlineData("return SWFOC_ListMods()")]
    public void PresetMenu_Iter468ReadWire_HasNoIterCodenameLeak(string expectedScript)
    {
        // Red-green guard: this test FAILS on the pre-iter-468 [296]/[299]/[300]
        // labels and PASSES on the iter-468 [disc] labels. Mirrors the iter-467
        // codename-leak guard but applied to recategorised (not new) presets.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var preset = vm.Iter100to113Presets.SingleOrDefault(p => p.Script == expectedScript);
            preset.Should().NotBeNull();
            var label = preset!.Label;
            System.Text.RegularExpressions.Regex.IsMatch(label, @"iter[ -]?\d+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Should().BeFalse(
                    $"iter-388 codified rule: operator-visible label '{label}' must not surface 'iter N' codenames");
            // Also reject bracketed-iter-N forms ([296], [299-N], etc.) — the
            // exact pattern this iter removes from the operator surface.
            System.Text.RegularExpressions.Regex.IsMatch(label, @"^\[\d+(?:[- ]\d+)*\]")
                .Should().BeFalse(
                    $"iter-468 strips [NNN] bracketed iter-N prefix from label '{label}'");
        }
    }

    [Fact]
    public void PresetMenu_DiscPrefixCount_GrewToAtLeast9_AfterIter468()
    {
        // iter-467 introduced 5 [disc] presets (GetAllPlayers / ListFactions /
        // EnumerateUnits / DiagSelfTest / GetBuildInfo). iter-468 relabels 4
        // existing read-only wires (GetPlanets / GetFactionRoster /
        // GetCurrentMod / ListMods) to share the same prefix. The combined
        // cluster is now >= 9; assert that floor to catch future drift.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var discCount = vm.Iter100to113Presets.Count(p => p.Label.StartsWith("[disc] "));
            discCount.Should().BeGreaterThanOrEqualTo(9,
                "iter-467 + iter-468 ship 5 new + 4 relabeled [disc] presets respectively");
        }
    }
}
