using System.Text.RegularExpressions;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-21 (iter 482, scope-honest-renamed iter 484) — drift catcher for
/// the iter-388 "Internal-Codename-In-Tooltips-Drift" rule, applied to the
/// <see cref="LuaPlaygroundTabViewModel.Iter100to113Presets"/> roster only.
///
/// SCOPE (post iter-484 honesty pass): the three invariants below iterate
/// exactly ONE collection — <c>LuaPlaygroundTabViewModel.Iter100to113Presets</c>.
/// They are NOT project-wide. If future work adds a second <c>IList&lt;Preset&gt;</c>
/// property to <see cref="LuaPlaygroundTabViewModel"/>, or if other tab VMs
/// surface preset dropdowns with iter-N codenames, those collections are
/// not guarded by this file. A reflection-based "all-VMs all-preset-lists"
/// extension is captured in the iter-482/484 polish backlog (cdbe4f12
/// follow-up batch) as a future arc.
///
/// Iter-467/468/469/470 added per-script regression guards that only fire
/// on the specific scripts being relabelled in each iter. The cdbe4f12
/// adversarial review (iter-470 sweep) flagged this as MEDIUM: the next
/// stale `[NNN]` codename added anywhere else in the same roster wouldn't
/// trip any test (drift surface left uncovered).
///
/// This file closes that gap for the Iter100to113Presets roster with three
/// invariants:
///
///   1. No preset label may contain a word-bounded "iter N" / "iter-N" /
///      "iterN" token (case-insensitive). The `\b` boundaries (added in
///      iter-484 per the 9298748 adversarial-review LOW) prevent false
///      positives on legitimate substrings like `filter1`, `rerouter5`,
///      `writer42`, `transmitter9`. Catches future drift where someone
///      writes the raw "iter 500" form in a new label.
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
/// Iter-484 (9298748 adversarial-review drainage):
///   - MEDIUM "Test-class name + commit narrative oversell scope" →
///     RESOLVED via this file's rename + this docstring rewrite.
///   - MEDIUM "Script-body codename deferral lacks tracked placeholder" →
///     RESOLVED via <see cref="ScriptBodyCodenameSweep_PlaceholderForFutureArc"/>
///     skipped fact below.
///   - LOW "Regex lacks word boundaries" → RESOLVED via `\b...\b`.
///   - LOW "Sim-startup waste" → DEFERRED to a multi-file Iter46x/47x/48x
///     fixture-pattern arc (changing this file alone creates inconsistency
///     with the 6+ peer test files using the same CreateVm idiom).
///   - LOW "Commit message 54 vs 48 count" → RESOLVED via scratchpad
///     correction; allowlist below has 48 entries.
///
/// Iter-485 (8f97e1d adversarial-review drainage — same-file precision):
///   - MEDIUM "Invariant #3 failure-message remediation hint" →
///     RESOLVED via the `because` rewrite on invariant #3 below
///     (explicit allowlist-shrinkage symmetry + "do NOT revert
///     production-side drainage" anti-reversion guard).
///   - LOW "Placeholder Skip-fact body lacks fail-on-activation guard" →
///     RESOLVED via Assert.Fail in
///     <see cref="ScriptBodyCodenameSweep_PlaceholderForFutureArc"/>
///     (deletion of Skip now puts the test in a deliberately-red state).
///   - LOW "using SwfocTrainer.Core.Services may be unused" →
///     NOT-A-DEFECT: NamedPipeLuaBridgeClient lives in that namespace
///     (verified iter-485 grep) and is used in CreateVm at line ~89.
///   - LOW "FluentAssertions `because` precision audit" → RESOLVED as
///     bundled into the invariant #3 rewrite per reviewer recommendation.
///   - MEDIUM "Misfiled directory Regression/ vs DriftCatchers/" →
///     DEFERRED (cross-file arc covering 6+ peer files; same scope as M4).
///   - MEDIUM "Compound-word regex form" → DEFERRED (watch-item, no
///     real-world drift yet; reverting `\b` re-introduces a known LOW).
///   - MEDIUM "Sim-startup fixture refactor" → DEFERRED (re-affirmed
///     9298748 LOW; multi-file arc, blocked by sealed V2BridgeAdapter).
///   - LOW "Skip-message back-reference durability" → DEFERRED (anchor
///     update bundled with next backlog rotation).
///
/// Discoverer: editor-polish hat, iter-482, picking from
/// knowledge-base/polish_backlog_2026-05-20.md (3-MEDIUM cdbe4f12 set).
/// Scope-honesty + word-boundary + Skip-placeholder follow-ups landed in
/// iter-484 from the 9298748 adversarial review batch.
/// </summary>
public sealed class Iter100to113PresetCodenameLeakSweepTests
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
    ///
    /// Count (verified iter-484 manual recount post 9298748 review): 48.
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
        // Invariant #1: no label may contain a word-bounded "iter N" /
        // "iter-N" / "iterN" token (case-insensitive). The `\b` boundaries
        // (iter-484 9298748 adversarial-review LOW fix) prevent false
        // positives on legitimate substrings like `filter1`, `rerouter5`,
        // `writer42`, `transmitter9`. Catches future drift where someone
        // writes a label like "iter 500 spawn helper".
        var (sim, vm) = CreateVm();
        using (sim)
        {
            var iterDigitsRegex = new Regex(
                @"\biter[ -]?\d+\b",
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
                "iter-388 roster-scoped drift catcher: NEW `[NNN]` codename " +
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
                "REMEDIATION: delete the stale entry/entries from " +
                "`AllowlistedBracketedPrefixes` in this file. " +
                "This invariant exists to FORCE allowlist-shrinkage symmetry " +
                "with sweep-drainage: when a sweep removes the last " +
                "production label carrying a `[NNN]` prefix, the same " +
                "commit MUST shrink the allowlist. Failing this assertion " +
                "is the EXPECTED outcome of legitimate sweep work and is " +
                "NOT a regression in the production change — do NOT revert " +
                "the production-side drainage. Likely causes: (a) the most " +
                "recent sweep forgot to remove the prefix in the same " +
                "commit, OR (b) the production preset using this prefix " +
                "was deleted in unrelated work. Both fixes are identical: " +
                "drop the stale entry from `AllowlistedBracketedPrefixes`.");
        }
    }

    /// <summary>
    /// iter-484 9298748 adversarial-review MEDIUM "Script-body codename
    /// deferral lacks tracked placeholder" — code-side breadcrumb for the
    /// future arc that sweeps `iter-N` substrings out of the preset SCRIPT
    /// BODIES (the second <c>new(...)</c> argument that pastes into the
    /// editor pane when an operator selects the preset).
    ///
    /// Iter-482 (this file's parent) scoped the cdbe4f12 MEDIUM to LABELS
    /// only. Script bodies still carry `-- iter 267-268:` / `iter-99` /
    /// `iter-100` / `iter-225` substrings inside the Lua source pasted to
    /// the editor. Those substrings ARE operator-visible per the
    /// iter-388 codified rule (the script renders in the editor pane on
    /// preset selection), but were deferred because the label sweep is
    /// the higher-blast-radius surface (visible in the dropdown without
    /// selection).
    ///
    /// Deletion of the <c>Skip</c> attribute below is the natural commit
    /// boundary for the script-body sweep arc. When undertaking that arc:
    ///   1. Drop the Skip attribute.
    ///   2. Update the regex to apply to <c>p.Script</c> in addition to
    ///      (or instead of) <c>p.Label</c>.
    ///   3. Rewrite each violating script-body comment/identifier.
    ///   4. Close the corresponding entry in
    ///      knowledge-base/polish_backlog_2026-05-20.md.
    /// </summary>
    [Fact(Skip = "iter-484 future arc: script-body codename sweep — see knowledge-base/polish_backlog_2026-05-20.md MEDIUM 'Script-body codename deferral'. Deletion of this Skip attribute IS the arc's natural entry point.")]
    public void ScriptBodyCodenameSweep_PlaceholderForFutureArc()
    {
        // iter-485 (from 8f97e1d adversarial-review LOW "Placeholder
        // Skip-fact body lacks fail-on-activation guard"): deliberately
        // RED on activation. When a future arc removes the Skip attribute
        // above (the natural entry-point per iter-484's design), this
        // Assert.Fail forces the arc author to replace the body with the
        // actual regex-over-script-bodies assertion before the test goes
        // green. Empty body would have silently passed and masked
        // incomplete arc work.
        Assert.Fail(
            "Script-body codename sweep arc activated but not implemented. " +
            "Replace this Assert.Fail with the actual assertion — mirror " +
            "the NoPresetLabelContainsIterDigitsSubstring shape but apply " +
            "the `\\biter[ -]?\\d+\\b` regex to `p.Script` instead of " +
            "`p.Label`. See the Skip attribute message for the 4-step " +
            "playbook.");
    }
}
