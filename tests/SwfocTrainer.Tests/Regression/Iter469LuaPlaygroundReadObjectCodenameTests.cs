using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-20 (iter 469) — pins the iter-469 relabelling of 8 per-object
/// read presets on the Lua Playground tab. Iter-167 through iter-174 covers
/// LIVE read-only wires that inspect a specific game object (hull, engines,
/// credits, name, position, garrison, ability state, bone position). These
/// were carrying iter-N codenames ([167]-[174]) as their operator-visible
/// prefix; this iter migrates them to a new [read] semantic-category prefix.
///
/// Distinct from the iter-467/468 [disc] cluster: [disc] is environment /
/// global state discovery (planets, faction roster, mod list, player list);
/// [read] is per-object / per-faction state inspection. Two semantic
/// prefixes, two coherent clusters.
///
/// 6th forward application of the iter-388 codified rule + 2nd
/// VM-string-literal recategorisation (after iter-468 was 1st).
///
/// Red-green pair: the regex guard fires on the OLD [167]-[174] labels
/// and passes on the NEW [read] labels. Pair lives in this file per the
/// "regression-guard lives next to the rule it pins" discipline.
/// </summary>
public sealed class Iter469LuaPlaygroundReadObjectCodenameTests
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
    [InlineData("return SWFOC_GetHullLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_AreEnginesOnlineLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_GetCreditsLua('Find_Player(\"REBEL\")')")]
    [InlineData("return SWFOC_GetNameLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_GetPositionLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_GetGarrisonUnitsLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_IsAbilityActiveLua('Find_First_Object(\"Empire_AT_AT\")', '\"DEPLOY\"')")]
    [InlineData("return SWFOC_GetBonePositionLua('Find_First_Object(\"Empire_AT_AT\")', '\"HEAD\"')")]
    public void PresetMenu_Iter469ReadObjectWire_NowCarriesReadPrefix(string expectedScript)
    {
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var preset = vm.Iter100to113Presets.SingleOrDefault(p => p.Script == expectedScript);
            preset.Should().NotBeNull(
                $"iter-469 keeps the existing preset for {expectedScript} — only the label changes");
            preset!.Label.Should().StartWith("[read] ",
                $"iter-469 relabels {expectedScript} from [NNN] codename to [read] semantic prefix per iter-388 rule");
        }
    }

    [Theory]
    [InlineData("return SWFOC_GetHullLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_AreEnginesOnlineLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_GetCreditsLua('Find_Player(\"REBEL\")')")]
    [InlineData("return SWFOC_GetNameLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_GetPositionLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_GetGarrisonUnitsLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("return SWFOC_IsAbilityActiveLua('Find_First_Object(\"Empire_AT_AT\")', '\"DEPLOY\"')")]
    [InlineData("return SWFOC_GetBonePositionLua('Find_First_Object(\"Empire_AT_AT\")', '\"HEAD\"')")]
    public void PresetMenu_Iter469ReadObjectWire_HasNoIterCodenameLeak(string expectedScript)
    {
        // Red-green guard: this test FAILS on the pre-iter-469 [167]-[174]
        // labels and PASSES on the iter-469 [read] labels. Mirrors the
        // iter-468 codename-leak guard but applied to the per-object read
        // cluster (vs iter-468's environment discovery cluster).
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
            // Also reject bracketed-iter-N forms ([167], [173-174], etc.) — the
            // exact pattern this iter removes from the operator surface.
            System.Text.RegularExpressions.Regex.IsMatch(label, @"^\[\d+(?:[- ]\d+)*\]")
                .Should().BeFalse(
                    $"iter-469 strips [NNN] bracketed iter-N prefix from label '{label}'");
        }
    }

    [Fact]
    public void PresetMenu_ReadPrefixCount_IsAtLeast8_AfterIter469()
    {
        // iter-469 introduces 8 [read] presets in a new semantic cluster
        // (per-object inspection wires from the iter 167-174 surfacing arc).
        // Distinct from the iter-467/468 [disc] cluster. Assert the floor
        // to catch future drift / accidental relabel back to [NNN].
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var readCount = vm.Iter100to113Presets.Count(p => p.Label.StartsWith("[read] "));
            readCount.Should().BeGreaterThanOrEqualTo(8,
                "iter-469 ships 8 [read] presets in a new per-object inspection cluster");
        }
    }

    [Fact]
    public void PresetMenu_DiscClusterFloor_IsUnchangedByIter469()
    {
        // Defensive guard: iter-469 introduces the [read] cluster but MUST
        // NOT cannibalise or relabel iter-467/468's [disc] cluster. The
        // floor of 9 [disc] presets (5 iter-467 + 4 iter-468) must hold.
        // This protects against an accidental sweep that conflates the two
        // semantic prefixes.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var discCount = vm.Iter100to113Presets.Count(p => p.Label.StartsWith("[disc] "));
            discCount.Should().BeGreaterThanOrEqualTo(9,
                "iter-469 keeps the iter-467/468 [disc] cluster intact (9 entries)");
        }
    }
}
