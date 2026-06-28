# Iter 268 — A1.x SetUnitField max_speed HONEST DEFER close-out (iter-267 telescoped 2-iter cycle finale)

**Date:** 2026-05-07 21:50 UTC (close-out)
**Iter:** 268 (HONEST DEFER finale)
**Arc:** 7th multi-iter A1.x arc; **2-iter telescoped honest-defer cycle COMPLETE** (iter 267 + iter 268).
**Predecessor:** iter 267 RE kickoff (HONEST DEFER candidate identified).
**Successor:** iter 269 (next A1.x arc OR polish iter).

## Headline

**HONEST DEFER COMPLETE.** Catalog rationale extension shipped; NEW pin
test added; capability surface regenerated; ALL 8169 GREEN.

| Metric | Value |
|---|---|
| Bridge changes | **0** (max_speed stays Phase-1 mirror; no LIVE branch added) |
| Catalog rationale | extended `max_speed` enumerated as honest-defer with iter-99/iter-100 cross-refs + iter-267 RE finding |
| VM source comment | extended `EditFieldOptions` Phase-1 mirror block with iter-267-268 RE walk provenance |
| NEW pin test | `SetUnitField_NoteCitesIter268MaxSpeedHonestDeferAlternatives` (6 substring assertions) |
| Editor full suite | **8169 / 0 / 8169** (+1 from iter-267's 8168) |
| Editor binary | `publish/SwfocTrainer.App.exe` **157.83 MB** (unchanged size; rationale text doesn't affect binary footprint) |
| Bridge harness | **1100 / 0** unchanged |
| Verifier ledger lint | **0 / 0** (318 entries) unchanged |
| Capability surface markdown | regenerated via `SWFOC_REGEN_CAPABILITY_SURFACE=1` |

## What shipped

### CapabilityStatusCatalog.cs rationale extension

Extended `SWFOC_SetUnitField` Note block by enumerating `max_speed` as
a honest-defer entry separate from the other 4 Phase-1 mirror sub-fields
(attack_power, respawn_ms, is_hero, respawn_enabled):

```csharp
+ "max_speed → Phase-1 mirror with HONEST DEFER (iter 267-268 RE walk per iter-256 "
+ "memory rule confirmed NO TYPE-LEVEL max_speed offset in ledger; Override_Max_Speed "
+ "@ 0x57E590 walks unit+0x60 locomotor not unit+0x298 UnitType. Operator should use "
+ "iter-99 SWFOC_SetUnitSpeed for per-instance speed override OR iter-100 "
+ "SWFOC_SetPerFactionSpeedMultiplier for per-faction; both call SetSpeedOverride @ "
+ "0x3A8C90 directly, providing LIVE coverage that max_speed sub-field cannot match "
+ "without sacrificing iter-258 TYPE-LEVEL semantic consistency); "
+ "attack_power / respawn_ms / is_hero / respawn_enabled → Phase-1 mirror only "
+ "(g_pendingUnitFieldWrites) pending per-field RE walk; "
```

**Operator-trust audit trail** now has 4-link chain for max_speed:
`SWFOC_SetUnitField` rationale → iter-267 RE walk doc → iter-256 memory
rule → iter-99/100 LIVE alternatives. Operators reading the
`max_speed` deferral can find the LIVE path without grepping docs.

### UnitStatEditorTabViewModel.cs comment extension

VM-layer source comment (touched at iter-260) extended to reflect the
honest-defer semantic:

```csharp
- // Phase-1 mirror (queued, no engine effect; pending future RTTI offset arcs):
- //   max_speed / attack_power / respawn_ms / is_hero / respawn_enabled.
+ // Phase-1 mirror with HONEST DEFER (iter 267-268 — semantic verification per
+ // iter-256 memory rule confirmed no TYPE-LEVEL max_speed offset; Override_Max_Speed
+ // @ 0x57E590 walks unit+0x60 locomotor NOT unit+0x298 UnitType; iter-99
+ // SWFOC_SetUnitSpeed + iter-100 SWFOC_SetPerFactionSpeedMultiplier already cover
+ // per-instance + per-faction LIVE; routing max_speed through this dispatcher would
+ // sacrifice iter-258 TYPE-LEVEL semantic consistency):
+ //   max_speed.
+ //
+ // Phase-1 mirror only (queued, no engine effect; pending future RTTI offset arcs):
+ //   attack_power / respawn_ms / is_hero / respawn_enabled.
```

This applies the iter-260 lesson #2 (source-grep pin tests for VM
comments) to keep the VM-layer documentation in sync with the catalog
rationale.

### NEW pin test

`Iter136SetUnitFieldPartialLiveTests.cs` extended with
`SetUnitField_NoteCitesIter268MaxSpeedHonestDeferAlternatives` — 6
substring assertions on the rationale:

| Substring | Purpose |
|---|---|
| `max_speed → Phase-1 mirror with HONEST DEFER` | distinguishes max_speed from the other 4 Phase-1 mirror sub-fields |
| `iter 267-268` | telescoped 2-iter cycle provenance |
| `iter-99 SWFOC_SetUnitSpeed` | per-instance LIVE alternative cross-ref |
| `iter-100 SWFOC_SetPerFactionSpeedMultiplier` | per-faction LIVE alternative cross-ref |
| `Override_Max_Speed @ 0x57E590` | iter-267 RE walk RVA finding |
| `locomotor` | iter-267 RE walk's locomotor-vs-UnitType distinction |

**Source-grep pattern** per iter-260 lesson #2 — bypasses VM
construction; ~1 ms execution; catches future rationale decay.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-267 |
|---|---|---|
| Bridge harness | **1100 / 0** | unchanged (no bridge changes) |
| Verifier ledger lint | **0 / 0** (318 entries) | unchanged |
| Editor full suite | **8169 / 0 / 8169** | **+1 from iter-268 NEW pin test** (was 8168/0/8168) |
| Editor binary | 157.83 MB (165,499,723 B) | unchanged size (rationale text changes don't affect footprint) |
| Bridge binary | 406.5 KB | unchanged |
| Focused-tests run | 41 / 41 GREEN | covers Iter136 + Iter221 + Iter244 + Iter245 + Iter260 + CapabilitySurfaceReportIntegration |
| Capability surface markdown | regenerated | absorbed iter-268 rationale change |

## Arc-level capstone (iter 267-268 telescoped 2-iter cycle)

### What this arc closed

`SWFOC_SetUnitField` `max_speed` sub-field investigated at iter-258
deferred 5-list level. iter-267 RE kickoff identified that the ledger
lacks a TYPE-LEVEL max_speed offset (iter-258 reader-side pattern
unavailable). iter-268 closed the honest defer by enumerating
`max_speed` as distinct-from-other-Phase-1-mirror sub-fields with
explicit cross-references to existing per-instance + per-faction LIVE
alternatives (iter-99/100).

### Arc-level pattern lessons (from iter-267 capstone, validated by iter-268 close)

1. **iter-258's reader-side pattern doesn't transfer to all sub-fields**
   — confirmed at iter-268 with no surprises. Ledger absence was
   deterministic; honest defer was the correct strategic call.
2. **HONEST DEFER preserves operator-trust when alternatives exist** —
   confirmed at iter-268 by the rationale extension format: operators
   reading the Phase-1 mirror entry now find iter-99/100 LIVE
   alternatives in 1 click (catalog tooltip → cross-references).
3. **Semantic consistency is operator-trust currency** — the rationale
   block now shows max_speed flagged "HONEST DEFER" (distinct phrase)
   while the other 4 Phase-1 mirror sub-fields are labeled "Phase-1
   mirror only" — operators can see at a glance which deferral has a
   ready alternative vs which is awaiting RE.
4. **A1.x arc length depends on ledger-state, not topic-state** —
   2-iter cycle complete (iter-267 + iter-268). Mirrors iter-249-250
   SetUnitCapOverride 2-iter pattern. **Both honest-defer arcs this
   session telescoped to 2 iters; full 5-iter A1.x arcs need
   reader-side ledger entries pre-pinned.**

### Cumulative arc shipping

| Metric | Pre-arc (iter 266) | Post-arc (iter 268) | Δ |
|---|---|---|---|
| LIVE wire/sub-field flips | 99 | 99 | **0** (HONEST DEFER) |
| SetUnitField LIVE sub-fields | 7/13 | 7/13 | unchanged |
| Deferred SetUnitField sub-fields | 5 | 5 (max_speed re-classified honest-defer) | semantic shift |
| Editor test count | 8168 | **8169** | +1 (iter-268 NEW pin test) |
| Pin test files | 0 NEW | 0 NEW (existing Iter136 file extended) | extension only |
| Catalog rationale | 27-line | **34-line** | +7 lines |
| Pattern lessons codified (iter 267 + 268) | 0 | **4 arc-level capstone** | NEW |
| Editor binary size | 157.83 MB | 157.83 MB | unchanged |

## What's next (iter 269+)

**Recommended priority order**:

1. **Iter 269 (next A1.x arc kickoff)** — candidates from iter-262
   changelog priority list:
   - **`attack_power` via iter-94 retry** — iter-94 rejected as not
     directly writable; future arc might MinHook at the
     `Damage_Multiplier` read site. Higher RE risk than iter-258.
     Likely 5-iter arc IF reader-side entries exist; 2-iter honest
     defer if not.
   - **`respawn_ms (per-hero)`** — needs per-hero respawn-timer table
     RVA, not in ledger. iter-130 confirmed defer; needs RE arc.
   - **`is_hero / respawn_enabled`** — both higher-risk RTTI/behavior
     write paths.

2. **Alternative: Single-iter polish** —
   - **Lua Playground preset menu refresh at iter 270**
   - **README capstone update at iter 295** (~30-iter cadence)
   - **Reverse-orphan audit at iter 285** (~20-30-iter cadence)
   - **Phase2HookPending re-audit at iter 282** (~16-iter cadence per
     iter-250→iter-266 trend)

**Recommendation**: Iter 269 = **`attack_power` via iter-94 retry RE
kickoff**. The iter-94 rejection used incomplete ledger context (no
type-stats offsets known then); a fresh RE walk per iter-256 memory
rule might find a writable path. If not, falls through to honest-defer
2-iter cycle (mirrors iter-267-268).

## Iter 268 close-out summary

- This document is the iter 268 deliverable.
- **Code changes**: 1 catalog rationale extension (~7 lines) + 1 VM
  comment extension (~5 lines) + 1 NEW pin test (~25 lines).
- **No bridge changes**.
- All 5 gates GREEN (bridge harness 1100/0; lint 0/0; editor 8169/0;
  binaries unchanged size; focused tests 41/41; surface regenerated).
- Pattern: **2nd HONEST DEFER arc complete this session** (iter-248-249
  + iter-267-268). Both 2-iter telescoped cycles validate the
  iter-249 pattern as the canonical shape for ledger-state-blocked
  arcs.
- 109 → 109 buttons UNCHANGED. 104 → 104 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- 7th back-to-back A1.x arc this session, COMPLETE.
- **Catalog-discipline framework strengthened**: max_speed rationale
  now demonstrates the iter-251 cross-reference pattern applied at
  arc-completion time (vs after-the-fact iter-251 catch). Future
  honest-defer arcs should ship the rationale extension in the
  close-out iter (iter-N+1), not require a follow-up audit catch.
