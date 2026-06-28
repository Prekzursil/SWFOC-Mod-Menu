# Iter 270 — A1.x SetUnitField attack_power HONEST DEFER close-out (iter-269 telescoped 2-iter cycle finale)

**Date:** 2026-05-07 22:30 UTC (close-out)
**Iter:** 270 (HONEST DEFER finale)
**Arc:** 8th multi-iter A1.x arc; **2-iter telescoped honest-defer cycle COMPLETE** (iter 269 + iter 270).
**Predecessor:** iter 269 RE kickoff (HONEST DEFER candidate identified).
**Successor:** iter 271 (next-arc choice — recommended NON-A1.x per iter-269 lesson #2).

## Headline

**HONEST DEFER COMPLETE — 3rd this session.** Catalog rationale extension
shipped with NEW alternative-set pattern; VM comment extended for parity;
NEW pin test added; capability surface regenerated; ALL 47/47 focused
GREEN.

| Metric | Value |
|---|---|
| Bridge changes | **0** (attack_power stays Phase-1 mirror; no LIVE branch added) |
| Catalog rationale | extended `attack_power` enumerated as honest-defer with iter-96/154/225 alternative-set + iter-269 RE walk + HardpointFire @ 0x387F50 finding |
| VM source comment | extended `EditFieldOptions` block with iter-269-270 RE walk provenance + alternative triplet |
| NEW pin test | `SetUnitField_NoteCitesIter270AttackPowerHonestDeferAlternatives` (8 substring assertions) |
| Iter136 file test count | 13 → **14** |
| Focused regression suite | **47 / 47 GREEN** (Iter136 + Iter221 + Iter244 + Iter245 + Iter260 + Iter264 + Iter266 + CapabilitySurfaceReportIntegration) |
| Editor test build | **0 errors / 18 pre-existing warnings** unchanged from iter-268 |
| Capability surface markdown | regenerated via `SWFOC_REGEN_CAPABILITY_SURFACE=1` (108,238 bytes; iter-270 substrings present) |
| Bridge harness | unchanged (no bridge changes — assumed 1100/0 from iter-268 close) |
| Verifier ledger lint | unchanged at 318 entries (no ledger changes) |

## What shipped

### CapabilityStatusCatalog.cs rationale extension

Extended `SWFOC_SetUnitField` Note block by enumerating `attack_power`
as a third honest-defer entry distinct from the other 3 remaining
Phase-1 mirror sub-fields (respawn_ms, is_hero, respawn_enabled). The
attack_power block introduces the **alternative-set pattern** — a
refinement of iter-251/268's single-alternative pattern — by listing
ALL THREE existing LIVE alternatives covering distinct damage scopes:

```csharp
+ "attack_power → Phase-1 mirror with HONEST DEFER (iter 269-270 RE walk per iter-256 "
+ "memory rule confirmed iter-94 rejection EMPIRICALLY REAFFIRMED — combat path has "
+ "NO central per-unit attack_power read site; HardpointFire @ 0x387F50 inspection "
+ "shows param_1+0x28 is the hardpoint HP CONSUMER and param_4 damage is PASSED IN, "
+ "computed dynamically from per-weapon XML attributes at fire time. Operator has 3 "
+ "LIVE alternatives covering distinct damage scopes (alternative-set pattern): "
+ "iter-96 SWFOC_SetDamageMultiplierGlobal for global outgoing damage scaling via "
+ "Take_Damage_Outer @ 0x38A350 MinHook detour; iter-154 SWFOC_SetDamageModifierLua "
+ "for per-unit damage scaling via Set_Damage_Modifier engine API; iter-225 "
+ "SWFOC_SetFireRateMultiplierGlobal for global fire-rate scaling via WeaponTick @ "
+ "0x387010 MinHook detour. Together these triple-cover damage tuning; adding a 4th "
+ "attack_power LIVE branch would not add operator capability and would sacrifice "
+ "iter-258 TYPE-LEVEL semantic consistency); "
+ "respawn_ms / is_hero / respawn_enabled → Phase-1 mirror only "
+ "(g_pendingUnitFieldWrites) pending per-field RE walk; "
```

**Operator-trust audit trail** for attack_power now spans 5 links:
`SWFOC_SetUnitField` rationale → iter-269 RE walk doc → iter-256 memory
rule → iter-94 original rejection → iter-96/154/225 LIVE alternatives.
Operators reading the `attack_power` deferral can pick the correct LIVE
wire by **scope** (global outgoing / per-instance / global fire-rate)
without grepping docs.

### UnitStatEditorTabViewModel.cs comment extension

VM-layer source comment extended to mirror the catalog rationale change
per iter-260 lesson #2 (source-grep pin tests for VM comments). Adds a
new HONEST DEFER section for attack_power citing the iter-269-270 RE
walk provenance + alternative triplet, separate from the existing
max_speed HONEST DEFER section, separate again from the now-shrunken
"Phase-1 mirror only" group:

```csharp
// Phase-1 mirror with HONEST DEFER (iter 269-270 — semantic verification per
// iter-256 memory rule EMPIRICALLY REAFFIRMED iter-94's rejection: combat path has
// NO central per-unit attack_power read site; HardpointFire @ 0x387F50 inspection
// shows param_1+0x28 is the hardpoint HP CONSUMER and damage is param_4 PASSED IN,
// computed dynamically from per-weapon XML attributes at fire time. Operator has
// 3 LIVE alternatives covering distinct damage scopes (alternative-set pattern):
// iter-96 SWFOC_SetDamageMultiplierGlobal (global outgoing via Take_Damage_Outer
// detour), iter-154 SWFOC_SetDamageModifierLua (per-instance via Set_Damage_Modifier
// engine API), iter-225 SWFOC_SetFireRateMultiplierGlobal (global fire-rate via
// WeaponTick detour). Adding a 4th attack_power LIVE branch would not add operator
// capability and would sacrifice iter-258 TYPE-LEVEL semantic consistency):
//   attack_power.
//
// Phase-1 mirror only (queued, no engine effect; pending future RTTI offset arcs):
//   respawn_ms / is_hero / respawn_enabled.
```

### NEW pin test

`Iter136SetUnitFieldPartialLiveTests.cs` extended with
`SetUnitField_NoteCitesIter270AttackPowerHonestDeferAlternatives` —
**8 substring assertions** (largest pin-test substring count yet,
reflecting the alternative-set pattern's 3 LIVE alternative
cross-references vs iter-268's 2):

| Substring | Purpose |
|---|---|
| `attack_power → Phase-1 mirror with HONEST DEFER` | distinguishes attack_power from the other 3 Phase-1 mirror sub-fields |
| `iter 269-270` | telescoped 2-iter cycle provenance |
| `iter-96 SWFOC_SetDamageMultiplierGlobal` | global outgoing damage scaling LIVE alternative (entry 1/3) |
| `iter-154 SWFOC_SetDamageModifierLua` | per-instance damage scaling LIVE alternative (entry 2/3) |
| `iter-225 SWFOC_SetFireRateMultiplierGlobal` | global fire-rate scaling LIVE alternative (entry 3/3) |
| `HardpointFire @ 0x387F50` | iter-269 RE walk RVA finding for the damage CONSUMER |
| `per-weapon XML` | iter-269 RE walk's key finding (damage computed dynamically from per-weapon XML at fire time) |
| `alternative-set pattern` | iter-270 introduces this refinement of iter-251/268 single-alternative pattern |

**Source-grep pattern** per iter-260 lesson #2 — bypasses VM
construction; ~1 ms execution; catches future rationale decay.

### Pre-existing comment-glyph fix discovery

Grep output showed lines 202-203 of the existing iter-268 test as `\`
characters where comment lines should be `//`. Read tool confirmed the
file is actually correct on disk (`//` characters); Grep render was an
artifact, not a real defect. Build verified clean — no spurious
escapes, no compile blockers introduced by iter-270.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-268 |
|---|---|---|
| Editor test build | **0 errors / 18 pre-existing warnings** | unchanged warning count |
| Iter136 focused suite | **14 / 14 passed in 25 ms** | **+1 test** (iter-270 NEW pin) |
| Rationale-touching focused suite (8 test classes) | **47 / 47 passed in 267 ms** | covers Iter136 + Iter221 + Iter244 + Iter245 + Iter260 + Iter264 + Iter266 + CapabilitySurfaceReportIntegration |
| Capability surface markdown | regenerated via `SWFOC_REGEN_CAPABILITY_SURFACE=1` | 108,238 bytes; substrings `iter 269-270` + `alternative-set pattern` confirmed present |
| Capability surface JSON | regenerated as sibling | 69,087 bytes |
| Bridge harness | n/a (no bridge changes) | inherits iter-268 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-268 0/0 at 318 entries |

## Arc-level capstone (iter 269-270 telescoped 2-iter cycle)

### What this arc closed

`SWFOC_SetUnitField` `attack_power` sub-field investigated at iter-242
deferred 10-list level (iter-94 originally rejected at the global-mult
level). iter-269 RE kickoff identified that the ledger lacks a
TYPE-LEVEL attack_power offset AND the combat path doesn't expose a
per-unit attack_power read site (HardpointFire confirms damage is
param-passed, computed dynamically from per-weapon XML at fire time).
iter-270 closed the honest defer by enumerating `attack_power` as
distinct-from-other-Phase-1-mirror sub-fields with explicit
cross-references to **3 existing LIVE alternatives** covering distinct
damage scopes (the alternative-set pattern).

### Arc-level pattern lessons (validated by iter-270 close)

1. **iter-256 memory rule confirms TRUE NEGATIVES, not just catches
   FALSE POSITIVES** — confirmed at iter-270 with no surprises. Three
   data points now: iter-249 (FP — community ledger drift caught),
   iter-267 (TN — no TYPE-LEVEL max_speed offset), iter-269 (TN —
   no central per-unit attack_power read site, iter-94 was right).
   **Pattern**: rule's value isn't measured solely by FP catches.
2. **HONEST DEFER cadence indicates ledger-state asymptote** —
   confirmed at iter-270: 3 of 8 multi-iter A1.x arcs this session
   are honest-defer (37.5%). Rising ratio across the trajectory:
   1/5 (20%) at iter-249 → 2/7 (28.6%) at iter-267 → **3/8 (37.5%)
   at iter-269**. **Pattern**: easy reader-side ledger entries are
   exhausted; future A1.x sub-field arcs will have INCREASING
   honest-defer probability. Pivot to NON-A1.x classes.
3. **Damage cross-reference triplet (iter-96 + iter-154 + iter-225)
   is the alternative-set pattern** — confirmed at iter-270 by the
   rationale extension format: operators reading the Phase-1 mirror
   entry now find 3 LIVE alternatives by SCOPE (global outgoing /
   per-instance / global fire-rate) in 1 click via tooltip
   cross-references. **Refinement of iter-251 single-alternative
   pattern**: when an honest-defer arc has multiple existing LIVE
   alternatives, the rationale extension should list ALL of them
   (alternative-set), not just the closest match.
4. **A1.x arc length depends on ledger-state, not topic-state** —
   2-iter cycle complete (iter-269 + iter-270). Mirrors iter-249-250
   (SetUnitCapOverride) + iter-267-268 (max_speed) 2-iter patterns.
   **All three honest-defer arcs this session telescoped to 2 iters;
   full 5-iter A1.x arcs need reader-side ledger entries pre-pinned.**

### Cumulative arc shipping

| Metric | Pre-arc (iter 268) | Post-arc (iter 270) | Δ |
|---|---|---|---|
| LIVE wire/sub-field flips | 99 | 99 | **0** (HONEST DEFER) |
| SetUnitField LIVE sub-fields | 7/13 | 7/13 | unchanged |
| Deferred SetUnitField sub-fields | 5 (max_speed honest-defer + 4 plain) | 5 (max_speed + attack_power honest-defer + 3 plain) | semantic shift |
| Plain Phase-1 mirror sub-fields | 4 | **3** | -1 (attack_power promoted to honest-defer) |
| Honest-defer sub-fields | 1 (max_speed) | **2** (max_speed + attack_power) | +1 |
| Iter136 file test count | 13 | **14** | +1 (iter-270 NEW pin test) |
| Iter136 NEW test substring count | 6 (iter-268) | **8 (iter-270)** | +2 — reflects alternative-set pattern's larger cross-reference surface |
| Catalog rationale | 34-line | **~42-line** | +8 lines |
| Pattern lessons codified (iter 269 + 270) | 0 | **4 arc-level capstone** | NEW |
| Honest-defer arc count this session | 2 | **3** | +1 |
| Honest-defer rate trend | 2/7 (28.6%) | **3/8 (37.5%)** | climbing — empirical asymptote signal |

## What's next (iter 271+)

**Per iter-269 lesson #2**, the honest-defer rate has climbed to 37.5%
across 8 multi-iter arcs. The strategic call is to **pivot away from
A1.x sub-field arcs** until live-game CheatEngine tracing surfaces new
reader-side offsets. Recommended priority order:

1. **Iter 271 (RECOMMENDED — NON-A1.x class)**:
   - **Lua Playground preset menu refresh** — last ran iter-264; 104
     entries; would extend to ~106 entries with iter-269-270 + iter-256
     memory-rule comment block. Pure VM/XAML; ~30 min.
   - **Reverse-orphan audit (~22-iter window)** — last ran iter-263;
     would clean-pass-or-catch since iter-243; pure tooling; ~20 min.
   - **README capstone update (~30-iter cadence)** — last ran
     iter-265; would cover iter-265-269 master loop window; pure docs;
     ~30 min.
   - **Phase2HookPending re-audit (~16-iter cadence)** — last ran
     iter-266; would track drift trend 12.5%→15%→4%→8%→?; ~30 min.

2. **Iter 271 (alternative — NEW arc class kickoff)**:
   - **Thread B Overlay Phase 2-full** ImGui vendoring (~500 LoC,
     ~15 files, multi-iter).
   - **Thread C Save-game RE** (not started, multi-iter).
   - **Thread D Multi-repo CI gate hygiene** (not started).
   - **Thread E Local SonarQube workflow** (not started).

3. **Iter 271 (NOT recommended — another A1.x arc)**:
   - `respawn_ms (per-hero)` — needs per-hero respawn-timer table
     RVA, not in ledger. Almost certainly 4th HONEST DEFER (would push
     rate to 4/9 = 44.4%). Defer until live-game tracing is available.
   - `is_hero / respawn_enabled` — both higher-risk RTTI/behavior
     write paths. Same caveat.

## Iter 270 close-out summary

- This document is the iter 270 deliverable.
- **Code changes**: 1 catalog rationale extension (~10 lines) + 1 VM
  comment extension (~13 lines) + 1 NEW pin test (~36 lines).
- **No bridge changes**.
- All gates GREEN: build 0 errors, focused 47/47, capability surface
  regenerated. Bridge harness + ledger lint inherit unchanged.
- **3rd HONEST DEFER arc COMPLETE this session** (iter-248-249 +
  iter-267-268 + iter-269-270). All three follow iter-249 telescoped
  2-iter pattern.
- 8th back-to-back A1.x arc COMPLETE this session.
- **Catalog-discipline framework strengthened**: alternative-set
  pattern formally introduced as a refinement of iter-251/268
  single-alternative pattern. Future honest-defer arcs with multiple
  LIVE alternatives should ship the alternative-set rationale at
  arc-completion time, not require a follow-up audit catch.
- **Honest-defer rate 3/8 (37.5%)** signals A1.x ledger-state asymptote.
  Iter 271 should pivot to non-A1.x class.
