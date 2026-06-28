# A1.x SetUnitField Extras — Multi-Iter Arc Operator Changelog (iter 242-246)

**Date range:** 2026-05-06 20:30 UTC to 22:30 UTC (single session)
**Status at end of arc:** **CLOSED at offline verification level**, live-game smoke `[LIVE-PENDING]` for next live-attached session
**LIVE wire count delta:** **+0 catalog wires** (sub-field LIVE flips inside an existing wire); SetUnitField LIVE branch ratio **3/13 → 5/13**
**Master-loop tally:** 149 → **149 LIVE wires** (UNCHANGED)
**Native UX delta:** 0 buttons (UnitStatEditor staging UI already exposed all 12 supported field names since iter 136; iter 245 just locked in the design with regression guards)
**Ledger delta:** 0 (offsets pre-pinned in `rvas.h`'s `GameObj` namespace since well before iter 136); 318 entries unchanged

---

## What this arc closed

`SWFOC_SetUnitField` had been **PARTIAL LIVE** since iter 136 — 3/13 sub-fields (`hull` / `shield` / `speed`) routed through engine helpers, the other 10 fell through to a Phase-1 mirror queue with no engine effect. The iter-242 RE pass identified that **2 of the remaining 10 sub-fields had GameObject offsets ALREADY pinned in `rvas.h`'s `GameObj` namespace** (`InvulnFlag = +0x3A7`, `PreventDeath = +0x3A1` bit 0x80). Iter 243 shipped both as LIVE direct-write branches. Iter 244-246 closed the simulator + UX + verification loop.

This is the **fourth back-to-back A1.x multi-iter arc** in the same session — proving the canonical 5-iter shape is repeatable across **four different implementation strategies AND across arc scopes ranging from +1 to +4 LIVE wires to +2 sub-field flips inside an existing wire**:

| Arc | Iter range | Strategy | LIVE delta |
|---|---|---|---|
| A1.3 SetFireRate | iter 224-228 | Every-frame MinHook detour at WeaponTick | +1 |
| A1.x FreezeCredits | iter 230-234 | Bool-freeze-precedence MinHook detour at AddCredits | +4 |
| A1.x SetCameraPos | iter 236-240 | Direct call at SetTransformMatrix (no detour) | +2 |
| **A1.x SetUnitField extras** | **iter 242-246** | **Direct memory write inside existing wire** | **+2 sub-field flips** |

**20 iters of pure deferred-arc closure across 4 strategies.** The 5-iter shape is invariant; the *contents* scale to the work required.

---

## Per-iter walk-through

### Iter 242 — RE design kickoff (research-only, no code)

- Created `knowledge-base/iter242_setunitfield_remaining_re_kickoff.md` (~330 lines).
- **HEADLINE FINDING**: 2 sub-fields have offsets pre-pinned in `rvas.h`'s `GameObj` namespace — **zero new RE work needed for iter 243**:
  - `invuln_flag` → `GameObj::InvulnFlag = +0x3A7` (byte; display-only flag).
  - `prevent_death` → `GameObj::PreventDeath = +0x3A1` (bit 0x80; set by iter-153 Set_Cannot_Be_Killed).
- **`owner_slot` deferred indefinitely** despite having an offset (`GameObj::OwnerPlayerID = +0x58`). Direct write would bypass:
  - Selection-list update at GameModeClass+0x1C0 per-player vectors.
  - AI brain reassignment in AIPlayerClass instances.
  - UI roster refresh in Diagnostics tab Get_Owner reader.
  - Save-game ownership consistency.
  Operator MUST use **iter-108 SWFOC_ChangeUnitOwnerLua** which calls `Change_Owner @ 0x574D0E` and handles all of the above.
- **7 harder sub-fields deferred to 4-5 future arcs** (each needs its own runtime-offset RE pass; most are XML-loaded/RTTI-driven): `max_hull`, `max_shield`, `max_speed`, `attack_power`, `respawn_ms`, `is_hero`, `respawn_enabled`.
- **Design decision matrix**: chose direct memory write with explicit "partial-effect-only" caveat in catalog rationale + cross-references to engine-state-aware LIVE alternatives (iter-110 SWFOC_MakeInvulnerableLua for invuln, iter-153 SWFOC_SetCannotBeKilledLua for prevent_death). Justification: operator convenience (not gameplay correctness). Lesson from `feedback_flag_flipping_vs_engine_state` memory entry preserved in design doc risks section.
- **Rejected**: engine-Lua-API wrapping (already covered by iter-110 + iter-153 LIVE wires); defer entirely (iter-136 already created the dispatcher precedent and operators expect field-name access).
- **Pattern observation**: A1.x multi-iter arcs scale **DOWN** as well as up (iter-224 +1 / iter-230 +4 / iter-236 +2 / iter-242 +2 sub-field flips inside an existing wire). Marginal cost ≈ 5-10 LoC per sub-field branch when offset is already pinned.

### Iter 243 — Bridge LIVE branches shipped (+2 sub-field LIVE flips, 3/13 → 5/13 ratio)

- `swfoc_lua_bridge/lua_bridge.cpp` `Lua_SetUnitField` (line ~6404): inserted 2 new branches between the existing `if (f == "speed")` block and the Phase-1 mirror fall-through:
  - `if (f == "invuln_flag")` → direct byte write `*reinterpret_cast<uint8_t*>(addr + RVA::GameObj::InvulnFlag)` to `0x01` if `val != 0.0f` else `0x00`.
  - `if (f == "prevent_death")` → bit-write of bit 0x80 of byte at `addr + RVA::GameObj::PreventDeath` (read-modify-write).
- Both response strings explicitly cite the engine-state-aware LIVE alternatives:
  - `OK: invuln_flag written (LIVE — display only; pair with MakeInvulnerableLua for engine effect)`
  - `OK: prevent_death bit set (LIVE — bit 0x80 of +0x3A1; operator may prefer SWFOC_SetCannotBeKilledLua)`
- `CapabilityStatusCatalog.cs` `SWFOC_SetUnitField` rationale extended 12-line → 21-line:
  - Ratio updated **3/13 → 5/13 sub-fields LIVE iter 136+243**.
  - Per-LIVE-field caveats with iter-110/iter-153 cross-references.
  - Phase-1 mirror fields enumerated explicitly.
  - **owner_slot defer-with-pointer-to-iter-108 documented**: "operator MUST use SWFOC_ChangeUnitOwnerLua iter-108 for engine-aware ownership change."
- Test updates:
  - `Iter136SetUnitFieldPartialLiveTests.cs` ratio pin updated 3/13 → 5/13. NEW `SetUnitField_NoteCitesIter243LiveAlternatives` + `SetUnitField_NoteCitesIter243CrossRefs` tests.
  - `Iter221Phase2PendingReAuditTests.cs` count pin 26 → 25 (silent iter-237 SetCameraPos flip drift caught by audit-by-fail this iter).
- **Mid-iter drift catches × 2**:
  1. Iter-136 ratio pin (cascading test obligation — flipping 3/13 → 5/13 in catalog rationale required updating the test that pinned `Contain("3/13")`).
  2. **Catalog-wide Phase2 count drift `26 → 25`** — surfaced by audit-by-fail when running iter-136 tests. The iter-237 SetCameraPos catalog flip (Phase2HookPending → Live) drifted the count silently 6 iters ago. This is the same drift class as per-tab AllActions count pins (`feedback_allactions_count_pin_drift` memory).
- Bridge harness **1100/0 GREEN** clean. DLL + replay rebuilt.
- **+2 sub-field LIVE flips. 149 → 149 catalog wires UNCHANGED.** SetUnitField wire LIVE branch ratio: 3/13 → 5/13.

### Iter 244 — Simulator handler extension + 6 pin tests + wire-format-canonical alignment closed

- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs` `HandleSetUnitField` extended with **canonical snake_case branches matching the bridge's 13-field taxonomy**:
  - 5 LIVE branches: `hull` → CurrentHull, `shield` → CurrentShield, `speed` → Speed, `invuln_flag` → Invulnerable (bool), `prevent_death` → DeathPrevented (bool).
  - 7 Phase-1 mirror branches: `max_hull` → MaxHull, `max_shield` → MaxShield, `max_speed` → MaxSpeed, `attack_power` → DamageScalar, `is_hero` → IsHero, `respawn_enabled`/`respawn_ms` → no-op (no FakeUnit fields), `owner_slot` → OwnerSlot (Phase-1 mirror; doesn't cascade to FakePlayer's selection-list).
  - **Backwards-compat preserved**: legacy PascalCase names (MaxHull / CurrentHull / MaxShield / CurrentShield / Speed / MaxSpeed / DamageScalar / FireRateScalar) still work for iter-136-era tests.
- **Wire-format-mismatch closed**: this is a **drift class flagged in `reference_simulator_wire_gotchas` memory** (now 10 wire-format mismatches caught across iters 22-244). The iter-136 simulator handler used PascalCase but the bridge's BridgeUnitStatEditDispatcher emits snake_case — silent mismatch surfaced only when iter-244 pin tests forced canonical wire format.
- NEW pin file `Iter244SetUnitFieldExtraFieldsSimulatorTests.cs` (6 tests):
  - `CatalogStatus_SetUnitFieldIsLive` — pin SetUnitField stays Live across iter 136 → iter 243 multi-stage promotion.
  - `CatalogRationale_DocumentsIter243LiveBranchesAndCaveats` — pin 5/13 ratio + iter-110 + iter-153 + iter-108 cross-references.
  - `SimulatorRoundTrip_InvulnFlag_TogglesOnAndOff` — bridge wire `SWFOC_SetUnitField(addr, 'invuln_flag', 0/1)` → FakeUnit.Invulnerable bool flip.
  - `SimulatorRoundTrip_PreventDeath_TogglesOnAndOff` — same pattern for prevent_death → FakeUnit.DeathPrevented.
  - `SimulatorRoundTrip_HullShieldSpeed_CanonicalSnakeCaseBranches` — regression guard for iter-244's wire-format-canonical alignment.
  - `SimulatorRoundTrip_OwnerSlotPhase1Mirror_StoresButDoesNotCascade` — Phase-1 mirror semantics pin (operator-correct path is iter-108).
  - `SimulatorRoundTrip_LegacyPascalCase_StillWorks` — backwards-compat regression guard.
- **Mid-iter fixes × 2**:
  1. Helper signature drift — initial NewSession() didn't construct a unit; fixed by extending helper to `(sim, adapter, unit)` and pre-creating a Rebel_Trooper_Squad FakeUnit.
  2. xUnit1030 ConfigureAwait warnings — 9 instances dropped via single replace_all to maintain 0-warnings standard.
- Verify: **22/22 GREEN** focused (Iter244 + Iter136 + Iter221) in 35 ms; **184/184 GREEN** wider sweep (Simulator + SetUnitField + PhaseC).

### Iter 245 — UnitStatEditor staging-UI verification iter (no UX extension; 6 pin tests + iter-242 design lock-in)

- **Verification iter only** — no UX extension needed. The existing `UnitStatEditorTabViewModel.EditFieldOptions` already lists 12 of the 13 SWFOC_SetUnitField sub-fields including iter-243's NEW LIVE fields (invuln_flag + prevent_death). owner_slot was INTENTIONALLY ABSENT since iter 136 (per iter-242 design rationale).
- **ViewModel comment block added** to `EditFieldOptions` declaration documenting:
  - 5 LIVE branches with engine-state-aware alternative cross-references (iter-110, iter-153).
  - 7 Phase-1 mirror branches.
  - **owner_slot intentional exclusion** rationale + iter-108 SWFOC_ChangeUnitOwnerLua redirect.
- NEW pin file `Iter245UnitStatEditorStagingFieldsTests.cs` (6 tests):
  - `EditFieldOptions_IncludesAllFiveLiveFields` — pin all 5 LIVE fields present.
  - `EditFieldOptions_IncludesAllSevenPhase1MirrorFields` — pin all 7 Phase-1 fields present.
  - **`EditFieldOptions_DoesNotIncludeOwnerSlot`** — regression guard for iter-242 design exclusion (cascading guard if someone adds it without the iter-108 warning).
  - `EditFieldOptions_TotalCountIs12` — drift guard for future scope creep.
  - `EditFieldOptions_FirstSixPreserveIter136Ordering` — pin existing hull/max_hull/shield/max_shield/speed/max_speed interleaved order.
  - `ComposedBadge_StaysLiveForSetUnitField` — 2nd-order pin reinforcing iter-243 catalog flip lock-in.
- **Pattern lesson**: **verification iters lock deliberate exclusions via regression guards**. iter-245 didn't ship code mutations beyond a comment block, but the 6 pin tests turn iter-242's owner_slot defer into a test guard. Without these pins, a future contributor could "complete" the staging UI by adding owner_slot, silently desyncing ownership state.
- Verify: **28/28 GREEN** focused (Iter245 + Iter244 + Iter136 + Iter221) in 30 ms.

### Iter 246 — Live verify + close (multi-iter arc finale, 5/5)

- Pure verify + close-out, no code changes.
- **5 verify gates GREEN**:
  - Bridge harness `bridge_test_harness.exe` → **1100 passed, 0 failed** (clean).
  - Verifier ledger lint `python -m verifier lint` → **0 errors / 0 warnings** at 318 entries.
  - Editor focused tests → **28/28 GREEN** (Iter245 6 + Iter244 6 + Iter136 9 + Iter221 7).
  - Editor build → 0 warnings / 0 errors.
  - Capability surface markdown → unchanged from iter-243 regen.
- **HISTORY.md prepended** the 5-iter arc summary entry (~1300 words, 5 NEW patterns).
- **STATUS.md master-loop**: new row added for "A1.x SetUnitField extras (invuln_flag + prevent_death)" with full timeline + iter-110/iter-153/iter-108 cross-references + "Catalog wire count 149 → 149 UNCHANGED" note.
- A1.x SetUnitField extras arc **COMPLETE** at offline verification level.

---

## Operator workflow — UnitStatEditor "Apply staged edits" with 5/13 LIVE sub-fields

The UnitStatEditor tab's staging UI is the canonical operator entry point for `SWFOC_SetUnitField` (12 of 13 sub-fields exposed in the dropdown; owner_slot intentionally absent — operator must use iter-108 instead).

| Sub-field | Status | Engine effect | Engine-state-aware alternative |
|---|---|---|---|
| `hull` | LIVE iter 136 | Direct write to GameObj::HP @ +0x5C | — (this is the canonical path) |
| `shield` | LIVE iter 136 | SetFrontShield @ 0x3A8630 + SetRearShield @ 0x3A91E0 | — (canonical path) |
| `speed` | LIVE iter 136 | SetSpeedOverride @ 0x3A8C90 | — (canonical path) |
| `invuln_flag` | **LIVE iter 243** | Direct byte write at GameObj+0x3A7 (display flag only) | **iter-110 SWFOC_MakeInvulnerableLua** for full hardpoint-propagated invulnerability via BehaviorMarker + per-hardpoint INVULNERABLE attachments |
| `prevent_death` | **LIVE iter 243** | Direct bit-write of bit 0x80 of GameObj+0x3A1 | **iter-153 SWFOC_SetCannotBeKilledLua** for engine-state-aware path via Set_Cannot_Be_Killed Lua API |
| `max_hull` | Phase-1 mirror | (queued; no engine effect) | Future A1.x MaxHull arc |
| `max_shield` | Phase-1 mirror | (queued; no engine effect) | Future MaxHull/MaxShield arc |
| `max_speed` | Phase-1 mirror | (queued; no engine effect) | Future A1.x MaxSpeed arc |
| `attack_power` | Phase-1 mirror | (queued; no engine effect) | Future A1.x AttackPower arc |
| `respawn_ms` | Phase-1 mirror | (queued; no engine effect) | Future per-hero respawn arc (iter-130 confirmed defer for per-hero path) |
| `is_hero` | Phase-1 mirror | (queued; no engine effect) | Future A1.x IsHero arc (high RTTI complexity) |
| `respawn_enabled` | Phase-1 mirror | (queued; no engine effect) | Future per-hero respawn arc |
| `owner_slot` | **NOT EXPOSED** | (intentionally absent from staging UI) | **iter-108 SWFOC_ChangeUnitOwnerLua** — calls Change_Owner @ 0x574D0E, handles selection-list + AI brain + UI roster cascade |

### Workflow walkthrough

1. Paste obj_addrs (one or more, comma- or whitespace-separated) into the "Target obj_addrs" textbox.
2. Pick a field name from the "Field" dropdown (12 options).
3. Type a numeric value into the "Value" textbox (booleans use 0/1).
4. Click "Stage" to add the (field, value) pair to the staged-edits ListBox.
5. Repeat for additional fields if needed.
6. Click "Apply all staged to all targets" — bridge fires one `SWFOC_SetUnitField(addr, field, value)` per (target, staged-pair).

The bottom status bar reflects the composed badge: **LIVE** (since SWFOC_SetUnitField is catalogued Live with 5/13 sub-fields LIVE).

---

## Engine semantic caveats (6)

1. **`invuln_flag` is display-only**: writing GameObj+0x3A7 directly does NOT make the unit invulnerable in gameplay terms. The actual invulnerability lives in the BehaviorMarker at +0x37D plus per-hardpoint INVULNERABLE behavior attachments (see iter-110 hardpoint propagation finding). Operators wanting full invulnerability MUST also fire iter-110 SWFOC_MakeInvulnerableLua.

2. **`prevent_death` bit-flip is partial**: iter-153 Set_Cannot_Be_Killed(true) likely sets multiple bits in addition to +0x3A1's 0x80. Operators wanting the engine-state-aware path should prefer iter-153 SWFOC_SetCannotBeKilledLua.

3. **Phase-1 mirror semantics**: the 7 deferred sub-fields (max_hull, etc.) are stored in `g_pendingUnitFieldWrites` but have NO engine effect. Operators staging these edits and clicking Apply will see "OK: unit-field write queued (Phase 2 offset-table hook pending)" — but the engine state stays untouched until a future RTTI offset arc lands.

4. **`owner_slot` direct write is the iter-23 anti-pattern**: the `feedback_flag_flipping_vs_engine_state` memory rule warns against byte-flipping gameplay state directly because it bypasses engine state machines. owner_slot is the highest-risk field — direct write desyncs selection vectors, AI brain assignments, and UI rosters. **The staging UI intentionally excludes owner_slot to prevent this footgun.** Use iter-108 SWFOC_ChangeUnitOwnerLua instead.

5. **7 sub-fields still Phase-1 mirror**: max_hull, max_shield, max_speed, attack_power, respawn_ms, is_hero, respawn_enabled — each needs its own future RE arc. Most are XML-loaded/RTTI-driven, so the offsets aren't simple GameObject+N writes; they likely live in per-unit-type tables that need RTTI walking to find.

6. **Tactical/galactic scope**: SetUnitField operates on tactical-mode and galactic-mode unit objects identically at the GameObject memory layer. Both modes use the same offsets (+0x3A1 / +0x3A7). The iter-110/iter-153 engine-Lua-API alternatives also work in both modes, so operators don't need to switch wires by mode.

---

## Cross-tab combined surface (1 tab)

**UnitStatEditor tab** is the operator-facing entry point for all 12 staged sub-fields. Different tabs route through different LIVE wires for the same engine effects:

| Tab | Wire | Sub-field equivalent | Notes |
|---|---|---|---|
| UnitStatEditor → "Apply" | SWFOC_SetUnitField | All 12 staged | Canonical multi-field staging surface |
| UnitControl → "Make invuln" | SWFOC_MakeInvulnerableLua | invuln_flag (engine-aware) | iter-110; full hardpoint propagation |
| UnitControl → "Cannot be killed ON" | SWFOC_SetCannotBeKilledLua | prevent_death (engine-aware) | iter-153; engine Set_Cannot_Be_Killed Lua API |
| UnitControl → "Change owner" | SWFOC_ChangeUnitOwnerLua | owner_slot (engine-aware) | iter-108; full ownership-change pipeline |
| HeroLab → "Apply staged hero edits" | SWFOC_HeroStatEdit | hull/shield/speed only (3/4 LIVE) | iter-135 catalog clarification; mirrors iter-136 pattern |

Operator pattern: **for engine-state-aware mutations, prefer the dedicated UnitControl tab buttons (iter-110/153/108)**; the UnitStatEditor staging UI is operator-convenience for batch / multi-field workflows.

---

## Pattern lessons (5)

1. **A1.x arcs scale DOWN as well as up** — iter 224-228 (+1 LIVE), iter 230-234 (+4 LIVE), iter 236-240 (+2 LIVE), iter 242-246 (+2 sub-field LIVE flips inside an existing wire). Marginal cost ≈ 5-10 LoC per sub-field branch when offset is already pinned. The 5-iter shape stays invariant; the *contents* scale to the work required.

2. **Catalog-wide aggregation pins are the same drift class as per-tab AllActions count pins** — iter-237 SetCameraPos flip silently drifted `Phase2PendingEntryCount_Is26` (catalog-wide count) in `Iter221Phase2PendingReAuditTests`. Caught by audit-by-fail in iter-243. Future Phase-1 → LIVE flips must update both per-tab AllActions counts AND catalog-wide Phase2 count pins. Memory rule `feedback_allactions_count_pin_drift` extends to catalog-wide aggregation pins.

3. **Simulator wire-format gaps surface only when canonical bridge wire is exercised** — iter-136 simulator handler had its own PascalCase taxonomy that never collided with the bridge's snake_case canonical until iter-244's pin tests forced the canonical form. Fix-when-it-shows-up cadence works; the gap stayed silent for 7 iters with zero operator impact. Memory rule `reference_simulator_wire_gotchas` now has 10 wire-format mismatches caught across iters 22-244.

4. **Verification iters lock deliberate exclusions via regression guards** — iter-245 didn't ship code mutations beyond a comment block, but the 6 pin tests turn iter-242's owner_slot defer into a test guard. Without these pins, a future contributor could "complete" the staging UI by adding owner_slot, silently desyncing ownership state. Pattern: every *intentional gap* in a feature surface needs a test that fires if the gap gets closed without the cross-reference warning.

5. **The 5-gate close-out template extends to "intentional gaps are provably absent"** — bridge harness + lint + tests + build + capability surface = 5 gates. Iter-245 added an implicit 6th gate: "the deliberate exclusion is test-pinned." This is what `feedback_flag_flipping_vs_engine_state` was missing as a memory rule — the rule said *don't byte-flip*, but until iter-245 there was no test that would FIRE if a contributor added owner_slot byte-flipping back in.

---

## Cross-references

- **iter-242 RE design doc**: `knowledge-base/iter242_setunitfield_remaining_re_kickoff.md` (~330 lines)
- **HISTORY.md**: 5-iter arc summary section "2026-05-06 — A1.x SetUnitField extras multi-iter arc CLOSED (iter 242-246)"
- **Predecessor docs iters**: iter 229 (SetFireRate arc, ~270 lines), iter 235 (FreezeCredits arc, ~300 lines), iter 241 (SetCameraPos arc, ~280 lines), iter 247 (this doc).
- **STATUS.md master-loop**: new row "A1.x SetUnitField extras (invuln_flag + prevent_death)" with timeline iter 136 → 242 → 243 → 244 → 245 → 246
- **Engine-state-aware LIVE alternatives**:
  - iter 110 — `SWFOC_MakeInvulnerableLua` (BehaviorMarker + per-hardpoint propagation)
  - iter 153 — `SWFOC_SetCannotBeKilledLua` (Set_Cannot_Be_Killed Lua API)
  - iter 108 — `SWFOC_ChangeUnitOwnerLua` (Change_Owner @ 0x574D0E pipeline)
- **Memory rules invoked**:
  - `feedback_flag_flipping_vs_engine_state` (iter 23 anti-pattern guidance)
  - `feedback_allactions_count_pin_drift` (extended to catalog-wide aggregation pins this arc)
  - `reference_simulator_wire_gotchas` (now 10 wire-format mismatches caught)

---

## What's next (iter 248+)

The 4-back-to-back-A1.x-arc cadence has now closed all the **easy** deferred sub-tasks (offsets pinned, helpers existing). The remaining A1.x candidates require deeper RE work:

**Option A — A1.x SetUnitCapOverride** (single-wire arc; iter-132 deferred). Per-unit-type cap is XML-loaded into a runtime-cached table; needs RTTI walk to find the runtime-write path. Estimated 3-iter arc (RE design + bridge wire + UX/close).

**Option B — A1.x SetUnitField max_hull/max_shield/max_speed/attack_power batch** (4 sub-fields; iter-242 deferred). Each likely shares a per-unit-type max-stats table; one RTTI walk could pin all 4 offsets. Estimated 4-5 iter arc (RE + bridge bundle + sim+tests + UX + close).

**Option C — Operator polish session** (multi-iter, no new bridge work). Refresh the Lua Playground preset menu to include iter 242-246 wires (none new — but document the iter-243 caveats); verify all 12 staged sub-fields work end-to-end via UnitStatEditor; audit reverse-orphan snapshot for any newly-unwired entries since iter 238.

**Option D — Phase2HookPending re-audit pass** (mirrors iter-132 + iter-221 audit pattern). Re-audit the 25 PHASE 2 PENDING entries (iter-243 caught the count drift to 25); identify any silent LIVE flips since iter 221 + queue any drift catches as future arcs.

**Recommended**: **Option A** (SetUnitCapOverride). Single-wire arcs are predictable and the iter-132 audit already flagged it as a candidate. Operators will benefit from being able to force per-faction unit caps for tournament/sandbox scenarios.

---

**Format mirrors iter 187 / 196 / 216 / 220 / 222 / 229 / 235 / 241 / 247 docs-iter precedent.**

**Pure markdown — no bridge / dispatcher / VM / XAML / test changes.**

**No editor binary republish needed (no editor source touched).**

**109 → 109 buttons UNCHANGED.**
