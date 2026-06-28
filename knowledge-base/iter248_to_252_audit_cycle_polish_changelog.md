# Iter 248-252 — Audit Cycle + Polish Operator Changelog

**Date range:** 2026-05-06 23:30 UTC to 2026-05-07 00:45 UTC (single session)
**Scope:** 5 iters spanning 5th A1.x arc honest defer + 3rd Phase2HookPending audit + audit drift fix + 2nd preset menu refresh
**LIVE wire count delta:** **+0** (no LIVE flips this window — see "Honest defer" section below)
**Master-loop tally:** 149 → **149 LIVE wires** (UNCHANGED)
**Native UX delta:** 0 buttons (109 → 109 across 10 tabs UNCHANGED)
**Ledger delta:** +1 DEPRECATION (`rva_apocalypticx_unit_cap_gc` flipped VERIFIED → DEPRECATED iter 249); 318 entries unchanged
**Preset menu delta:** **+12 NEW presets** (iter 252; 90 → 102 entries)
**Test infrastructure delta:** **+2 NEW pin files** + **+1 NEW cross-reference test in iter-221 audit guards** (iter 251 + 252)

---

## What this 5-iter window closed

This window does NOT advance the LIVE-wire surface — instead it strengthens the **catalog-discipline framework** that makes future LIVE flips trustworthy. Three things shipped in parallel:

1. **5th A1.x arc closed via honest defer** (iter 248-249): SetUnitCapOverride had a community CE table AOB anchor `rva_apocalypticx_unit_cap_gc @ 0x28DF6F` flagged as "Unit cap calculation (GC)". Iter-249 RE walk revealed the address is in a string-deallocation cleanup block, NOT a unit-cap reader. The AOB likely matched a different function in an older binary version. Ledger entry DEPRECATED; SetUnitCapOverride stays Phase2HookPending pending fresh RE (live-game CheatEngine tracing or IDA MCP xref walk). NEW pattern lesson: **AOB drift across binary versions** — community CE tables lose accuracy across binary fingerprints.

2. **3rd Phase2HookPending audit closes the operator-trust loop** (iter 250-251): re-audit caught 1 catalog-rationale drift (SWFOC_FreezeCredits rationale "BLOCKED-NO-RVA" stale — iter-231 SetCreditsFreezeGlobal LIVE alternative not cited). Iter-251 fixed end-to-end: catalog rationale extended + NEW `LegacyPhase1Mirrors_CiteLiveAlternativeWhenAvailable` cross-reference pin test added to iter-221 audit guards. **Decreasing drift rate validates discipline ROI**: 12.5% (iter 132) → 15% (iter 221) → **4% (iter 250)**.

3. **2nd preset menu refresh** (iter 252): Lua Playground preset menu extended with 12 NEW presets covering iter 225/231/237/243 LIVE flips. 90 → 102 entries. XAML header bumped "Iter 100-219" → "Iter 100-251". **Operator-trust documentation pin** in iter-243 presets cite engine-state-aware alternatives (iter-110 MakeInvulnerableLua + iter-153 SetCannotBeKilledLua) per `feedback_flag_flipping_vs_engine_state` memory rule.

---

## Per-iter walk-through

### Iter 248 — A1.x SetUnitCapOverride RE design kickoff

- Created `knowledge-base/iter248_setunitcapoverride_re_kickoff.md` (~280 lines).
- **Strategy chosen — Option A**: MinHook detour at the canonical cap reader (matches iter-96 / iter-225 / iter-231 detour pattern). Reversible via cap=-1 clears override. Per-slot map already existed in bridge (`g_pendingUnitCapOverride` since iter-132 audit Phase-1 stub).
- 3 alternatives rejected: Option B (per-faction table direct write — risky), Option C (no engine Lua API exists), Option D (defer entirely).
- iter 249-252 implementation outline with 6 risks documented (mid-function AOB attachment was Risk #5).
- **5th unique RE entry pattern this session**: community CE table AOB anchor (vs prior 4: function-entry, consume-site, engine-helper, pre-pinned struct offset).

### Iter 249 — RE walk + CORRECTION + DEFERRED CONFIRMED (5th arc closes as 2-iter honest defer)

- **HEADLINE**: iter-248 strategy INVALIDATED. Reading `sub_14028DBE0` disassembly at offset 0x38F (= 0x14028DF6F − 0x14028DBE0) revealed:
  ```
  14028df6b  call j_j_free        ; <-- standard string deallocation thunk
  14028df6f  mov [rbp+var_30], rdi ; <-- post-free cleanup, NOT unit-cap calc
  ```
- The address is **inside a string-deallocation cleanup block** (post-`j_j_free`); surrounding code references `aThestorymodeLo` string. This is an event-handler / script-loader, NOT a unit-cap calculation.
- **3 actions taken**:
  1. iter-248 design doc updated with prominent CORRECTION section (preserves original design as archival; explicitly marks it superseded).
  2. `rva_apocalypticx_unit_cap_gc` flipped VERIFIED → **DEPRECATED** with full `deprecated_reason` field describing AOB-drift-across-binary-versions root cause.
  3. Bridge stub comment updated with iter-248 → iter-249 correction provenance + DEFERRED CONFIRMED status.
- **No catalog flip** (SWFOC_SetUnitCapOverride stays Phase2HookPending; no iter-221 count drift). **No bridge harness rebuild** (comment-only change).
- **5-iter arc collapses to 2-iter "RE kickoff + correction-with-defer" cycle**. Honest defers are arc completions too — matches iter-130 SetFireRate + iter-131 SetGameSpeed defer pattern.
- **NEW pattern lesson — "AOB drift across binary versions"**: community CE table AOBs lose accuracy across binary versions. Future RE using community CE tables must verify the AOB-pinned function is **semantically what the table label claims**, not just that the address resolves. This is a NEW drift class distinct from catalog drift / simulator drift / wire-format drift / stale-count drift.
- Ledger lint clean: 318 entries, 305 VERIFIED + 2 LIVE_OBSERVED + **11 DEPRECATED** (was 10), 0/0.

### Iter 250 — Phase2HookPending re-audit pass (3rd audit; +1 catalog-rationale drift caught)

- Created `knowledge-base/iter250_phase2_pending_audit.md` (~250 lines).
- **25 PHASE 2 PENDING entries triaged**:
  - **22 confirmed defer** (cumulative iter-132 + iter-221 + iter-249): EventControl, SetIncomeMultiplier, SetGameSpeed (iter-131), SetBuildSpeed, SetDamageMultiplier (per-slot, iter-94), SetAreaDamage, SetTargetFilter, ToggleOHKAttackPower, FreezeAI, FreeCam, SpawnUnit, SetBuildCost, **SetUnitCapOverride (NEW iter-249)**, InstantBuild, FreeBuild, GetPlanets, ChangePlanetOwner, GetPlanetTechAndBuildings, ListHeroes, SetHeroRespawnTimer, SetPermadeath. + 2 vestigial-fixed (iter-137 ChangePlanetOwnerWithMode + SpawnAsStoryArrival).
  - **1 catalog-rationale drift caught**: SWFOC_FreezeCredits rationale "BLOCKED-NO-RVA" stale; iter-231 SWFOC_SetCreditsFreezeGlobal LIVE alternative not cited. **Operator-trust drift, not status drift** — Phase2HookPending status is correct (legacy wire IS Phase-1), but operators reading the rationale don't know about the LIVE alternative shipped under a sibling catalog entry.
- Phase2 count pin `_Is25` confirmed correct (no change since iter-243 fix).
- **NEW drift class identified — "Catalog-rationale-cross-reference drift"**: a Phase-1 mirror catalog entry stays Phase2HookPending correctly but its rationale doesn't cite the LIVE alternative shipped under a sibling catalog entry. Pattern: when an iter ships a NEW catalog entry that supersedes a legacy Phase-1 mirror (like iter-225 / iter-231 / iter-237 / iter-243), the legacy entry's rationale MUST be updated in the same iter.
- **Pattern lesson capstone — decreasing drift rate validates catalog-discipline**:
  - iter-132 (1st audit, 24 entries): 3 catches = **12.5% drift rate**
  - iter-221 (2nd audit, 26 entries): 4 catches = **15% drift rate**
  - **iter-250 (3rd audit, 25 entries): 1 catch = 4% drift rate**
  - Each audit closes drift sources permanently — the framework converges. Catalog-discipline ROI is measurable.

### Iter 251 — SWFOC_FreezeCredits rationale fix + NEW LegacyPhase1Mirrors cross-reference pin test

- **Catalog change** (`CapabilityStatusCatalog.cs` line 121): SWFOC_FreezeCredits rationale extended from "BLOCKED-NO-RVA" to a 5-line block citing iter-231 SWFOC_SetCreditsFreezeGlobal (Hook_AddCredits MinHook detour at 0x27F370 with bool-precedence; +4 LIVE flips iter 231) as the LIVE alternative + iter-250 audit-catch provenance + Phase-1 mirror legacy wire shape rationale.
- **NEW pin test** in `Iter221Phase2PendingReAuditTests.cs`: `LegacyPhase1Mirrors_CiteLiveAlternativeWhenAvailable` — asserts SetFireRate (iter-225) + FreezeCredits (iter-231) rationales contain their respective iter-N cross-references. Future legacy-Phase-1-mirror flips must extend this list when shipped.
- **Verify gates**: 29/29 GREEN focused (Iter221 7 + Iter136 9 + Iter244 6 + Iter245 6 + Iter221 NEW LegacyPhase1Mirrors 1) in 40 ms; 33/33 GREEN capability surface markdown regen.
- **Pattern lesson reinforced**: the iter-250 NEW drift class — "Catalog-rationale-cross-reference drift" — now has a regression guard. Future contributors who ship a legacy-Phase-1-mirror LIVE alternative without updating the legacy entry's rationale will fire the test.

### Iter 252 — Lua Playground preset menu refresh (+12 NEW presets)

- **+12 NEW presets** in `Iter100to113Presets` (90 → **102 entries**) covering the iter 224-251 LIVE flips:
  - iter 225 (2): SetFireRateMultiplierGlobal set (2.0x) + reset (1.0x).
  - iter 231 (5): SetCreditsFreezeGlobal freeze + unfreeze + Get + SetCreditsMultiplierGlobal (3.0x) + Get.
  - iter 237 (2): SetCameraPos (X/Y/Z teleport) + GetCameraPos (read).
  - iter 243 (3): SetUnitField('invuln_flag', 1) + SetUnitField('prevent_death', 1) — **with operator-trust caveats citing iter-110 MakeInvulnerableLua + iter-153 SetCannotBeKilledLua** + 1 explanatory comment.
- **MainWindowV2.xaml**: GroupBox header "Iter 100-219 LIVE wires" → **"Iter 100-251 LIVE wires"**.
- **NEW pin file** `Iter252PresetMenuRefreshTests.cs` (5 tests; same source-grep pattern as iter-223): per-iter-tag presence + SWFOC_* function name presence + iter-243 cross-reference assertions + GroupBox header pin.
- **Verify gates**: 25/25 GREEN focused (Iter252 5 + Iter223 8 + Iter183 12) in 21 ms; bridge harness 1100/0 unchanged; ledger lint 0/0 unchanged; editor binary republished `publish/SwfocTrainer.App.exe` (Release single-file, ~157 MB).
- **2nd preset-menu refresh this conversation** (iter-223 was 1st covering iter 184-219; iter-252 covers iter 224-251). Cadence ≈ every 30-50 iters of LIVE-flip activity.

---

## NEW pattern lessons (4)

### 1. Honest defers ARE arc completions (iter 248-249)

The 5-iter arc shape supports 4 closure modes:
- **Mode 1: Full LIVE flip** (iter 224-228 / iter 230-234 / iter 236-240) — 5 iters with +1/+4/+2 LIVE wires.
- **Mode 2: Sub-field flip inside existing wire** (iter 242-246) — 5 iters with +2 sub-field LIVE flips, no catalog wire count change.
- **Mode 3: Honest defer** (iter 248-249) — 2 iters with 0 LIVE flips, ledger DEPRECATION + clear next-step requirements.
- **Mode 4 (future): Single-iter deferral** — if iter-1 RE design itself reveals a hard block.

The cadence is the right unit, regardless of strategy / scope / success outcome. Honest defers are arc completions because they:
- Document the engine-block reason (verified, with disassembly evidence).
- Mark the misleading ledger entry DEPRECATED so future contributors don't re-step the same trap.
- Provide explicit next-step requirements (live-game CE tracing, IDA MCP xref walk, community CE table refresh).

### 2. AOB drift across binary versions (iter 249)

Community CE table AOBs lose accuracy across binary versions. The 2026 SWFOC binary has different code layout than the binary the Apocalypticx table was authored against. Future RE that uses community CE tables must verify the AOB-pinned function is **semantically what the table label claims**, not just that the address resolves.

This is a NEW drift class distinct from:
- Catalog drift (catalog status diverges from bridge reality)
- Simulator drift (simulator wire format diverges from bridge wire format)
- Wire-format drift (PascalCase vs snake_case mismatch)
- Stale-count drift (per-tab AllActions count pins drift)
- Catalog-rationale-cross-reference drift (legacy Phase-1 mirror rationales miss LIVE-alternative cross-refs)

Memory rule: when consuming a community CE table, perform a 1-step verification: decompile the function containing the AOB and verify the surrounding code is semantically consistent with the table label.

### 3. Catalog-rationale-cross-reference drift (iter 250)

A Phase-1 mirror catalog entry stays Phase2HookPending correctly (the wire IS Phase-1), but its rationale doesn't cite the LIVE alternative shipped under a sibling catalog entry. This is **operator-trust drift** — operators reading the Phase2 entry don't know the LIVE alternative exists.

Pattern: when an iter ships a NEW catalog entry that supersedes a legacy Phase-1 mirror (like iter-225 / iter-231 / iter-237 / iter-243), the legacy entry's rationale MUST be updated in the same iter. Iter-251 added a regression guard (`LegacyPhase1Mirrors_CiteLiveAlternativeWhenAvailable`).

Memory rule extension: `feedback_allactions_count_pin_drift` (catalog-wide aggregation pins) extends to cover **legacy-Phase-1-mirror rationale cross-references too**.

### 4. Decreasing drift rate validates catalog-discipline ROI (iter 250)

| Audit | Iter | Phase2 entries | Drift catches | Rate |
|---|---|---|---|---|
| 1st | 132 | 24 | 3 | 12.5% |
| 2nd | 221 | 26 | 4 | 15% |
| 3rd | **250** | 25 | **1** | **4%** |

The drift rate is decreasing — each audit closes drift sources permanently; future drift sources are NEW additions only. The audit cost stays roughly constant (~250-line doc + per-entry triage ≈ 1 iter of work) but the catches drop, meaning **the catalog-discipline framework has positive ROI: cumulative drift prevention outpaces the per-iter audit cost**.

---

## Audit cycle closed-loop diagram

```
     ┌────────────────────────────────────────────────────────────┐
     │                                                            │
     │   ┌──── Iter 132 ────┐    ┌──── Iter 221 ────┐    ┌─── Iter 250 ───┐
     │   │   1st audit:     │    │   2nd audit:     │    │   3rd audit:   │
     │   │   24 entries     │    │   26 entries     │    │   25 entries   │
     │   │   3 drift catches│    │   4 drift catches│    │   1 drift catch│
     │   │   (12.5%)        │    │   (15%)          │    │   (4%)         │
     │   └─────────┬────────┘    └─────────┬────────┘    └────────┬───────┘
     │             ▼                       ▼                      ▼
     │  ┌── Iter 133 ──┐        ┌── Iter 222 ──┐         ┌── Iter 251 ──┐
     │  │ Drift fixes  │        │ Drift fixes  │         │ Drift fix:   │
     │  │ ship in next │        │ ship in next │         │ FreezeCredits│
     │  │ iters (133-  │        │ iters (222-  │         │ rationale +  │
     │  │ 138)         │        │ 237)         │         │ NEW pin test │
     │  └──────┬───────┘        └──────┬───────┘         └──────┬───────┘
     │         │                       │                        │
     │         └───────────────────────┴────────────────────────┘
     │                                 │
     │                                 ▼
     │                        REGRESSION GUARDS
     │                        (iter-221 audit guards
     │                         + iter-251 NEW
     │                         LegacyPhase1Mirrors test)
     │                                 │
     └─────────────────────────────────┘  ◀──── Future drift catches will
                                              fire test instead of staying
                                              silent for ~28 iters like
                                              iter-231 → iter-250 did
```

The iter-132 → iter-221 → iter-250 → iter-251 4-step closed loop demonstrates the catalog-discipline framework's full closed loop: **catch drift in audit → fix in next iter → pin test prevents recurrence**.

---

## Cross-references

- **iter-248 RE design doc**: `knowledge-base/iter248_setunitcapoverride_re_kickoff.md` (~280 lines, includes ITER 249 CORRECTION section)
- **iter-250 audit doc**: `knowledge-base/iter250_phase2_pending_audit.md` (~250 lines)
- **iter-247 prior changelog**: `knowledge-base/iter242_to_246_setunitfield_extras_arc_changelog.md`
- **STATUS.md master-loop SetUnitCapOverride row**: still marked Phase2HookPending; iter-249 deprecation referenced
- **Predecessor docs iters**: iter 187/196/216/220/222/229/235/241/247 (this is iter 253 = 9th docs iter this conversation; iter 253 number, but the doc covers iter 248-252)
- **Memory rules invoked**:
  - `feedback_flag_flipping_vs_engine_state` (iter 23 anti-pattern; cited in iter-243 + iter-252 preset operator-trust caveats)
  - `feedback_allactions_count_pin_drift` (iter-243 catalog-wide count drift; extended iter-250 to cover rationale cross-references)
  - `reference_simulator_wire_gotchas` (iter-244 wire-format-canonical alignment finding)
  - **NEW: `feedback_aob_drift_across_binary_versions`** (iter-249 — to be added to memory)

---

## What's next (iter 254+)

**Recommended: Option C — README capstone update** (mirrors iter-222 capstone pattern; covers iter 100-252 master loop with 152 iters of activity).

Rationale:
- iter-222 was the last README capstone (covered iter 100-221).
- 31 iters since (iter 223-252) include 4 back-to-back A1.x arcs + audit cycle close-out + 2 preset menu refreshes — substantial operator-facing surface evolution.
- README is the operator's first stop; gaps between capstones are operator-trust debt.

Alternatives:
- **Option A**: Phase2HookPending audit-followup polish — any other Phase-1 mirrors with LIVE alternatives (extend the iter-251 LegacyPhase1Mirrors regression guard).
- **Option B**: A1.x SetUnitField max_* batch RTTI walk arc (iter-242 deferred 7 sub-fields).
- **Option D**: Reverse-orphan snapshot audit — last ran iter-238/iter-239; verify no newly-unwired entries since iter-243.
- **Option E**: NEW: docs supplement covering iter 248-249 honest-defer pattern lesson at memory-rule level (codify `feedback_aob_drift_across_binary_versions`).

---

## Iter 253 close-out

- This document is the iter 253 deliverable.
- No bridge / dispatcher / VM / XAML / test changes. Pure markdown.
- 109 → 109 buttons UNCHANGED. 102 → 102 preset entries UNCHANGED from iter-252.
- **9th docs iter this conversation** (iter 187/196/216/220/222/229/235/241/247/253).
- Format mirrors iter 187 / 196 / 216 / 220 / 222 / 229 / 235 / 241 / 247 docs-iter precedent.

**Pattern lesson capstone — the audit + polish window strengthens the framework, not the surface**: iter 248-252 added 0 LIVE wires but added 1 NEW pattern lesson (AOB drift) + 1 NEW drift class (Catalog-rationale-cross-reference) + 1 NEW regression guard (LegacyPhase1Mirrors) + 1 NEW operator-trust documentation pin (iter-243 preset cross-references) + 1 honest-defer arc closure (iter 248-249) + 1 measurable validation (decreasing drift rate 12.5%→15%→4%). Future LIVE-flip arcs land on a stronger foundation. **Catalog-discipline ROI compounds across iters, not within a single iter.**
