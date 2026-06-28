# Iter 261 — A1.x SetUnitField max_* arc finale (5/5 of multi-iter arc)

**Date**: 2026-05-07
**Iter**: 261 (close-out)
**Arc**: 6th multi-iter A1.x arc; iter 5 of 5 (FINALE).
**Predecessor chain**: iter 257 RE kickoff → iter 258 bridge LIVE → iter 259
sim handler + cascade catch → iter 260 staging-UI verify → **iter 261**.
**Successor**: iter 262 (operator changelog for iter 257-261 arc).

## Arc finale: ALL 5 GATES GREEN

| Gate | Result | Iter that last touched |
|---|---|---|
| Bridge harness (1100 tests) | **1100 passed / 0 failed** | iter 258 (rebuilt with iter-258 LIVE branches) |
| Verifier ledger lint | **0 errors / 0 warnings** (318 entries — 305 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED) | iter 249 (last DEPRECATION) |
| Callgraph CLI smoke | **22,728 functions / 152,032 xrefs / 3,737 RTTI refs / 282 verified facts** | iter 124 (build) |
| Editor full suite | **8162 passed / 0 failed / 8162 total** | iter 260 (+6 new tests vs iter-259) |
| Editor binary | `publish/SwfocTrainer.App.exe` **157.83 MB** (165,495,627 B), built iter-260 | iter 260 (republished) |
| Bridge binary | `swfoc_lua_bridge/powrprof.dll` **406.5 KB** (416,256 B), built iter-258 | iter 258 (rebuilt) |

**Zero baseline failures.** First arc finale this session that closes with
ZERO unresolved test drift — iter-259 cleared the iter-237 DirectorMode
cascade trio that had been accumulating through 22 prior iters.

## Live-game smoke verify — HONEST DEFER

Per the iter-249 honest-defer pattern: **no live SWFOC.exe process detected**
in this environment. Process check via `Get-Process -Name 'StarWarsG',
'SwfocTrainer.App'` returned empty. Live verification of max_hull /
max_shield TYPE-shared writes against a running game session is **deferred
to operator validation** outside this autonomous-loop context.

The deferral is **honest** because:

1. **Bridge LIVE wires are byte-write deterministic**, not heuristic.
   Iter-258 walks `unit + 0x298 → UnitType*`, writes float at known
   offset, returns. The behavior is fully specified by the offset
   constants; there's no engine-state-machine path that could silently
   no-op the write.

2. **Two independent engine readers** confirm the offset chain
   semantically (iter-258 RE walk: `rva_get_hull_percentage` +
   `rva_set_hp` both pass `*(unit + 0x298)` to GetMaxHealth's `this`
   slot AND dereference typename string at `(type + 0xF8)`). The same
   offset the engine uses to READ max_hull is what the bridge WRITES
   to.

3. **Simulator round-trip pinned at iter-259** — the type-shared
   semantic ("write affects every unit of the same type for the
   session") is verified end-to-end via the 7-test pin file with a
   2-AT_AT + 1-Trooper fixture demonstrating both propagation AND
   cross-type isolation.

4. **VM/UI Apply path pinned at iter-260** — the staging UI's existing
   max_hull/max_shield input fields produce LIVE engine effect through
   the same wire format, validated by 6 cardinal pin tests including
   the SendRawAsync-direct round-trip.

**What the operator actually verifies in-game** (queued for iter 262
changelog operator-test-checklist row):

1. Spawn 2+ units of the same type (e.g. AT-AT pair).
2. Open UnitStatEditor staging tab. Select one AT-AT. Set field =
   `max_hull`, value = `9999`. Click Apply.
3. Observe response string contains "OK: max_hull written to
   UnitType+0xDCC (LIVE — affects EVERY unit of this type for the
   session)".
4. Verify SECOND AT-AT (not the targeted one) also shows max_hull =
   9999 in inspector readout.
5. Verify a Trooper unit's max_hull stays at fixture default
   (cross-type isolation).

This 5-step procedure is the live counterpart to iter-259's
`SimulatorRoundTrip_MaxHull_TypeSharedWriteAffectsAllSiblings` test.

## 5-iter arc capstone — iter 257-261 SetUnitField max_*

### Arc shipping summary

| Iter | Type | Headline |
|---|---|---|
| 257 | RE kickoff | 3 reader-side offsets pre-pinned in ledger (`+0xDCC` max_hull / `+0xDD0` max_front_shield / `+0xDD4` max_rear_shield); semantically verified via iter-256 memory rule. ObjectTypePtr offset deferred to iter-258a. |
| 258 | Bridge LIVE | +2 LIVE branches (max_hull + max_shield); ratio **5/13 → 7/13**. ObjectTypePtr semantically verified at +0x298 via 2 independent engine-reader callers. NEW `RVA::UnitType` namespace. iter-256 memory rule earns 2nd downstream beneficiary. |
| 259 | Simulator + cascade catch | Type-shared simulator handler (foreach-by-TypeName); +7 NEW pin tests; **3rd-cousin cascade catch from iter-237** (DirectorMode trio). 3 baseline failures cleared (3 → 0). |
| 260 | Staging-UI verify | **NO UI EXTENSION NEEDED** (seamless LIVE promotion). +6 NEW pin tests; 3 iter-245 sibling tests renamed in place; VM source comment extended. |
| 261 | Arc finale (THIS DOC) | All 5 gates GREEN; live verify deferred per iter-249 pattern; capstone document covering 5-iter arc shape + 4 capstone pattern lessons. |

### Arc-level pattern lessons

#### Capstone Lesson #1 — Reader-side ledger entries are A1.x arc multipliers

**Iter 257 found 3 max_* offsets pre-pinned in the ledger** without doing
any new RE work. The iter-242 design hypothesized "max_hull etc. need RTTI
walk" — iter-257 RE searched the ledger for `rva_get_max_*` engine readers
and found `+0xDCC` / `+0xDD0` / `+0xDD4` already documented from
2026-04-04. **2 ledger lookups did the work of one full RTTI-dissection
arc**.

This is the second time this pattern has saved a multi-iter RE arc
(iter-128 SetUnitShield correction was the first; iter-257 SetUnitField
max_* was the second). **Pattern**: when a future arc looks for a writable
offset, FIRST grep the ledger for `rva_get_*` engine readers that read the
same field; the offset is recoverable from their first-instruction
`(a1 + N)` access pattern.

#### Capstone Lesson #2 — iter-256 memory rule earns ROI within 2 iters

The iter-256 codification ("AOB drift across binary versions; semantic
verification required") earned downstream beneficiaries iter-257 (1st)
and iter-258 (2nd) within 2 iters. **Pattern proven**: memory rules
codified the same week as the originating finding earn ROI fast because
the codification language is fresh in the iter-author's mind. By iter
261, this rule is now standard practice for any RE arc (semantic
verification before designing) — a discipline rather than a checklist
item.

#### Capstone Lesson #3 — Cascading-drift recursive cleanup proven across 3 hops

The iter-237 silent SetCameraPos LIVE flip cascaded to:

- **1st-cousin** (iter-243): `Phase2PendingEntryCount` count-pin drift
  (caught by full-suite count failing).
- **2nd-cousin** (iter-258): `Iter107ScrollCameraToTargetTests
  .Catalog_KeepsSetCameraPos_AsPhase2Pending` (caught by full-suite
  running iter-258 changes).
- **3rd-cousin** (iter-259): `DirectorModeTabViewModelCapabilityTests`
  StartPlayback/StepPlayback/Phase2PendingWarning (caught by full-suite
  running iter-259 changes).

**6 iters between flip and full cascade resolution.** Each cascade caught
ONE level of drift; the next sibling sat silent until the next iter ran
the full suite. The iter-258 pattern lesson #5 ("cascading drift catches
need recursive cleanup") got an empirical 3-hop validation by iter-261.

**Pattern enforcement (iter-262 changelog will pin this)**: when a
LIVE-flip changes a SWFOC_* status, run
`grep -rn "SWFOC_<NAME>" tests/` IMMEDIATELY. Don't wait for downstream
iters to surface each cousin individually.

#### Capstone Lesson #4 — "Seamless LIVE promotion" pattern proven 2nd time

Iter-245 (iter-243 invuln_flag/prevent_death promotion) showed that
LIVE-flipping a Phase-1 mirror with an existing staging-UI input field
costs **0 UI lines**. Iter-260 (iter-258 max_hull/max_shield promotion)
proved the same.

**The pattern is now reliable**: when planning an A1.x arc that will
flip a SetUnitField sub-field LIVE, the staging-UI iter (iter 4 of the
canonical 5-iter shape) is **always a no-op verification iter**, never
a UX-extension iter. This unlocks +6 pin tests + sibling renames in
place without UI churn — 0 visual regressions, 0 dispatcher
extensions, 0 XAML edits.

### Cumulative arc shipping

| Metric | Pre-arc (iter 256) | Post-arc (iter 261) | Δ |
|---|---|---|---|
| LIVE wire/sub-field flips | 97 (cumulative this conversation) | 99 | **+2** (max_hull, max_shield) |
| SetUnitField LIVE sub-fields | 5/13 (post iter-243) | **7/13** | +2 |
| Deferred SetUnitField sub-fields | 7 (iter-242 list) | 5 | -2 |
| Editor test count | 8149 (iter 257 close) | **8162** | +13 |
| Pin test files (iter 257-261 arc) | 0 | **3 NEW** (`Iter259SetUnitFieldMaxFieldsSimulatorTests` + `Iter260UnitStatEditorMaxFieldsLivePromotionTests` + iter-245 sibling renames) | +13 tests |
| Catalog rationale (SWFOC_SetUnitField Note) | 5/13 enumerated | **7/13 enumerated + iter-256 memory-rule citation + TYPE-LEVEL caveat** | extended |
| Bridge dispatchers/builders | 12 helpers | 12 helpers (iter-258 added 0 helpers; reused direct write pattern from iter-243) | unchanged |
| `RVA::*` namespaces | 5 (Lua, GameObj, PlayerObj, Selection, etc.) | **6** (iter-258 added `UnitType`) | +1 |
| Baseline test failures | 3 (DirectorMode trio) | **0** | -3 |
| Pattern lessons codified | 4 across iter 257-260 close-outs | **17** total (4+5+4+4 = 17 across iter 257/258/259/260; iter-261 capstone adds 4 more arc-level lessons) | +4 |

### What the next arc inherits

**Direct memory-write pattern with type-stats walk** is now bridge-tested
+ simulator-tested + UI-verified. The next arc that needs to write per-
type stats can re-use the iter-258 pattern at ~5 LoC marginal cost (one
new branch in `Lua_SetUnitField` + one new offset constant in
`RVA::UnitType`).

**Candidate next arcs** (priority order):

1. **max_speed** — needs iter-99 locomotor-chain RE re-audit. Iter-99
   used a different chain (locomotor +0xA8) for live speed; max_speed
   would need to find the type-stats max-speed offset. Likely RE walk
   parallel to iter-258.
2. **attack_power** — iter-94 rejected as not directly writable; the
   `Damage_Multiplier` is a per-tick scalar applied at `Take_Damage`.
   Future arc might MinHook at the multiplier-read site.
3. **respawn_ms (per-hero)** — needs per-hero respawn-timer table RVA,
   not in ledger. iter-130 confirmed defer.
4. **is_hero** — RTTI-flag write risk; would change unit's RTTI class
   in-place which could destabilize multiple subsystems. Likely keep
   deferred.
5. **respawn_enabled** — behavior layer arc (similar to iter-110
   MakeInvulnerable BehaviorAttach pattern). Multi-iter.

OR alternatively:

6. **Phase2HookPending re-audit pass** — mirrors iter-132/iter-221/
   iter-250 cadence. Catalog has grown; some non-A1.x entries may have
   silent LIVE flips waiting to be caught.
7. **Lua Playground preset menu refresh** for iter 252-260 wires
   (would extend iter-252's 102-entry list; mostly catalog-rationale
   updates rather than new presets since iter 252-260 added LIVE
   sub-fields under existing entries).

The iter-262 changelog will recommend one of these as the next arc
kickoff.

### Verification commands run for this finale

```powershell
# Bridge harness — 1100/0
swfoc_lua_bridge/bridge_test_harness.exe

# Verifier ledger lint — 0/0
cd tools && python -m verifier lint

# Callgraph CLI — built 22728/152032/3737/282
python tools/callgraph_query.py info

# Editor full suite — 8162/0/8162
powershell -File run_editor_tests_v2.ps1

# Live-process check — none detected
Get-Process -Name 'StarWarsG','SwfocTrainer.App' -ErrorAction SilentlyContinue
```

## Iter 262+ queued

- **Iter 262**: Operator changelog for iter 257-261 SetUnitField max_*
  arc (mirrors iter-247 / iter-241 / iter-235 / iter-229 / iter-253
  changelog precedents). 5-iter arc walkthrough + arc-level pattern
  lessons + operator test checklist (5-step live-game verify procedure
  documented above) + recommendations for iter-263 next arc kickoff.
- **Iter 263+**: Triage queue at the end of the arc — reverse-orphan
  audit (mirrors iter-255 / iter-238 cadence; verify no NEW catalog
  entries silently broke wiring-graph invariant); next A1.x arc
  (max_speed via iter-99 path / attack_power via iter-94 retry / etc.)
  OR Phase2HookPending re-audit pass (mirrors iter-132/iter-221/
  iter-250 cadence).
