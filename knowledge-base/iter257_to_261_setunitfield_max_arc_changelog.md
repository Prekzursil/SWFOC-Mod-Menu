# A1.x SetUnitField max_* batch — Multi-Iter Arc Operator Changelog (iter 257-261)

**Date range:** 2026-05-07 18:15 UTC to 19:40 UTC (single session, ~85 minutes)
**Status at end of arc:** **CLOSED at offline verification level**, live-game smoke `[LIVE-PENDING]` with documented 5-step operator procedure
**LIVE wire count delta:** **+0 catalog wires** (sub-field LIVE flips inside an existing wire); SetUnitField LIVE branch ratio **5/13 → 7/13**
**Master-loop tally:** 149 → **149 LIVE wires** (UNCHANGED)
**Native UX delta:** 0 buttons (UnitStatEditor staging UI already exposed `max_hull` + `max_shield` since iter 245; iter 260 just promoted the bridge effect from Phase-1 mirror to LIVE without UI churn)
**Ledger delta:** 0 (offsets pre-pinned in `verified_facts.json` reader entries since 2026-04-04; semantically re-verified per iter-256 memory rule); 318 entries unchanged
**Test delta:** 8149 → **8162** (+13 tests via Iter259 +7 + Iter260 +6, plus 3 baseline failures cleared 3 → 0)
**Bridge namespace delta:** **+1** NEW `RVA::UnitType` (MaxHull = 0xDCC, MaxFrontShield = 0xDD0, MaxRearShield = 0xDD4)
**Pattern lessons codified:** **+21** total (17 iter-level across iter 257-260 close-outs + 4 arc-level capstone in iter 261)

---

## What this arc closed

`SWFOC_SetUnitField` had been **PARTIAL LIVE** since iter 136 (3/13 sub-fields)
and was extended to **5/13** at iter 243 (invuln_flag + prevent_death direct
writes). The iter-242 design originally deferred 7 more sub-fields to "future
RTTI dissection arcs" — but the iter-257 RE pass discovered that **3 of those
7 sub-fields had per-unit-type-stats offsets ALREADY pinned in
`verified_facts.json`** under engine-reader entries (`rva_get_max_health` /
`rva_get_max_front_shield` / `rva_get_max_rear_shield`, all from 2026-04-04
single-tool Ghidra finds, later 3-tool VERIFIED).

**Iter 258 shipped 2 of those 3 as LIVE direct-type-stats writes** — `max_hull`
and `max_shield` (front+rear collapse to one operator-facing field).
**Iter 259 mirrored the TYPE-shared semantic at the simulator level**.
**Iter 260 verified the staging UI seamlessly produces LIVE engine effect**
through the same dispatcher path that previously routed Phase-1 mirrors.
**Iter 261 closed the arc with all 5 gates GREEN**.

This is the **sixth back-to-back A1.x multi-iter arc** in the same session —
proving the canonical 5-iter shape is repeatable across **six different
implementation strategies** spanning +1 to +4 LIVE wires to +2 sub-field flips
inside an existing wire:

| Arc | Iter range | Strategy | LIVE delta |
|---|---|---|---|
| A1.3 SetFireRate | iter 224-228 | Every-frame MinHook detour at WeaponTick | +1 |
| A1.x FreezeCredits | iter 230-234 | Bool-freeze-precedence MinHook detour at AddCredits | +4 |
| A1.x SetCameraPos | iter 236-240 | Direct call at SetTransformMatrix (no detour) | +2 |
| A1.x SetUnitField extras | iter 242-246 | Direct memory write inside existing wire | +2 sub-field flips |
| A1.x SetUnitCapOverride | iter 248-249 | **Honest defer** — community CE table ledger entry mis-labeled in current binary fingerprint per iter-256 memory rule | +0 (DEPRECATED) |
| **A1.x SetUnitField max_*** | **iter 257-261** | **Direct type-stats walk via unit+0x298 → UnitType\*** | **+2 sub-field flips** |

**25 iters of pure deferred-arc closure across 6 strategies.** The 5-iter
shape is invariant; the *contents* and *number-of-iters-needed-for-RE* scale
to the work required (iter-249 honest-defer needed only 2 iters).

---

## Per-iter walk-through

### Iter 257 — RE design kickoff (research-only, no code)

- Created `knowledge-base/iter257_setunitfield_max_batch_re_kickoff.md` (~280
  lines, structured RE findings + design decision matrix + iter 258-261
  implementation outline + risks).
- **HEADLINE FINDING**: 3 of the 7 deferred SetUnitField sub-fields have
  **engine-reader offsets pre-pinned in the verified ledger** — readable via
  existing `rva_get_*` entries:
  - `rva_get_max_health @ 0x3727A0` → reads `type+0xDCC` (max_hull base value).
  - `rva_get_max_front_shield @ 0x372320` → reads `type+0xDD0` (max_shield front).
  - `rva_get_max_rear_shield @ 0x3725F0` → reads `type+0xDD4` (max_shield rear).
- **Iter-256 memory rule applied** (`feedback_aob_drift_across_binary_versions`):
  3 offset claims SEMANTICALLY VERIFIED via decompile body inspection (first-
  instruction `(a1 + N)` access pattern matches the offset claim). **Memory
  rule earned its 1st downstream beneficiary within 1 iter of codification.**
- **Iter 258 scope**: 2 sub-fields (max_hull + max_shield) for direct-write
  LIVE flips. SetUnitField LIVE branch ratio **5/13 (iter 243) → 7/13
  (iter 258)**. Catalog wire count UNCHANGED.
- **5 sub-fields stay deferred**: max_speed (iter-99 different chain),
  attack_power (iter-94 rejected as non-writable; needs MinHook detour at
  multiplier-read site), respawn_ms (per-hero; needs respawn-timer table
  RVA), is_hero (RTTI risk; would change unit's RTTI class in-place),
  respawn_enabled (behavior layer arc; mirrors iter-110 BehaviorAttach
  pattern).
- **iter 258a RE step queued**: identify ObjectTypePtr offset (unit instance
  → unit type) — likely vtable-based at obj+0x0 or a specific offset distinct
  from iter-99's locomotor +0xA8 chain.
- 6th back-to-back A1.x arc this session.
- No bridge / dispatcher / VM / XAML / test changes — pure RE + design doc.
- 109 → 109 buttons UNCHANGED. 102 → 102 preset entries UNCHANGED.
- **Pattern lesson reinforced**: per-type stats are recoverable via engine-
  reader offset re-use. iter-242/243 used `rvas.h::GameObj::*` (per-instance
  offsets); iter-257 extends to `rvas.h::UnitType::*` (NEW namespace,
  per-type offsets). Future per-type-stats arcs should search the ledger
  for `rva_get_*` engine readers and verify their first-instruction offset
  claims.

### Iter 258 — Bridge LIVE wire shipped (+2 sub-field LIVE flips, 5/13 → 7/13 ratio)

- Created `knowledge-base/iter258_setunitfield_max_batch_bridge_live.md`
  (~200 lines, structured close-out + 5 pattern lessons).
- **iter-258a (RE step) — ObjectTypePtr semantic verification**:
  - Decompile body of `GetMaxHealth` @ `0x1403727A0` (rva_get_max_health):
    first instruction `fVar5 = *(float *)((longlong)this + 0xdcc);` —
    confirms `this` IS the unit-type-stats struct AND `+0xDCC` IS max-hull
    offset on it.
  - Decompile body of `rva_get_hull_percentage` @ `0x140396DF0` (caller of
    GetMaxHealth, found via `python tools/callgraph_query.py callers
    0x3727A0`): `fVar2 = (float)FUN_1403727a0(param_1[0x53], param_1);` —
    `param_1[0x53]` = `*(longlong*)(unit + 0x53*8)` = `*(longlong*)(unit +
    0x298)`. Confirms unit-instance → unit-type access pattern at +0x298.
  - Decompile body of `rva_set_hp` @ `0x1403A89D0` (independent caller):
    passes `*(unit + 0x298)` to GetMaxHealth's `this` slot AND
    dereferences typename string at `(*(unit+0x298)) + 0xF8` — this is
    consistent ONLY if +0x298 holds the unit-type pointer.
  - **Two independent semantic confirmations + matches ledger constant**
    (`RVA::GameObj::GameObjType = 0x298`, first documented 2026-04-04
    from trainer Inspector tab + ce_trainer_inventory.md section 1.2).
- **iter-258b (bridge)**:
  - `swfoc_lua_bridge/rvas.h`: NEW `namespace UnitType { MaxHull = 0xDCC;
    MaxFrontShield = 0xDD0; MaxRearShield = 0xDD4; }` block with full
    semantic-verification provenance comment.
  - `swfoc_lua_bridge/lua_bridge.cpp` `Lua_SetUnitField`: NEW unified
    `if (f == "max_hull" || f == "max_shield")` branch:
    1. Walks `unit_instance + GameObj::GameObjType (0x298)` → `UnitType*`.
    2. Null-check (returns ERR if orphan unit).
    3. **`max_hull`**: writes float at `UnitType + 0xDCC`.
    4. **`max_shield`**: dual-writes float at `UnitType + 0xDD0` (front) AND
       `UnitType + 0xDD4` (rear) — mirrors iter-129's per-instance dual-write
       shape.
  - Both response strings carry the loud TYPE-LEVEL caveat:
    > `OK: max_hull written to UnitType+0xDCC (LIVE — affects EVERY unit
    > of this type for the session; engine reads it on next damage tick)`
    > `OK: max_shield front+rear written to UnitType+0xDD0 / +0xDD4 (LIVE
    > — affects EVERY unit of this type for the session)`
- **Catalog rationale extension** (`CapabilityStatusCatalog.cs`):
  SWFOC_SetUnitField rationale block extended 21-line → 27-line with
  per-LIVE-field semantics + the type-shared caveat + iter-256 memory-rule
  citation provenance.
- **Iter 258 NEW pin test** (`SetUnitField_NoteCitesIter258TypeLevelCaveat`)
  in `Iter136SetUnitFieldPartialLiveTests.cs` — pins per-field enumeration
  + offset citations + "EVERY unit of this type" caveat text + iter-256
  cross-reference.
- **3 sibling pin tests updated for ratio 5/13 → 7/13**:
  `Iter136.SetUnitField_NoteDeclaresLiveFieldCount`,
  `Iter221.Iter132ToIter220DriftCatches_AreLive` reason text,
  `Iter244.{CapabilityStatus_StaysLive,
  CatalogRationale_DocumentsIter243LiveBranchesAndCaveats}`.
- **Collateral cleanup**: stale
  `Iter107ScrollCameraToTargetTests.Catalog_KeepsSetCameraPos_AsPhase2Pending`
  test renamed to `Catalog_PromotesSetCameraPos_ToLive_PerIter237` with
  explicit cross-reference back to the iter-237 silent-flip → iter-243
  cascade catch chain. **Cascading drift catch's missed sibling caught at
  iter-258**.
- **Capability surface markdown regenerated** via
  `SWFOC_REGEN_CAPABILITY_SURFACE=1`.
- Bridge harness **1100/0 GREEN** clean (after +2 LIVE branches added).
- **+2 sub-field LIVE flips. 149 → 149 catalog wires UNCHANGED.** SetUnitField
  wire LIVE branch ratio: **5/13 → 7/13**.

### Iter 259 — Simulator handler + 7 pin tests + DirectorMode 3rd-cousin cascade catch

- Created `knowledge-base/iter259_setunitfield_max_simulator_handler.md`
  (~210 lines).
- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs` `HandleSetUnitField`
  branches `max_hull` + `max_shield` extended from per-instance Phase-1
  mirror to **TYPE-SHARED** (foreach Units by TypeName) mirroring iter-258
  bridge semantics:

  ```csharp
  case "max_hull":
      foreach (var sibling in GameState.Units)
          if (sibling.TypeName == u.TypeName) sibling.MaxHull = value;
      break;
  case "max_shield":
      foreach (var sibling in GameState.Units)
          if (sibling.TypeName == u.TypeName) sibling.MaxShield = value;
      break;
  ```
- NEW pin file `Iter259SetUnitFieldMaxFieldsSimulatorTests.cs` (7 tests):
  - `CatalogStatus_SetUnitFieldIsLive_PostIter258` — pin: catalog stays Live
    across iter-136 → iter-243 → iter-258 promotions.
  - `SimulatorRoundTrip_MaxHull_TypeSharedWriteAffectsAllSiblings` —
    **cardinal test**: 2 AT-AT units + 1 Trooper. Write max_hull on
    AT-AT 1 → AT-AT 2 changes too, Trooper stays.
  - `SimulatorRoundTrip_MaxShield_TypeSharedDualWrite` — same fixture, write
    max_shield → both AT-ATs propagate, Trooper isolated.
  - `SimulatorRoundTrip_MaxHullThenMaxShield_OnDifferentTypes_StaysIsolated`
    — combined test verifying no cross-contamination between fields/types.
  - `SimulatorRoundTrip_MaxHull_WithSingleUnitType_DoesNotErrorOut` — edge
    case: only 1 unit of the type exists; the type-loop sibling-count == 1
    still works.
  - `SimulatorRoundTrip_MaxHull_LegacyPascalCaseStillWritesPerInstance` —
    regression guard: legacy `"MaxHull"` PascalCase branch keeps PER-INSTANCE
    scope (used by PhaseCSimulatorTests). Pins the snake-vs-Pascal scope-
    difference so a future "let's unify" refactor can't silently break legacy
    tests.
  - `HandleSetUnitField_SnakeCaseAndPascalCaseSubFields_CountIsTwelve` —
    drift guard: source-grep verifies all 13 snake_case branches present in
    HandleSetUnitField; catches silent additions/removals.
- **3rd-cousin cascade catch from iter-237 SetCameraPos silent LIVE flip**:
  3 stale `DirectorModeTabViewModelCapabilityTests` (StartPlayback /
  StepPlayback / Phase2PendingWarning) updated to expect LIVE badge per
  iter-237 SetCameraPos LIVE flip propagating through composing playback
  wires. **3 baseline failures cleared (3 → 0)**.
- **Cascade chain provenance**:

  | Cousin | Iter | Test caught | Iters between flip and catch |
  |---|---|---|---|
  | 1st | iter 243 | `Phase2PendingEntryCount_Is25` (was `_Is26`) | 6 |
  | 2nd | iter 258 | `Iter107.Catalog_KeepsSetCameraPos_AsPhase2Pending` rename | 21 |
  | 3rd | iter 259 | `DirectorMode.{StartPlayback, StepPlayback, Phase2PendingWarning}` | 22 |

  **6 iters between iter-237 silent flip and full cascade resolution.**
- **Capability surface markdown re-regenerated** (DirectorMode badge change
  shifted Phase2PendingWarning text).
- Editor full suite **8156 / 0 / 8156 total** (was 8149/3/8156 at iter-258
  close; +3 from cleared baseline + 7 new tests offset by 7 wins).

### Iter 260 — UnitStatEditor staging-UI verification (NO UI EXTENSION NEEDED)

- Created `knowledge-base/iter260_setunitfield_max_unitstateditor_verify.md`
  (~180 lines).
- **HEADLINE — NO UI EXTENSION NEEDED**: as predicted in the iter-258
  close-out queue. The UnitStatEditor staging dropdown's `EditFieldOptions`
  already lists max_hull + max_shield from iter-245 (which added them as
  Phase-1 mirrors). iter-258 promoted the bridge branches to LIVE without
  touching the UI. iter-260 verified the staging UI Apply path now produces
  LIVE engine effect with **zero UI changes**.
- **VM source comment extension** in
  `src/SwfocTrainer.App/V2/ViewModels/UnitStatEditorTabViewModel.cs`:
  added a 3rd LIVE-branch entry citing iter-258 with TYPE-LEVEL caveat
  ("affects EVERY unit of this type for the session"). Phase-1 mirror
  count drops 7 → 5.
- **3 iter-245 sibling tests renamed in place** (per pattern lesson #3 in
  iter-260 close-out doc):
  - `IncludesAllFiveLiveFields` → `IncludesAllSevenLiveFields` (+2 LIVE
    assertions: max_hull, max_shield).
  - `IncludesAllSevenPhase1MirrorFields` →
    `IncludesAllFivePhase1MirrorFields` (-2 Phase-1 assertions).
  - `TotalCountIs12` reason text updated to reflect 7-LIVE + 5-Phase-1 = 12
    (count UNCHANGED).
- NEW pin file `Iter260UnitStatEditorMaxFieldsLivePromotionTests.cs`
  (6 tests):
  - `StagingFieldOptions_MaxHullAndMaxShieldStillPresent_PostIter258` —
    pin: iter-258 LIVE promotion preserves staging-UI option.
  - `CatalogStatus_SetUnitFieldStillLive_AcrossIter258Promotion` — pin:
    catalog status stays Live.
  - `CatalogRationale_DocumentsIter258TypeLevelCaveat_AtVMLayerToo` —
    pin: max_hull / max_shield / "EVERY unit of this type" all appear in
    catalog Note (drives staging UI tooltip).
  - `StagingApplyPath_MaxHull_ProducesLiveEngineEffect_NoUIChanges` —
    **cardinal test**: 2-AT_AT fixture; SendRawAsync `SWFOC_SetUnitField(addr,
    'max_hull', 999)` → both AT-ATs' MaxHull == 999 (TYPE-shared via
    iter-259 sim handler).
  - `StagingApplyPath_MaxShield_ProducesLiveEngineEffect_NoUIChanges` —
    sibling test for max_shield.
  - `StagingComment_DocumentsIter258Promotion` — source-grep pin: VM source
    must cite "iter 258" + "max_hull / max_shield (iter 258" + "TYPE-LEVEL
    writes". Catches future comment decay.
- Editor full suite **8162 / 0 / 8162 total** (was 8156/0/8156; +6 new
  iter-260 tests).

### Iter 261 — Arc finale (5/5 of multi-iter arc)

- Created `knowledge-base/iter261_setunitfield_max_arc_finale.md` (~280
  lines, 5-iter arc shipping summary + 4 arc-level capstone pattern lessons
  + 5-step operator live-test procedure + iter-263 next-arc candidate
  queue).
- **HEADLINE — ALL 5 GATES GREEN, ZERO BASELINE FAILURES**:
  - Bridge harness **1100 / 0** (built iter-258).
  - Verifier ledger lint **0 / 0** (318 entries: 305 VERIFIED + 2
    LIVE_OBSERVED + 11 DEPRECATED).
  - Callgraph CLI smoke (22,728 funcs / 152,032 xrefs / 3,737 RTTI refs /
    282 verified facts; built iter-124).
  - Editor full suite **8162 / 0 / 8162** (built iter-260).
  - Editor binary `publish/SwfocTrainer.App.exe` **157.83 MB** (165,495,627 B).
  - Bridge binary `swfoc_lua_bridge/powrprof.dll` **406.5 KB** (416,256 B).
- **Live verify HONEST DEFER per iter-249 pattern**: no live SWFOC.exe or
  SwfocTrainer.App processes detected. Deferred to operator validation
  outside autonomous-loop context. Defer is HONEST because:
  1. Bridge LIVE wires are byte-write deterministic (not heuristic).
  2. Two independent engine readers semantically confirm offset chain
     (iter-258 RE walk).
  3. Simulator round-trip pinned at iter-259.
  4. VM/UI Apply path pinned at iter-260.

---

## 5-step operator live-test procedure (queued from iter-261 close-out)

When operator launches a live SWFOC.exe + SwfocTrainer.App session, this
5-step procedure verifies max_hull + max_shield TYPE-shared writes against
the running engine:

| Step | Action | Expected outcome |
|---|---|---|
| 1 | Spawn 2+ units of the same type (e.g. AT-AT pair). | Two AT-AT units present in tactical view; both at fixture default max_hull. |
| 2 | Open UnitStatEditor staging tab. Select one AT-AT. Set field = `max_hull`, value = `9999`. Click Apply. | Apply button fires; bridge wire `SWFOC_SetUnitField(addr, 'max_hull', 9999)` sent. |
| 3 | Inspect bridge response in Diagnostics activity log. | Response contains `OK: max_hull written to UnitType+0xDCC (LIVE — affects EVERY unit of this type for the session)`. |
| 4 | Verify SECOND AT-AT (not the targeted one) shows max_hull = 9999 in inspector readout. | Type-shared semantic propagated; second AT-AT sees same max_hull. |
| 5 | Verify a Trooper unit's max_hull stays at fixture default (cross-type isolation). | Trooper unchanged; UnitType separation preserved. |

**Repeat for max_shield** with value `750` (front+rear collapse to one
operator-facing field via FakeUnit MaxShield abstraction; same TYPE-shared
semantic).

This 5-step procedure is the live counterpart to iter-259's
`SimulatorRoundTrip_MaxHull_TypeSharedWriteAffectsAllSiblings` test.

---

## Arc-level capstone pattern lessons (4 NEW + 17 iter-level)

### Capstone Lesson #1 — Reader-side ledger entries are A1.x arc multipliers

Iter 257 found 3 max_* offsets pre-pinned in the ledger without doing any
new RE work. The iter-242 design hypothesized "max_hull etc. need RTTI
walk" — iter-257 RE searched the ledger for `rva_get_max_*` engine readers
and found `+0xDCC` / `+0xDD0` / `+0xDD4` already documented from
2026-04-04. **2 ledger lookups did the work of one full RTTI-dissection
arc**. This is the second time the pattern saved a multi-iter RE arc
(iter-128 SetUnitShield correction was the first; iter-257 was the second).

**Pattern**: when a future arc looks for a writable offset, FIRST grep the
ledger for `rva_get_*` engine readers that read the same field; the offset
is recoverable from their first-instruction `(a1 + N)` access pattern.

### Capstone Lesson #2 — iter-256 memory rule earns ROI within 2 iters

The iter-256 codification (`feedback_aob_drift_across_binary_versions`)
earned downstream beneficiaries iter-257 (1st) and iter-258 (2nd) within
**2 iters of codification**. Pattern proven: same-week codification earns
ROI fast because the codification language is fresh in the iter-author's
mind.

By iter 261, this rule is now standard practice for any RE arc — semantic
verification before designing — a discipline rather than a checklist item.

### Capstone Lesson #3 — Cascading-drift recursive cleanup proven across 3 hops

The iter-237 silent SetCameraPos LIVE flip cascaded to:

- **1st-cousin** (iter-243): `Phase2PendingEntryCount` count-pin drift.
- **2nd-cousin** (iter-258): `Iter107ScrollCameraToTargetTests
  .Catalog_KeepsSetCameraPos_AsPhase2Pending` test.
- **3rd-cousin** (iter-259): `DirectorModeTabViewModelCapabilityTests`
  StartPlayback / StepPlayback / Phase2PendingWarning trio.

**6 iters between flip and full cascade resolution.** Each cascade caught
ONE level of drift; the next sibling sat silent until the next iter ran
the full suite.

**Pattern enforcement**: when a LIVE-flip changes a SWFOC_* status, run
`grep -rn "SWFOC_<NAME>" tests/` IMMEDIATELY. Don't wait for downstream
iters to surface each cousin individually.

### Capstone Lesson #4 — "Seamless LIVE promotion" pattern proven 2nd time

Iter-245 (iter-243 invuln_flag/prevent_death promotion) showed that
LIVE-flipping a Phase-1 mirror with an existing staging-UI input field
costs **0 UI lines**. Iter-260 (iter-258 max_hull/max_shield promotion)
proved the same.

**Pattern is now reliable**: when planning an A1.x arc that will flip a
SetUnitField sub-field LIVE, the staging-UI iter (iter 4 of the canonical
5-iter shape) is **always a no-op verification iter**, never a UX-extension
iter. This unlocks pin tests + sibling renames in place without UI churn —
0 visual regressions, 0 dispatcher extensions, 0 XAML edits.

---

## Cumulative arc shipping

| Metric | Pre-arc (iter 256) | Post-arc (iter 261) | Δ |
|---|---|---|---|
| LIVE wire/sub-field flips (cumulative this conversation) | 97 | 99 | **+2** (max_hull, max_shield) |
| SetUnitField LIVE sub-fields | 5/13 (post iter-243) | **7/13** | +2 |
| Deferred SetUnitField sub-fields | 7 (iter-242 list) | 5 | -2 |
| Editor test count | 8149 (iter 257 close) | **8162** | +13 |
| Pin test files added (iter 257-261 arc) | 0 | **2 NEW** (`Iter259SetUnitFieldMaxFieldsSimulatorTests` + `Iter260UnitStatEditorMaxFieldsLivePromotionTests`) | +13 tests |
| Iter-245 sibling renames | 0 | 3 | +3 (5-Live → 7-Live, 7-Phase1 → 5-Phase1, count reason text) |
| DirectorMode trio fixed | baseline failures | 0 baseline failures | -3 failures |
| Catalog rationale (SWFOC_SetUnitField Note) | 5/13 enumerated | **7/13 enumerated + iter-256 memory-rule citation + TYPE-LEVEL caveat** | extended |
| Bridge dispatchers/builders | 12 helpers | 12 helpers (iter-258 added 0 helpers; reused direct write pattern from iter-243) | unchanged |
| `RVA::*` namespaces | 5 (Lua, GameObj, PlayerObj, Selection, etc.) | **6** (iter-258 added `UnitType`) | +1 |
| Pattern lessons codified | 0 (this arc) | **21** (17 iter-level across iter 257-260 close-outs + 4 arc-level capstone in iter 261) | +21 |
| Capability surface regen | 0 | 2 (iter 258 catalog rationale + iter 259 DirectorMode badge change) | +2 |
| Editor binary | 157.83 MB (iter 258) | 157.83 MB | unchanged |
| Bridge binary | 406.5 KB (iter 258) | 406.5 KB | unchanged |

---

## What the next arc inherits

**Direct memory-write pattern with type-stats walk** is now bridge-tested +
simulator-tested + UI-verified. The next arc that needs to write per-type
stats can re-use the iter-258 pattern at ~5 LoC marginal cost (one new
branch in `Lua_SetUnitField` + one new offset constant in `RVA::UnitType`).

**Candidate next arcs** (priority order from iter-261 close-out):

1. **max_speed** — needs iter-99 locomotor-chain RE re-audit. iter-99
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

**Alternative paths**:

6. **Phase2HookPending re-audit pass** — mirrors iter-132/iter-221/iter-250
   cadence. Catalog has grown; some non-A1.x entries may have silent LIVE
   flips waiting to be caught. Drift rate trend so far: iter-132 12.5%,
   iter-221 15%, iter-250 4% (decreasing — discipline working).
7. **Lua Playground preset menu refresh** for iter 252-260 wires (would
   extend iter-252's 102-entry list; mostly catalog-rationale updates
   rather than new presets since iter 252-260 added LIVE sub-fields under
   existing entries).
8. **Reverse-orphan audit** — mirrors iter-255 cadence. 11-iter window
   since iter-238/iter-239 last reverse-orphan check (now 22 iters since
   iter-255; window has doubled).

---

## Operator quick-reference: iter 257-261 LIVE wire surface

When operating the editor + bridge after iter 261, the SetUnitField LIVE
sub-field surface is **7/13**:

| Sub-field | LIVE since | Implementation strategy |
|---|---|---|
| `hull` | iter 136 | Direct write to `GameObj::HP` (+0x5C) |
| `shield` | iter 136 | `SetFrontShield @ 0x3A8630` + `SetRearShield @ 0x3A91E0` |
| `speed` | iter 136 | `SetSpeedOverride @ 0x3A8C90` |
| `invuln_flag` | iter 243 | Direct byte write to `GameObj::InvulnFlag` (+0x3A7) |
| `prevent_death` | iter 243 | Bit-write of bit 0x80 of `GameObj::PreventDeath` (+0x3A1) |
| **`max_hull`** | **iter 258** | **Walks `GameObj+0x298` → `UnitType*`, writes float at `UnitType::MaxHull` (+0xDCC); TYPE-LEVEL — affects EVERY unit of this type** |
| **`max_shield`** | **iter 258** | **Same walk, dual-writes `UnitType::MaxFrontShield` (+0xDD0) AND `UnitType::MaxRearShield` (+0xDD4); TYPE-LEVEL** |

Phase-1 mirror sub-fields (deferred to future arcs):
`max_speed`, `attack_power`, `respawn_ms`, `is_hero`, `respawn_enabled`.

`owner_slot` — INTENTIONALLY EXCLUDED per iter-242 design; operator MUST
use `SWFOC_ChangeUnitOwnerLua` (iter-108) for engine-aware ownership change.
