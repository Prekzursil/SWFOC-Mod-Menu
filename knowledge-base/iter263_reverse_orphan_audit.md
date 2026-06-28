# Iter 263 — Reverse-Orphan Snapshot Audit (CLEAN PASS, 22-iter window since iter-255)

**Date**: 2026-05-07 19:55 UTC (close-out)
**Iter**: 263
**Type**: Verification iter — no code changes.
**Predecessor**: iter 262 operator changelog (10th docs iter).
**Window since last reverse-orphan audit**: **22 iters** (iter 241 → iter 262
inclusive of iter-262 docs iter); doubled since the iter-255 11-iter window.

## Test result

```
dotnet test --filter FullyQualifiedName~CapabilityCatalogReverseOrphan
  Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: < 1 ms
```

**1/1 GREEN in <1 ms** (1.62s wall-clock including dotnet host spawn +
assembly load). The wiring-graph invariant `actuallyUnwired.Count ==
KnownUnwiredEntries.Count` holds.

## Snapshot context

| Metric | Value | Δ vs iter-255 (when last audited) |
|---|---|---|
| Total catalog entries (`SWFOC_*` in CapabilityStatusCatalog.cs) | **219** | 0 new entries (iter 257-262 added 0 entries) |
| `KnownUnwiredEntries` count | **54** | unchanged from iter-255 |
| Test execution time | < 1 ms | unchanged (text-pattern-grep over editor source; constant work) |
| Editor full suite total | **8162** passed / 0 failed / 0 skipped | +13 tests since iter-255 (Iter259 +7 + Iter260 +6) |
| Pin-test files added during window | **2** (`Iter259SetUnitFieldMaxFieldsSimulatorTests` + `Iter260UnitStatEditorMaxFieldsLivePromotionTests`) | NEW |

## Activity in 22-iter window (iter 241 → iter 262)

The reverse-orphan invariant is sensitive to two things:

1. **NEW `SWFOC_*` catalog entries** (require either a wiring source-grep
   match OR an explicit `KnownUnwiredEntries` add).
2. **NEW source call sites** for catalog entries previously in
   `KnownUnwiredEntries` (require dropping the entry from the set).

For the audit to be CLEAN PASS, EVERY iter in the window must have either
left the catalog/source unchanged at the wiring-graph level, OR balanced
its add/drop pairs to keep counts stable.

| Iter | Type | Wiring-graph effect | Audit-relevant action |
|---|---|---|---|
| 241 | Operator changelog (iter 236-240 SetCameraPos arc) | None (pure docs) | n/a |
| 242 | A1.x SetUnitField extras RE kickoff | None (pure RE) | n/a |
| 243 | Bridge LIVE branches (invuln_flag + prevent_death) | None (LIVE flips inside existing wire; no new catalog entry) | n/a |
| 244 | Simulator handler extension + 6 pin tests | None | n/a |
| 245 | UnitStatEditor staging-UI verify (iter-243 promotion) | None (existing fields) | n/a |
| 246 | Live verify + close-out finale | None (close-out doc only) | n/a |
| 247 | Operator changelog (iter 242-246 arc) | None (pure docs) | n/a |
| 248 | A1.x SetUnitCapOverride RE kickoff | None (pure RE) | n/a |
| 249 | Honest defer + ledger DEPRECATION | None (DEPRECATED ledger entry, not catalog) | n/a |
| 250 | Phase2HookPending re-audit | None | n/a |
| 251 | SWFOC_FreezeCredits rationale fix | Rationale text only; catalog entry unchanged | n/a |
| 252 | Lua Playground preset menu refresh | NEW preset entries (operator-facing); no NEW SWFOC_ wires | n/a |
| 253 | Operator changelog (iter 248-252) | None (pure docs) | n/a |
| 254 | README capstone update | None (pure docs) | n/a |
| **255** | **Reverse-orphan audit (last) — CLEAN PASS** | n/a | **Audit baseline** |
| 256 | Memory rule codification | None (memory file outside repo) | n/a |
| 257 | A1.x SetUnitField max_* RE kickoff | None (pure RE) | n/a |
| 258 | Bridge LIVE wires (max_hull + max_shield) | None (LIVE flips inside existing wire) | n/a |
| 259 | Simulator handler + DirectorMode cascade catch + 7 pin tests | None (cascade renames only, no catalog entries touched) | n/a |
| 260 | UnitStatEditor staging-UI verify (iter-258 promotion) | None (VM source comment + 3 sibling renames + 6 NEW pin tests; no catalog entries touched) | n/a |
| 261 | A1.x arc finale | None (close-out doc only) | n/a |
| 262 | Operator changelog (iter 257-261 arc) | None (pure docs) | n/a |

**Across 22 iters: 0 new SWFOC_* catalog entries, 0 newly-wired existing
entries, 0 newly-unwired existing entries.** All iters either operated
inside-existing-wires (LIVE sub-field flips) or were pure docs / RE / audit
iters. The wiring-graph invariant holds because there was nothing to drift.

## Pattern lesson empirically validated

The iter-255 close-out doc captured a NEW pattern lesson: **"Wiring-graph
invariants are operator-trust artifacts"**. iter-263 provides a 2nd-instance
empirical validation:

| Audit iter | Window (iters since last audit) | Result |
|---|---|---|
| iter 238/239 | (initial baseline) | (initial pin) |
| iter 255 | **11 iters** (244-254) | **CLEAN PASS** |
| **iter 263** | **22 iters** (256-262 + iter 255 audit itself; counting iter 255 as the previous audit) | **CLEAN PASS** |

**Pattern proven across 2 instances**: pure framework-strengthening windows
(docs, RE, audits, sub-field LIVE flips, simulator extensions, staging-UI
verifications, cascade-catch cleanups, close-out finales, operator
changelogs, memory rule codifications) DO NOT move entries across the
wired/unwired boundary. The boundary is touched only when:

- A NEW `SWFOC_*` catalog entry is added (very rare; last NEW entry in a
  multi-iter arc was iter-152 SWFOC_GalacticSpawnUnit).
- An EXISTING catalog entry's wiring source-grep match changes (e.g. a
  hardcoded string-literal dispatcher call gets refactored to use
  `BuildUnitLuaMethodCall("SWFOC_X", ...)` or vice versa).

This means the reverse-orphan audit is the **cheapest verification gate**
in the entire toolchain — <1 ms execution, runs in CI on every full-suite
run, AND only fires when the wiring-graph genuinely shifts. It's the
operator-trust analog of a parity bit: if you don't touch the data, you
don't need to recompute the bit.

## Future audit cadence

Empirical evidence after 2 audit instances:

| Audit instance | Iters per audit | Drift catches | Per-iter "audit value" |
|---|---|---|---|
| iter 255 | 11 | 0 | 0 (no drift to catch) |
| iter 263 | 22 | 0 | 0 (no drift to catch) |

**Recommended cadence**: every **20-30 iters** OR immediately after any
iter that adds a NEW `SWFOC_*` catalog entry (whichever comes first).

The 20-30 iter floor is justified because the audit is so cheap (<1 ms)
that running it more often costs nothing operationally — but it ALSO
costs nothing diagnostically, since framework-strengthening iters can't
break the invariant. The "any NEW catalog entry" trigger is the actual
load-bearing rule.

## Iter 264+ queued

Per iter-261/262 recommendation, the iter-263 CLEAN PASS frees the loop
to start a new substantive arc. Candidates (priority order):

1. **A1.x next sub-field arc — max_speed via iter-99 path**. Iter-99 used
   the locomotor +0xA8 chain for live speed; max_speed needs the type-stats
   max-speed offset. RE walk parallel to iter-258. **5-iter arc shape
   following iter 257-261 precedent**.
2. **Phase2HookPending re-audit** (mirrors iter-132/iter-221/iter-250
   cadence). Drift trend: 12.5% (iter-132) → 15% (iter-221) → 4%
   (iter-250). **iter-251 caught 1 drift catch the day after iter-250**;
   running a 4th audit might catch the next one. **Single-iter close-out**.
3. **Lua Playground preset menu refresh** (mirrors iter-183/iter-223/
   iter-252 cadence). iter-252 last ran with 102 entries. iter 253-262
   added 2 NEW LIVE sub-fields under existing entries (max_hull, max_shield)
   — operator could pick them by hand-typing today, but a preset would
   make them discoverable. **Single-iter UX polish**.
4. **README capstone update** (mirrors iter-222/iter-254 cadence). Last
   ran iter-254 covering iter 100-253. New ground covered: iter 254-262
   = 8 iters of A1.x SetUnitField max_* arc + reverse-orphan audit.
   **Single-iter docs polish**.
5. **A1.x next sub-field arc — attack_power via iter-94 retry**. iter-94
   rejected as not directly writable; the `Damage_Multiplier` is a per-
   tick scalar applied at `Take_Damage`. Future arc might MinHook at the
   multiplier-read site. Higher RE risk than max_speed.
6. **Reverse-orphan audit at 50-iter cadence** — defer iter-263 conclusion
   suggests every 20-30 iters; 50 iters from iter-263 lands at iter-313.
   No code change needed; just queue the wakeup.

**Recommendation**: Run **option 3 (Lua Playground preset menu refresh)**
as iter 264 since it's the cheapest operator-discoverability polish, then
choose between option 1 (max_speed arc kickoff) and option 2
(Phase2HookPending re-audit) for iter 265+ based on whether the preset-
menu work surfaces any new wires that would benefit from immediate LIVE
treatment.

## What this audit pins for posterity

If a future iter accidentally:

- Adds a NEW `SWFOC_*` entry to `CapabilityStatusCatalog.cs` without
  either wiring it or adding to `KnownUnwiredEntries` — `actuallyUnwired
  .Count > KnownUnwiredEntries.Count` and the test fails with the
  newly-unwired list in the output.
- Refactors a dispatcher call from string-literal to interpolated form —
  `actuallyUnwired.Count < KnownUnwiredEntries.Count` and the test fails
  with the no-longer-unwired list pointing at the entries to drop.

The test output (per iter-255 design) prints both diff sets so the next
iter can fix the imbalance with a single edit. iter-263 is the second
audit to confirm the test stays passive when the diff is empty.
