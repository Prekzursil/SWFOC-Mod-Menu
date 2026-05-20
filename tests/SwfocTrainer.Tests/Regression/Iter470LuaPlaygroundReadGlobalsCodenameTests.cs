using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-20 (iter 470) — pins the iter-470 relabelling of 3 global state
/// read presets on the Lua Playground tab. Iter-178 + iter-181 covers LIVE
/// read-only wires that inspect engine GLOBAL state (current game mode,
/// local player handle, current cinematic Thread stage). These were carrying
/// iter-N codenames ([178]/[181]) as their operator-visible prefix; this iter
/// migrates them to the iter-469 [read] semantic cluster.
///
/// Cluster scope expansion: iter-469 introduced [read] as the per-object
/// inspection cluster (8 entries: AT-AT hull/engines/name/position/etc.,
/// Rebel credits, garrison units, bone position). Iter-470 extends [read]
/// semantics to ANY explicit Read operation regardless of scope — the
/// absence of an object name in the label IS the global-scope signal:
///   per-object: "[read] AT-AT current hull", "[read] Rebel current credits"
///   global:     "[read] Current game mode",  "[read] Local player handle",
///               "[read] Current cinematic Thread stage"
///
/// 7th forward application of the iter-388 codified rule + 3rd
/// VM-string-literal recategorisation (iter-468 1st, iter-469 2nd).
///
/// Red-green pair: the regex guard fires on the OLD [178]/[181] labels and
/// passes on the NEW [read] labels. Pair lives in this file per the
/// "regression-guard lives next to the rule it pins" discipline.
/// </summary>
public sealed class Iter470LuaPlaygroundReadGlobalsCodenameTests
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
    [InlineData("return SWFOC_GetGameModeLua()")]
    [InlineData("return SWFOC_GetLocalPlayerLua()")]
    [InlineData("return SWFOC_ThreadGetCurrentStageLua()")]
    public void PresetMenu_Iter470ReadGlobalWire_NowCarriesReadPrefix(string expectedScript)
    {
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var preset = vm.Iter100to113Presets.SingleOrDefault(p => p.Script == expectedScript);
            preset.Should().NotBeNull(
                $"iter-470 keeps the existing preset for {expectedScript} — only the label changes");
            preset!.Label.Should().StartWith("[read] ",
                $"iter-470 relabels {expectedScript} from [NNN] codename to [read] semantic prefix per iter-388 rule");
        }
    }

    [Theory]
    [InlineData("return SWFOC_GetGameModeLua()")]
    [InlineData("return SWFOC_GetLocalPlayerLua()")]
    [InlineData("return SWFOC_ThreadGetCurrentStageLua()")]
    public void PresetMenu_Iter470ReadGlobalWire_HasNoIterCodenameLeak(string expectedScript)
    {
        // Red-green guard: this test FAILS on the pre-iter-470 [178]/[181]
        // labels and PASSES on the iter-470 [read] labels. Mirrors the
        // iter-469 codename-leak guard but applied to the global state read
        // cluster expansion (vs iter-469's per-object inspection cluster).
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
            // Also reject bracketed-iter-N forms ([178], [181], etc.) — the
            // exact pattern this iter removes from the operator surface.
            System.Text.RegularExpressions.Regex.IsMatch(label, @"^\[\d+(?:[- ]\d+)*\]")
                .Should().BeFalse(
                    $"iter-470 strips [NNN] bracketed iter-N prefix from label '{label}'");
        }
    }

    [Fact]
    public void PresetMenu_ReadPrefixCount_IsAtLeast11_AfterIter470()
    {
        // iter-470 grows the [read] cluster from 8 (iter-469 per-object) to
        // 11 (+3 global state reads: game mode, local player handle,
        // cinematic Thread stage). Assert the new floor to catch future
        // drift / accidental relabel back to [NNN].
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var readCount = vm.Iter100to113Presets.Count(p => p.Label.StartsWith("[read] "));
            readCount.Should().BeGreaterThanOrEqualTo(11,
                "iter-470 extends [read] cluster from 8 to 11 (+3 global state reads)");
        }
    }

    [Fact]
    public void PresetMenu_DiscClusterFloor_IsUnchangedByIter470()
    {
        // Defensive guard: iter-470 extends the [read] cluster but MUST NOT
        // cannibalise or relabel iter-467/468's [disc] cluster. The floor
        // of 9 [disc] presets (5 iter-467 + 4 iter-468) must hold. Protects
        // against an accidental sweep that conflates the two semantic
        // prefixes (especially relevant because [178] globals were the
        // "could fit [read] OR [disc]" borderline case noted in iter-469).
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var discCount = vm.Iter100to113Presets.Count(p => p.Label.StartsWith("[disc] "));
            discCount.Should().BeGreaterThanOrEqualTo(9,
                "iter-470 keeps the iter-467/468 [disc] cluster intact (9 entries)");
        }
    }

    [Fact]
    public void PresetMenu_Iter181WriteSideStaysAsCodename_NotRecategorised()
    {
        // Defensive pin: the iter-181 SFXManager "Disable unit VO" preset is
        // a WRITE/mutation wire (SFXManager.Allow_Unit_Reponse_VO = false),
        // not a read. It MUST NOT be swept into the [read] cluster by an
        // overzealous relabel pass. This pins the codename-as-prefix on the
        // mutation entry so the iter-470 sweep does not accidentally widen
        // its scope to mutations.
        //
        // Acceptable future behaviour: a separate semantic prefix for
        // mutations (e.g. [write] or [mut]) could land in a later iter with
        // its own dedicated regression-guard test. Until then, [181] stays.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var preset = vm.Iter100to113Presets.SingleOrDefault(
                p => p.Script == "return SWFOC_SFXAllowUnitReponseVoLua('false')");
            preset.Should().NotBeNull("[181] Disable unit VO preset survives iter-470 sweep");
            preset!.Label.Should().Contain("[181]",
                "the iter-181 mutation wire keeps its [NNN] codename — iter-470 only touched the Read-verb entries");
            preset.Label.Should().Contain("typo",
                "the iter-181 mutation wire still flags the engine typo 'Reponse'");
        }
    }
}
