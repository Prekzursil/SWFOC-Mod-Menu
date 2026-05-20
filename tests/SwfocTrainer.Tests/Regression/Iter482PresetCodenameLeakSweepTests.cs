using System.Text.RegularExpressions;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-21 (iter 482) — project-wide drift catcher for the iter-388
/// "Internal-Codename-In-Tooltips-Drift" rule, applied to the entire
/// <see cref="LuaPlaygroundTabViewModel.Iter100to113Presets"/> roster.
///
/// Iter-467/468/469/470 added per-script regression guards that only fire
/// on the specific scripts being relabelled in each iter. The cdbe4f12
/// adversarial review (iter-470 sweep) flagged this as MEDIUM: the next
/// stale `[NNN]` codename added anywhere else in the roster wouldn't trip
/// any test (drift surface left uncovered).
///
/// This file closes that gap with three project-wide invariants:
///
///   1. No preset label may contain the substring "iter N" / "iter-N" /
///      "iterN" anywhere (case-insensitive). This catches future drift
///      where someone writes the raw "iter 500" form in a new label.
///      Current count: 0 (none have ever been added in this form).
///
///   2. Bracketed `[NNN]` / `[NNN-NNN]` / `[NNN/NNN]` codename prefixes
///      must be in the explicit allowlist below. Catches NEW `[NNN]`
///      additions outside the sweep cadence; the allowlist must be
///      extended (with a rationale comment) before any new prefix lands.
///
///   3. The allowlist may not contain stale entries. After each iter-388
///      batch sweep (iter-380, iter-388, iter-464, iter-466, iter-468,
///      iter-469, iter-470) drains a prefix, the allowlist must shrink in
///      the same commit. Catches the failure mode where a sweep removes
///      the production label but forgets to update this allowlist —
///      which would silently re-admit any future re-introduction.
///
/// Pairs with the iter-388 codified rule's "Prospective uses" section
/// (per feedback_codified_rule_self_validates_via_forward_application.md
/// — iter-373 codified rule, 5th forward application here).
///
/// Drainage trail of the cdbe4f12 (iter-470 adversarial review) MEDIUMs:
///   - MEDIUM "Global codename-leak fact" → THIS FILE (iter-482).
///   - MEDIUM "Cluster membership identity" → still OPEN
///     (Iter469/Iter470 floor-count Facts pin cardinality, not identity).
///   - MEDIUM "Per-object vs global [read] discriminator" → still OPEN
///     (iter-470 heuristic comment, not test-enforced).
///
/// Discoverer: editor-polish hat, iter-482, picking from
/// knowledge-base/polish_backlog_2026-05-20.md (3-MEDIUM cdbe4f12 set).
/// </summary>
public sealed class Iter482PresetCodenameLeakSweepTests
{
    private static (SwfocSimulator sim, LuaPlaygroundTabViewModel vm) CreateVm()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, new LuaPlaygroundTabViewModel(adapter));
    }

    /// <summary>
    /// Allowlisted bracketed-iter-N prefixes present in the roster as of
    /// 2026-05-21 (iter 482). These are intentional survivors — the
    /// iter-467/468/469/470 sweep only recategorised the inspect / discover
    /// subsets ([read] / [disc]). The mutation-side, action-side, and
    /// PHASE-2-pending presets keep their `[NNN]` codenames until a future
    /// `[write]` / `[mut]` / `[action]` cluster lands (per the iter-181
    /// deferred-tracking entry in knowledge-base/polish_backlog_2026-05-20.md).
    ///
    /// When the next sweep lands and drains an entry from the production
    /// roster, the same commit MUST remove it from this allowlist.
    /// Invariant #3 below pins that discipline.
    /// </summary>
    private static readonly HashSet<string> AllowlistedBracketedPrefixes = new()
    {
        // Iter-96 / iter-100 — speed + global damage multiplier
        "[96]", "[100]",
        // Iter-107..113 — camera + owner + spawn + per-unit toggles + universal escape
        "[107]", "[108]", "[109]", "[110]", "[111]", "[112]", "[113]",
        // Iter-143..145 — camera primitive arc
        "[143]", "[144]", "[145]",
        // Iter-150..166 — letterbox / teleport / spawn / invuln / float-arg / player / abilities
        // / limbo / story / lock-controls / diplomacy / speed-override / cinematic / fire-target
        // / credits / music
        "[150]", "[151]", "[152]", "[153]", "[154]", "[155]", "[156]", "[157]",
        "[158]", "[159]", "[160]", "[161]", "[162]", "[163]", "[164]", "[166]",
        // Iter-175..186 — TaskForce + discovery + pair-completion + namespaced + iter-223 refresh
        "[175]", "[176]", "[177]", "[179]", "[180]", "[181]", "[182]",
        "[184]", "[185]", "[186]",
        // A1.x global-wire arc (iter 225-285)
        "[225]", "[231]", "[237]", "[243]", "[258]",
        // Iter 267-268 / 269-270 — HONEST DEFER comment presets (alternative-set pattern)
        "[267-268]", "[269-270]",
        "[282]", "[285]",
        // Iter-450 SWFOC_TriggerVictory — 14 PHASE-2-PENDING entries
        "[450]"
    };

    [Fact]
    public void NoPresetLabelContainsIterDigitsSubstring()
    {
        // Invariant #1: no label may contain "iter N" / "iter-N" / "iterN"
        // substring anywhere (case-insensitive). This is the strict iter-388
        // shape — the codified rule bans the literal "iter <number>" form
        // in operator-visible strings. Catches future drift where someone
        // writes a label like "iter 500 spawn helper".
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var iterDigitsRegex = new Regex(
                @"iter[ -]?\d+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var hits = vm.Iter100to113Presets
                .Where(p => iterDigitsRegex.IsMatch(p.Label))
                .Select(p => p.Label)
                .ToList();

            hits.Should().BeEmpty(
                "iter-388 codified rule: no operator-visible preset label may " +
                "carry an 'iter N' substring. Found violations: " +
                $"[{string.Join(", ", hits.Select(h => $"'{h}'"))}]. " +
                "Either rewrite the label to drop the codename or extend the " +
                "[read] / [disc] / [write] semantic cluster set.");
        }
    }

    [Fact]
    public void BracketedNCodenamePrefixes_StayWithinAllowlist()
    {
        // Invariant #2: every `[NNN]` prefix that appears in a production
        // preset label must be in AllowlistedBracketedPrefixes. Catches NEW
        // `[NNN]` additions outside the sweep cadence. Forces extending the
        // allowlist with a written rationale before the new prefix lands.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            // Matches `[NNN]`, `[NNN-NNN]`, `[NNN/NNN]` at start of label.
            // The `/` accommodates the iter-450/451 SWFOC_TriggerVictory
            // divider form (currently only on a separator line, but the
            // shape is part of the allowlisted convention).
            var bracketRegex = new Regex(
                @"^\[\d+(?:[- /]\d+)*\]",
                RegexOptions.Compiled);

            var unknownPrefixes = new List<(string Prefix, string Label)>();
            foreach (var preset in vm.Iter100to113Presets)
            {
                var match = bracketRegex.Match(preset.Label);
                if (!match.Success) continue;
                if (!AllowlistedBracketedPrefixes.Contains(match.Value))
                {
                    unknownPrefixes.Add((match.Value, preset.Label));
                }
            }

            unknownPrefixes.Should().BeEmpty(
                "iter-388 project-wide drift catcher: NEW `[NNN]` codename " +
                "prefix(es) appeared without an allowlist extension. " +
                $"Unknown prefix(es): [{string.Join(", ", unknownPrefixes.Select(u => $"{u.Prefix} in '{u.Label}'"))}]. " +
                "Either (a) replace [NNN] with a semantic cluster prefix " +
                "([read]/[disc]/[write]/[action]/etc.) per the iter-467+ " +
                "sweep convention, OR (b) extend AllowlistedBracketedPrefixes " +
                "with a rationale comment explaining why the codename must " +
                "survive (e.g. PHASE-2-PENDING marker, HONEST DEFER pointer).");
        }
    }

    [Fact]
    public void Allowlist_OnlyContainsActuallyPresentPrefixes()
    {
        // Invariant #3: every entry in AllowlistedBracketedPrefixes must
        // actually appear in the production roster. Catches the failure
        // mode where a sweep removes the last production label carrying a
        // prefix but forgets to shrink this allowlist — which would
        // silently re-admit any future re-introduction of that prefix.
        //
        // Red-green: PASSES today because every allowlist entry has ≥1
        // matching preset. FAILS the day a sweep drains the last production
        // label of (e.g.) [225] without removing "[225]" from the
        // allowlist set above.
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var bracketRegex = new Regex(
                @"^\[\d+(?:[- /]\d+)*\]",
                RegexOptions.Compiled);

            var actuallyPresent = vm.Iter100to113Presets
                .Select(p => bracketRegex.Match(p.Label))
                .Where(m => m.Success)
                .Select(m => m.Value)
                .ToHashSet();

            var staleEntries = AllowlistedBracketedPrefixes
                .Where(prefix => !actuallyPresent.Contains(prefix))
                .ToList();

            staleEntries.Should().BeEmpty(
                "iter-388 allowlist-parity invariant: AllowlistedBracketedPrefixes " +
                "contains entries that no longer appear in any production " +
                $"preset label. Stale entry/entries: [{string.Join(", ", staleEntries)}]. " +
                "Either (a) the most recent sweep forgot to remove them in the " +
                "same commit (fix: drop from allowlist), OR (b) the production " +
                "preset using this prefix was deleted (same fix).");
        }
    }
}
