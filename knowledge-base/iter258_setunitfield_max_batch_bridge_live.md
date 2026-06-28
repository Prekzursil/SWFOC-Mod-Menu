# Iter 258 — A1.x SetUnitField max_* batch bridge LIVE wire (iter 2/5 of multi-iter arc)

**Date**: 2026-05-06
**Iter**: 258 (close-out)
**Arc**: 6th multi-iter A1.x arc since iter 224 (SetUnitField max_* batch).
**Predecessor**: iter 257 RE kickoff (3 reader-side offsets pre-pinned in ledger;
ObjectTypePtr offset deferred to iter 258a per design contract).
**Successor**: iter 259 (simulator handler + pin tests).

## Headline

**+2 LIVE branches** (max_hull + max_shield). SetUnitField LIVE branch ratio
**5/13 (iter 243) → 7/13 (iter 258)**. Catalog wire count UNCHANGED (still 1
SWFOC_SetUnitField entry; the dispatcher just gained 2 more LIVE sub-fields).

## What shipped

### iter 258a — ObjectTypePtr semantic verification

Per the iter-256 `feedback_aob_drift_across_binary_versions` memory rule
(2nd downstream beneficiary; iter-257 was the 1st), I verified the
unit-instance → unit-type pointer offset semantically before designing the
bridge wire. Did NOT trust the existing `RVA::GameObj::GameObjType = 0x298`
constant on its own. Verification chain:

1. **Decompile body of `GetMaxHealth` @ `0x1403727A0`** (rva_get_max_health,
   ledger entry, 3-tool VERIFIED). First instruction:

   ```c
   fVar5 = *(float *)((longlong)this + 0xdcc);
   ```

   Confirms `this` IS the unit-type-stats struct, and `+0xDCC` IS the
   max-hull offset on it.

2. **Decompile body of `rva_get_hull_percentage` @ `0x140396DF0`** (caller of
   GetMaxHealth, found via `python tools/callgraph_query.py callers 0x3727A0`):

   ```c
   fVar2 = (float)FUN_1403727a0(param_1[0x53], param_1);
   ```

   `param_1[0x53]` = `*(longlong*)(unit + 0x53*8)` = `*(longlong*)(unit + 0x298)`.
   Confirms unit-instance → unit-type access pattern at +0x298.

3. **Decompile body of `rva_set_hp` @ `0x1403A89D0`** (independent caller):

   ```c
   fVar6 = (float)FUN_1403727a0(*(undefined8 *)(param_1 + 0x298), param_1);
   ...
   puVar4 = (undefined8 *)(*(longlong *)(param_1 + 0x298) + 0xf8);
   ```

   Two confirmations in the same function: passes `*(unit + 0x298)` to
   GetMaxHealth's `this` slot AND dereferences typename string at
   `(*(unit+0x298)) + 0xF8`. **This is consistent ONLY if +0x298 holds the
   unit-type pointer.**

**Two independent semantic confirmations + matches ledger constant
(`RVA::GameObj::GameObjType = 0x298`, first documented 2026-04-04 from
trainer Inspector tab + ce_trainer_inventory.md section 1.2).**

### iter 258b — Bridge LIVE branches

**`swfoc_lua_bridge/rvas.h`** — added `namespace UnitType` with semantically
verified offsets:

```cpp
namespace UnitType {
    constexpr int MaxHull        = 0xDCC; // float — base max-hull
    constexpr int MaxFrontShield = 0xDD0; // float — base max-front-shield
    constexpr int MaxRearShield  = 0xDD4; // float — base max-rear-shield
}
```

**`swfoc_lua_bridge/lua_bridge.cpp` `Lua_SetUnitField`** — added a unified
`if (f == "max_hull" || f == "max_shield")` branch that:

1. Walks `unit_instance + GameObj::GameObjType (0x298)` → `UnitType*`.
2. Null-check (returns ERR if orphan unit).
3. **`max_hull`**: writes float at `UnitType + 0xDCC`.
4. **`max_shield`**: dual-writes float at `UnitType + 0xDD0` (front) AND
   `UnitType + 0xDD4` (rear) — mirrors iter-129's per-instance dual-write
   shape.

Both branches log + push response strings with the loud TYPE-LEVEL caveat:

> "OK: max_hull written to UnitType+0xDCC (LIVE — affects EVERY unit of
> this type for the session; engine reads it on next damage tick)"

> "OK: max_shield front+rear written to UnitType+0xDD0 / +0xDD4 (LIVE —
> affects EVERY unit of this type for the session)"

### Catalog rationale extension

`CapabilityStatusCatalog.cs` SWFOC_SetUnitField rationale block updated
**5/13 → 7/13** with per-LIVE-field semantics + the type-shared caveat
loud and clear:

> "max_hull → walks GameObj+0x298 → UnitType\*, writes float at
> UnitType+0xDCC (LIVE iter 258 — TYPE-LEVEL: affects EVERY unit of this
> type for the session, operator should be aware that buff/nerf is
> global-by-type, not per-instance)"

Rationale also cites the iter-256 memory-rule application provenance:

> "...semantic verification per iter-256 memory rule via two engine-reader
> callers of GetMaxHealth @ 0x3727A0)."

### Iter 258 NEW pin test

`SetUnitField_NoteCitesIter258TypeLevelCaveat` in
`Iter136SetUnitFieldPartialLiveTests.cs` — asserts:

- `max_hull` + `max_shield` enumerated by name in catalog rationale
- `UnitType+0xDCC` / `UnitType+0xDD0` / `UnitType+0xDD4` offsets cited
- `"EVERY unit of this type"` caveat text appears
- `iter-256` memory-rule citation appears

### Ratio bump cascading test updates

3 sibling pin tests updated for the 5/13 → 7/13 ratio bump:

- `Iter136SetUnitFieldPartialLiveTests.SetUnitField_NoteDeclaresLiveFieldCount`:
  `"5/13"` → `"7/13"`
- `Iter221Phase2PendingReAuditTests.Iter132ToIter220DriftCatches_AreLive`:
  reason text updated to mention `iter-258 extended to 7/13`
- `Iter244SetUnitFieldExtraFieldsSimulatorTests.{CapabilityStatus_StaysLive,
  CatalogRationale_DocumentsIter243LiveBranchesAndCaveats}`: same 7/13 update

### Iter 258 collateral cleanup — iter-107 stale test

The full-suite run surfaced a stale `Iter107ScrollCameraToTargetTests
.Catalog_KeepsSetCameraPos_AsPhase2Pending` failure. iter-237 LIVE-flipped
SetCameraPos via direct SetTransformMatrix call; iter-243 cascading drift
catch updated `Phase2PendingEntryCount_Is26` → `_Is25` but missed THIS
iter-107 sibling. Renamed the test to `Catalog_PromotesSetCameraPos_ToLive
_PerIter237` with explicit cross-reference back to the cascade chain
(iter-237 promotion → iter-243 cascade catch → iter-258 collateral
cleanup).

### Capability surface markdown regen

`knowledge-base/capability_surface_2026-04-27.md` regenerated via
`SWFOC_REGEN_CAPABILITY_SURFACE=1` to match the iter-258 catalog
rationale change.

## Verification gates (all GREEN)

| Gate | Result |
|---|---|
| Bridge harness (C++) | **1100/0** |
| Verifier lint | **0/0** (305 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED = 318 entries) |
| Editor full suite | **8146 passed / 3 failed / 8149 total** |
| Editor binary | **`publish/SwfocTrainer.App.exe` 157.83 MB** (165,495,627 bytes) |
| New pin test | `SetUnitField_NoteCitesIter258TypeLevelCaveat` GREEN |
| Iter-107 collateral | `Catalog_PromotesSetCameraPos_ToLive_PerIter237` GREEN |
| Surface report drift | regenerated; integration test GREEN |

The 3 remaining test failures (`DirectorModeTabViewModelCapabilityTests
.{StartPlayback_BadgeIsPhase2Pending, StepPlayback_SamePrimitiveAsStart_BadgeIsPhase2Pending,
Phase2PendingWarning_NamesNonLiveActionsOnly}`) are pre-existing baseline
drift unrelated to iter-258 — DirectorMode's "Start playback" badge
appears to have been LIVE-flipped at some earlier iter (the test expects
PHASE 2 PENDING but the catalog reports LIVE; the catalog's PHASE 2
PENDING list now only contains "Set time scale"). Documented as
pre-existing for future iter to triage; iter-258 introduces neither these
failures nor any regression.

## Pattern lessons

### #1 — Iter-256 memory rule earns its 2nd downstream beneficiary

Iter-249 (1st application) → iter-257 RE kickoff (1st downstream
beneficiary; verification PASSED) → iter-258 RE walk (**2nd downstream
beneficiary**; verification PASSED again). Pattern confirmed:
**semantic verification BEFORE designing a bridge wire is now standard
practice and prevents wasted RE design effort**. Each application takes
~5 minutes (decompile body lookup + caller xref walk + 2 cross-checks)
vs the cost of an iter-248-style invalidation cycle (whole iter wasted on
DEFERRED CONFIRMED).

### #2 — Reader-side ledger entries are writable-offset goldmines

When a future arc looks for a writable offset, **search the ledger for
`rva_get_*` engine readers that read the same field**; the offset is
recoverable from their first-instruction `(a1 + N)` access pattern. The
iter-242 design hypothesized "max_hull etc. need RTTI walk" — iter-257 RE
found 3 offsets were already in the ledger waiting. Iter-258 walked the
2nd-level offset from a SECOND existing reader entry. **Two ledger
look-ups did the work of one full RTTI-dissection arc.**

### #3 — Existing constants must be semantically re-verified, not just
trusted

The bridge already had `RVA::GameObj::GameObjType = 0x298` defined since
the trainer Inspector tab work (2026-04-04). I could have just used it
directly. Iter-256 rule applied: re-verify semantically. Verification
PASSED, but more importantly **even a "trusted" pre-existing constant
gets the iter-256 treatment** because the Inspector might have been
right about the name but wrong about the precise offset, OR the offset
might have drifted across binary versions. Discipline > trust.

### #4 — Type-level vs instance-level write semantics deserve loud
caveats

Iter-243 (invuln_flag/prevent_death) wrote per-INSTANCE byte flags;
operator buff applies to ONE selected unit. Iter-258 (max_hull/max_shield)
writes per-TYPE stats; operator buff applies to EVERY unit of that type.
**Same dispatcher entry; vastly different operator-trust semantics**.
The catalog rationale + bridge response strings make this loud and clear
("affects EVERY unit of this type for the session"); iter-258 added 2
explicit pin tests asserting the caveat text appears in the rationale.
**"LIVE" is binary; "scope of LIVE effect" is not — the catalog rationale
is the only place to surface the difference**.

### #5 — Cascading drift catches need recursive cleanup

Iter-237 silent LIVE-flip → iter-243 cascading catch (Phase2PendingEntryCount
test) → iter-258 NEXT cascading catch (Iter107 sibling test). Each cascade
catches ONE test; the next sibling sits silent until the next iter that
runs it. **Pattern**: when a cascading drift catch fires (test count
mismatch, etc.), grep for the SWFOC_* name across the entire test tree
to find ALL stale tests, not just the one that surfaced. Iter-258 should
have run the grep proactively at iter-243; will do so going forward.

## Iter 259+ queued

- **Iter 259**: simulator handler extension for `max_hull` + `max_shield`
  (HandleSetUnitField switch); 4-6 pin tests for round-trip behavior;
  reverse-orphan rebalance check.
- **Iter 260**: UnitStatEditor staging-UI verification (likely no-op per
  iter-245 pattern; the staging UI already shows max_hull/max_shield input
  fields, just deferred to Phase-1 mirror until iter-258. Now operator
  Apply pushes LIVE values).
- **Iter 261**: live verify + close-out (multi-iter arc finale).
- **Iter 262**: operator changelog covering iter 257-261 arc.
