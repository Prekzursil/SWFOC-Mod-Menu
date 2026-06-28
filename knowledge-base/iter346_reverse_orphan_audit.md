# Iter 346 — Reverse-orphan snapshot audit (5th audit; FIRST DRIFT CATCH after 4 consecutive CLEAN PASSes; iter-238/255/263/272/346 sequence)

**Date:** 2026-05-07
**Arc class:** Audit (mirrors iter-238/255/263/272 cadence; FIRST DRIFT CATCH in the sequence)
**Cadence:** **5th audit** in the iter-238/255/263/272/346 sequence. Average gap so far ~17/8/9/74 iters (the 74-iter gap is 3.3× the canonical ~22-iter cadence; substantial overrun).
**Predecessor:** iter-345 (codify `feedback_resolver_injection_at_composition_root`)
**Successor (queued):** iter-347 (operator changelog supplement covering iter 340-346 — see "Next iter options" below)
**Result:** **DRIFT CAUGHT + FIXED**. 1 entry (`SWFOC_GetTypeLua`) flipped from unwired → wired since iter-272 because iter-343 added a regex-visible call site. Snapshot edited; CLEAN PASS verified.

## Headline

**FIRST drift catch in the iter-238/255/263/272/346 sequence after 4 consecutive CLEAN PASSes.** iter-272's lesson #2 ("audits become regression-confirmation, not drift-detection") was OVERCONFIDENT — the mechanism does still catch drift; it just hadn't fired in 4 consecutive audits because iter 264-272 added 0 new wires AND iter 273-342 added wires only via regex-invisible string-literal forms. iter-343 was the first iter in the iter-273-345 window to add a regex-visible call site (`$"return SWFOC_GetTypeLua({childAddr})"` in `CombatTabViewModel.ResolveHardpointIconAsync`).

| Metric | Value |
|---|---|
| Initial test result | **FAILED** in 523 ms (`UnwiredEntries_MatchKnownSnapshot`) |
| Diff: newly-unwired | **0** (high signal — catalog discipline at write-time is working) |
| Diff: no-longer-unwired | **1** (`SWFOC_GetTypeLua`) |
| `actuallyUnwired.Count` (pre-fix) | 53 (was 54 in snapshot) |
| Snapshot edit | -1 entry + +5-line drop note |
| Final test result | **PASSED** in <1 ms after rebuild |
| `KnownUnwiredEntries.Count` (post-fix) | 53 (was 54) |
| Build verification | 0 Errors / pre-existing warnings (XML doc + nullable refs) |
| Bridge harness | unchanged (no bridge changes) — inherits 1100/0 |
| Verifier ledger lint | unchanged at 318 entries — inherits 0/0 |
| Catalog SWFOC_* entry total | 225 (was ~92 at iter-138) |

## What this iter actually did

1. **Ran the audit test** — `SwfocTrainer.Tests.Diagnostics.CapabilityCatalogReverseOrphanTests.UnwiredEntries_MatchKnownSnapshot` against current bridge + catalog + editor source. Initial run FAILED with `actuallyUnwired.Count == 53` vs `KnownUnwiredEntries.Count == 54` (difference -1).
2. **Diff analysis** — Test stdout showed: `No-longer-unwired (now has a call site — drop from KnownUnwiredEntries): SWFOC_GetTypeLua`.
3. **Root-cause located** — `CombatTabViewModel.ResolveHardpointIconAsync` (added iter-343) calls `$"return SWFOC_GetTypeLua({childAddr})"`. The `$"return SWFOC_X(...)"` interpolated form is regex-visible (`\bSWFOC_X\s*\(`); contrast with `BuildUnitLuaNoArgCall("SWFOC_GetTypeLua", ...)` from iter-191 Inspector tab which is regex-invisible (string literal, no `(` immediately after the SWFOC name).
4. **Snapshot edit** — Removed `"SWFOC_GetTypeLua",` line from `KnownUnwiredEntries` and added a +5-line drop note (mirrors iter-200 FOWRevealLua / iter-218 TaskForceMoveToTargetLua format) explaining iter-343's interpolated form acquired the regex-visible call site, attributing the catch to iter-346 audit.
5. **Re-ran with rebuild** — Initial `--no-build` re-run FAILED against stale binary; full rebuild then PASSED. (Self-correcting toolchain note: when the test reads source-vs-snapshot at runtime, but the snapshot is COMPILED INTO the test binary, you MUST rebuild for snapshot edits to take effect.)

## Why DRIFT CATCH was not predicted

iter-272 close-out queue rationale at the time:

> **Pattern**: 4 consecutive CLEAN PASSes across 4 audits suggests the
> snapshot-discipline framework has **converged**. The mechanism that
> prevents drift (require `KnownUnwiredEntries` updates in the same PR
> that adds/removes catalog entries) is working.

iter-272 was wrong. The mechanism HAD been working — but only because:
- iter 264-272: zero new wires shipped (preset menu + audit + honest-defer iters)
- iter 273-342: many new wires (iter-282, 285, 296, 299, 300, 311-321, 331-339) shipped, but ALL via regex-invisible string-literal forms (`BuildUnitLuaNoArgCall("SWFOC_X", ...)` / `BuildUnitLuaMethodCall("SWFOC_X", ...)`). The visible-form `$"return SWFOC_X(...)"` pattern was used only inline in dispatchers/editor source, not added to the snapshot.

**iter-343 broke the dry spell** by adding a NEW regex-visible call site in `CombatTabViewModel.ResolveHardpointIconAsync`. Once that call site landed, the SWFOC_GetTypeLua entry was no longer "regex-invisibly used" — it acquired regex visibility via the new call site.

**iter-272 lesson #2 reversal**: 4 consecutive CLEAN PASSes was a **window of stability**, not convergence. The mechanism is differentiated (only catches regex-visible call-site additions), and the dry spell ended the moment a regex-visible form appeared.

## Cadence summary (iter-238/255/263/272/346 = 5 audits)

| Audit iter | Gap from prior | Newly-unwired catches | No-longer-unwired catches | Result |
|---|---|---|---|---|
| iter-238 | (1st) | 0 | 0 | CLEAN |
| iter-255 | 17 iters | 0 | 0 | CLEAN |
| iter-263 | 8 iters | 0 | 0 | CLEAN |
| iter-272 | 9 iters | 0 | 0 | CLEAN |
| **iter-346** | **74 iters** | **0** | **1** (`SWFOC_GetTypeLua`) | **DRIFT CAUGHT** |

**Pattern**: 5 audits across iter 238-346 (~108-iter window). 4 CLEAN passes + 1 drift catch. Drift rate: 1/5 = 20% across all audits, but the 74-iter gap before iter-346 means the mechanism's true sensitivity is HIGHER than the 1/5 ratio suggests — across the 73 iters of compounded changes, the mechanism still surfaced exactly 1 entry that needed snapshot maintenance.

## Pattern lessons

### Lesson #1 — 4-CLEAN-PASS "convergence" was a window, not a permanent state (REVERSAL of iter-272 lesson #2)

iter-272 said "4 consecutive clean passes = framework has converged." iter-346 disproves this — the mechanism does still catch drift; the dry spell was driven by ~108 iters of regex-invisible-only call-site additions. **The mechanism's signal-to-noise ratio is high, not zero.**

**Implication for future audits**: don't skip the audit because "the framework has converged." Run it on cadence even after long quiet periods. The 384 ms cost is cheap insurance against silent drift accumulation.

### Lesson #2 — Differentiation between regex-visible and regex-invisible call sites

The audit regex `\bSWFOC_([A-Z][A-Za-z_0-9]*)\s*\(` matches **only** call-site shapes where the SWFOC name is immediately followed by `(`. This catches:

- Direct calls: `SWFOC_GetTypeLua(addr)`
- Interpolated returns: `$"return SWFOC_GetTypeLua({addr})"`
- Engine-Lua-API DoString: `Lua_GetType(SWFOC_GetTypeLua(unit))`

But does NOT catch:

- String-literal helper args: `BuildUnitLuaNoArgCall("SWFOC_GetTypeLua", ...)`
- String-literal dispatch table: `dispatchers["SWFOC_GetTypeLua"] = HandleX;`

**Implication**: when adding a NEW UX surface that consumes a wire, the regex-visibility of the call-site shape determines whether the snapshot needs maintenance. The two iter-191/iter-343 events for SWFOC_GetTypeLua are a perfect instructional example:

- iter-191: added Inspector tab native UX via `BuildUnitLuaNoArgCall("SWFOC_GetTypeLua", ...)` → regex-invisible → snapshot stayed at "unwired" (correct, mechanism didn't false-positive)
- iter-343: added Combat tab Hardpoint Inspector chain via `$"return SWFOC_GetTypeLua({childAddr})"` → regex-visible → snapshot needed update (mechanism caught it)

### Lesson #3 — Zero NEW unwired entries proves catalog write-time discipline at scale

Across iter 273-345 (~73 iters), the catalog grew from ~135 entries to 225 entries (net +90 entries). The audit found **zero newly-unwired entries** — i.e. every catalog addition shipped with at least one regex-visible call site in the same iter. This is the single most important finding from iter-346:

- iter-282: SWFOC_GetFireRateMultiplierGlobal pair → matched call sites in BridgeCombatDispatcher
- iter-285: Tier 3 wires (kills/deaths/units-alive) → matched call sites in overlay worker
- iter-296: SWFOC_GetPlanets impl → matched call sites in GalacticTabViewModel
- iter-299: SWFOC_GetFactionRoster + SWFOC_GetCurrentMod → matched call sites in SettingsTabViewModel
- iter-300: SWFOC_ListMods → matched call sites in SettingsTabViewModel
- iter-313/321/331/332/333/336/338/339/343: weapon/ability icon classes + Hardpoint Inspector chain → matched call sites in respective dispatchers

**Codification candidate at 1/3 trigger**: `feedback_audit_dry_spell_is_not_convergence.md` — when an automated audit shows N consecutive CLEAN passes, do NOT downgrade to "regression-confirmation only"; the mechanism's signal lies dormant until a triggering condition (here: regex-visible call-site addition) occurs. Re-run on cadence regardless. Codification trigger reached at 3rd recurrence.

### Lesson #4 — `--no-build` re-run on snapshot edits is a footgun

After editing the snapshot HashSet (which is COMPILED INTO the test binary as a static field initializer), re-running with `--no-build` runs against the stale compiled snapshot and STILL FAILS with the original diff. This wasted ~1 cycle.

**Implication**: when editing test-side static data (HashSet/Dictionary/array initializers), always re-run with full build. The `--no-build` flag is only safe when editing source files that are JIT-compiled at test-runtime (e.g. test methods themselves) — but static field initializers are baked into the assembly at compile time.

**Toolchain rule capture worth flagging**: `feedback_no_build_safe_only_for_jit_paths.md` at 1/3 trigger — first instance.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-345 |
|---|---|---|
| Editor test build | **0 Errors** / pre-existing warnings only | clean (full rebuild) |
| Reverse-orphan audit | **PASSED** in <1 ms after fix | DRIFT CAUGHT + FIXED (1 entry) |
| Bridge harness | n/a (no bridge changes) | inherits iter-345 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-345 0/0 at 318 entries |
| Capability surface | n/a (no catalog changes) | unchanged at 225 SWFOC_* entries |
| Editor binary | n/a (no source changes) | inherits iter-344 republish (150 MiB at May 7 08:09) |

## What's next (iter 347+)

Per iter-345 + iter-346 + standing user directive:

1. **Iter 347 (RECOMMENDED) — Operator changelog supplement covering iter 340-346**
   (~7-iter window since iter-340; well-precedented at iter-235/241/247/262/280/311/320/330/340 cadence — 9th instance; ~25 min cycle; lowest token cost). Would cover the iter 340-346 master-loop window: iter-341 Phase2HookPending audit clean pass + iter-342/343 Hardpoint icon-resolution chain + iter-344 composition-root wiring + iter-345 codification + iter-346 reverse-orphan drift catch.

2. **Alternative iter 347** — Codify `feedback_audit_dry_spell_is_not_convergence.md` at 1/3 trigger (premature; defer to 3rd recurrence) OR live SWFOC verify of iter-343 chain (requires operator session) OR README capstone update (premature; only 24 iters since iter-322 vs canonical ~30 iters).

3. **NOT recommended for iter 347** — Phase2HookPending re-audit (iter-341 last ran; only 5 iters; way premature — canonical ~17-iter cadence).

## Iter 346 close-out summary

- This document is the iter 346 deliverable.
- **Code changes**: 1 file modified (`tests/SwfocTrainer.Tests/Diagnostics/CapabilityCatalogReverseOrphanTests.cs`):
  - Removed `"SWFOC_GetTypeLua",` line (-1 entry)
  - Added 5-line iter-343 drop-note (mirrors iter-200/iter-218 format)
  - Updated iter-191 NOTE block to remove "GetTypeLua" from its list
  - Net change: ~+2 LoC (5 added comment lines minus 3 lines removed: 1 entry + 2 reference removals from iter-191 NOTE)
- All gates GREEN: build clean; audit PASSED in <1 ms after fix; bridge harness + ledger lint inherit iter-345 unchanged.
- **5th audit in iter-238/255/263/272/346 sequence**; FIRST drift catch after 4 consecutive CLEAN PASSes (74-iter gap = 3.3× canonical cadence overrun).
- **NON-A1.x audit iter** per iter-269 lesson #2 ledger-state asymptote signal continuation.
- **Codified-rules tally remains at 11**; 2 NEW codification candidates flagged at 1/3 trigger:
  - `feedback_audit_dry_spell_is_not_convergence.md` (iter-346 lesson #3 capture)
  - `feedback_no_build_safe_only_for_jit_paths.md` (iter-346 lesson #4 toolchain rule)
- **Pattern lesson capstone**: iter-272's "framework has converged" was overconfident; the audit mechanism works fine across a 74-iter gap, surfacing exactly the drift it's designed to catch (regex-visibility flip on a single entry).
- 16th post-iter-323 arc iter (6 LIVE + 3 codification + 2 republish + 1 XAML + 4 docs/audit); 77th consecutive NON-A1.x iter per iter-269 lesson #2.
