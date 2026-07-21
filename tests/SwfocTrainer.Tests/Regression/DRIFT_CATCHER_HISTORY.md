# Drift Catcher History — Iter100to113PresetCodenameLeakSweepTests

This file moves the per-iter RESOLVED bullet history out of the
`Iter100to113PresetCodenameLeakSweepTests.cs` class-level XML doc, which
was approaching 95 lines of append-only chronological accretion (see
iter-486 polish_backlog entry "Class-doc archaeology accretion").

`git log -p tests/SwfocTrainer.Tests/Regression/Iter100to113PresetCodenameLeakSweepTests.cs`
preserves the authoritative audit trail. This file is a curated summary
optimized for a fresh reader landing on the test file at iter-500+ who
wants to understand "what review cycles shaped this file?" without
scrolling through five commits worth of XML doc bullets.

## Provenance

- **Owner test file**: `Iter100to113PresetCodenameLeakSweepTests.cs` (same directory).
- **Discoverer pattern**: every entry below originated as a polish_backlog item filed by the adversarial-reviewer hat against a specific commit.
- **Drainage cadence**: 2-iter review → drain (review of commit `X` → drain in commit `X+2`).

## Iter-482 (commit `9298748`) — file created

- Created file under name `Iter482PresetCodenameLeakSweepTests`.
- Implements 3 invariants for the `LuaPlaygroundTabViewModel.Iter100to113Presets` roster.
- Drainage of cdbe4f12 (iter-470) MEDIUM "Global codename-leak fact".
- Two MEDIUMs left OPEN at this commit: "Cluster identity" + "Per-object discriminator".

## Iter-484 (commit `8f97e1d`) — 9298748 review drainage

- MEDIUM "Test-class name + commit narrative oversell scope" → RESOLVED via file + class rename to `Iter100to113PresetCodenameLeakSweepTests` + SCOPE-honesty docstring rewrite explicitly disclaiming project-wide guarantees.
- MEDIUM "Script-body codename deferral lacks tracked placeholder" → RESOLVED via `ScriptBodyCodenameSweep_PlaceholderForFutureArc` skipped Fact (deletion of the Skip attribute IS the arc's natural entry point).
- LOW "Regex lacks word boundaries" → RESOLVED via `\b...\b` (prevents false positives on `filter1`, `rerouter5`, `writer42`, `transmitter9`).
- LOW "Sim-startup waste" → DEFERRED (multi-file Iter46x/47x/48x fixture-pattern arc; changing this file alone creates inconsistency with the 6+ peer test files using the same CreateVm idiom).
- LOW "Commit message 54 vs 48 count" → RESOLVED via scratchpad correction; allowlist count of 48 verified by manual recount.

## Iter-485 (commit `f18bc78`) — 8f97e1d review drainage

- MEDIUM "Invariant #3 failure-message remediation hint" → RESOLVED via `because`-text rewrite on invariant #3 (added REMEDIATION lead-in + allowlist-shrinkage-symmetry rationale + anti-reversion guard "do NOT revert production-side drainage" + cause taxonomy).
- LOW "Placeholder Skip-fact body lacks fail-on-activation guard" → RESOLVED via `Assert.Fail(...)` in placeholder body (deletion of Skip now puts the test in a deliberately-red state until the arc author replaces the body with the actual assertion).
- LOW "`using SwfocTrainer.Core.Services` may be unused" → NOT-A-DEFECT (`NamedPipeLuaBridgeClient` lives in that namespace; verified by grep; used in `CreateVm`).
- LOW "FluentAssertions `because` precision audit" → RESOLVED (bundled into invariant #3 rewrite per reviewer recommendation).
- MEDIUM "Misfiled directory `Regression/` vs `DriftCatchers/`" → DEFERRED (cross-file arc covering 6+ peer files).
- MEDIUM "Compound-word regex form" → DEFERRED (watch-item; no real-world drift yet; reverting `\b` re-introduces a known LOW).
- MEDIUM "Sim-startup fixture refactor" → DEFERRED (multi-file arc; see iter-486 entry below for corrected blocker analysis).
- LOW "Skip-message back-reference durability" → DEFERRED (anchor update bundled with next backlog rotation).

## Iter-486 (commit pending) — f18bc78 review drainage (same-file precision)

- MEDIUM "Class-doc archaeology accretion (95-line `<summary>` block)" → RESOLVED via this file (extracted chronological history) + class docstring compacted to ~20 lines (SCOPE-honesty contract + currently-OPEN drift-catcher pointers + reference to this `DRIFT_CATCHER_HISTORY.md`).
- MEDIUM "DEFERRED rationale hand-waves 'sealed V2BridgeAdapter' for sim-startup fixture" → RESOLVED via polish_backlog text correction (the blocker is cross-file scope, not adapter sealing; a shared `IClassFixture<>` calls the same `CreateVm()` regardless of sealing) + in-file iter-485 docstring line tightened.
- LOW "`Assert.Fail` + `[Fact(Skip = ...)]` xunit-version-fragile" → RESOLVED via 1-line doc comment in placeholder body documenting xunit v2/v3 Skip-before-body evaluation order coupling.
- LOW "Invariant #3 `because` wall-of-text" → RESOLVED via trim of (a)/(b) taxonomy (~3 lines shorter; identical signal-to-noise).
- LOW "Regex string escape verification" → NOT-A-DEFECT (closed at filing iter-485).
- LOW "Commit-message count drift" → addressed via pre-commit `git diff --stat` reflex (no code change; pattern-watch only).

## When to update this file

Add a new `## Iter-NNN (commit hash) — N review drainage` section whenever
an iter drains backlog items filed against this test file. Keep entries
terse; the polish_backlog file is the authoritative storage for full
discovery context + remediation discussion.
