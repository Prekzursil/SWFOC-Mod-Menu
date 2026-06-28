# Iter 267 — A1.x SetUnitField max_speed RE kickoff (7th multi-iter arc; HONEST DEFER candidate)

**Date:** 2026-05-07 21:30 UTC
**Iter:** 267 (RE kickoff)
**Arc:** 7th multi-iter A1.x arc; iter 1 of (likely) **2** iters via HONEST DEFER pattern.
**Predecessor:** iter 266 Phase2HookPending re-audit (4th audit-class iter; 2 NEW drift catches caught + closed).
**Successor:** iter 268 (HONEST DEFER close-out + SWFOC_SetUnitField rationale extension).

## Headline finding

**`max_speed` has NO TYPE-LEVEL offset in the ledger.** Unlike iter-258's
max_hull/max_shield which were unblocked by pre-pinned reader-side
offsets (`rva_get_max_health` @ +0xDCC, `rva_get_max_front_shield` @
+0xDD0, `rva_get_max_rear_shield` @ +0xDD4), there is **no
`rva_get_max_speed` or sibling entry** for a per-unit-type max-speed
field.

The closest engine entry is `rva_gameobjectwrapper_override_max_speed @
0x57E590` — the Lua-binding wrapper. Decompile body inspection (per
iter-256 memory rule semantic verification) confirms this writes
**per-INSTANCE** to the locomotor chain at `unit + 0x60` (NOT the
UnitType pointer at `unit + 0x298`).

**Strategic decision:** HONEST DEFER following iter-249 SetUnitCapOverride
2-iter pattern (RE kickoff + correction-with-defer). Reasons:

1. **No reader-side ledger entry pins a TYPE-LEVEL max_speed offset** —
   iter-258's reader-side discovery pattern doesn't apply here.
2. **Per-instance LIVE coverage already exists** — iter-99
   SWFOC_SetUnitSpeed + iter-100 SWFOC_SetPerFactionSpeedMultiplier both
   call `SetSpeedOverride @ 0x3A8C90` directly (per iter-99 ledger note,
   this is what `Override_Max_Speed` ultimately calls). Operators have a
   LIVE wire for "set this unit's speed" already.
3. **TYPE-LEVEL semantic consistency** — promoting max_speed to a
   per-instance LIVE branch in SWFOC_SetUnitField would sacrifice
   semantic consistency with iter-258 max_hull/max_shield (TYPE-LEVEL).
   Operator-trust scope would become inconsistent within the same
   dispatcher (some sub-fields TYPE-shared, others per-instance) — bad
   UX, would generate iter-260-style rationale-drift cleanup later.

## RE walk findings

### Ledger search (per iter-256 memory rule, performed BEFORE designing)

```bash
grep -nE 'rva_.*speed|max.{1,5}speed|speed.{1,5}max' verified_facts.json
```

Returns 3 entries:

| Entry | Category | Location | Semantic |
|---|---|---|---|
| `rva_clear_speed_override` | engine_function | `0x38F8B0` | Per-instance speed-override clear |
| `rva_set_speed_override` | engine_function | `0x3A8C90` | Per-instance speed-override write |
| `rva_gameobjectwrapper_override_max_speed` | engine_function | `0x57E590` | Lua-binding wrapper that calls the above |

**No reader-side TYPE-stats max-speed entry exists.**

### `Override_Max_Speed @ 0x57E590` decompile body (semantic verification)

```c
undefined8 FUN_14057e590(longlong param_1, undefined8 param_2, longlong param_3)
{
    // ...
    if (*(longlong *)(param_1 + 0x60) != 0) {  // <-- UNIT + 0x60 (locomotor pointer)
        // ... arg type checks ...
        if ((plVar2 != 0) && ((char)plVar2[2] == '\0')) {
            FUN_14038f8b0(*(undefined8 *)(param_1 + 0x60));   // ClearSpeedOverride(unit+0x60)
        }
        if (plVar3 != 0) {
            FUN_1403a8c90(*(undefined8 *)(param_1 + 0x60),    // SetSpeedOverride(unit+0x60, val)
                          (float)(double)plVar3[2]);
        }
    }
    return 0;
}
```

**Key observations:**

- **`param_1 + 0x60`** is the LOCOMOTOR sub-object pointer (per iter-99
  ledger note), NOT the UnitType pointer (`+0x298`).
- The function calls `SetSpeedOverride @ 0x3A8C90` directly with
  `(locomotor, float)`.
- **No walk of `unit + 0x298 → UnitType*`** — this is per-INSTANCE
  semantics, mirroring iter-99/100's existing wires.
- Per iter-99 ledger note: "the locomotor offset chain (obj+0xA8 ->
  +0x2A0) the bridge's Phase 1 comment cites is THE STORAGE LOCATION
  the wrapper writes to" — meaning the actual per-instance speed value
  lives at `unit + 0xA8 → +0x2A0` accessed via the locomotor pointer at
  `+0x60`.

### Why iter-258 pattern doesn't transfer

Iter-258 found:
1. `rva_get_max_health @ 0x3727A0` reads `*(this + 0xDCC)` where `this`
   IS the UnitType-stats struct.
2. Two engine readers (`rva_get_hull_percentage`, `rva_set_hp`) confirm
   `unit + 0x298` holds the UnitType pointer.

For max_speed to follow the same pattern, we'd need:
1. A `rva_get_max_speed` engine reader reading `*(type + ???)`.
2. Confirmation that the type-stats struct contains a max-speed field
   at some offset.

**Neither exists in the ledger.** The engine's speed model appears to
be per-instance only at the binary level — speed is governed by
locomotor sub-objects per-unit, with the XML-loaded base value flowing
into the locomotor at unit-creation time. There's no per-type "current
max speed" stored anywhere accessible at runtime.

### What an arc would need (deferred to future)

If a future iter wants to ship TYPE-LEVEL max_speed:

1. **XML-loader RE walk** — find where unit-types' XML-defined `Max_Speed`
   value is loaded into memory at unit-creation. Likely via
   `XmlMode_LoadXmlFromFile` → some unit-type-init path.
2. **In-memory type-stats search** — identify whether the loaded
   max-speed value persists in a per-type stats struct (mirroring
   iter-258 UnitType+0xDCC pattern) or just gets passed through to each
   spawned unit's locomotor.
3. **MinHook at read site** — if no persistent storage exists, MinHook
   the per-instance read at unit-creation time and override.

This is **~5-10 iters of RE** + likely needs IDA Pro MCP live session
or live-game CheatEngine tracing. Not appropriate for a single-iter
kickoff.

## HONEST DEFER design (iter-268 close-out)

Mirroring iter-248-249 SetUnitCapOverride 2-iter telescoped cycle:

### iter-268 (HONEST DEFER close-out) scope

1. **No bridge changes** — `max_speed` stays in the Phase-1 mirror
   fall-through of `Lua_SetUnitField` (no LIVE branch added).
2. **Catalog rationale extension** for `SWFOC_SetUnitField` Note —
   enumerate `max_speed` as a deferred sub-field with explicit
   cross-reference to iter-99/100 LIVE per-instance alternatives.
   Mirrors the iter-251 SWFOC_FreezeCredits pattern.
3. **NEW pin test** in Iter136SetUnitFieldPartialLiveTests.cs asserting
   the rationale cites iter-99 SWFOC_SetUnitSpeed cross-reference for
   max_speed. Same source-grep pattern as iter-260/iter-264 lessons.
4. **Capability surface markdown regen** to absorb rationale change.
5. **All gates verification**: bridge harness 1100/0 + ledger lint 0/0
   + editor full suite + binaries unchanged size.
6. **Close-out doc** `iter268_setunitfield_max_speed_honest_defer.md`
   documents the 2-iter cycle + queues iter-269 next-arc choice.

### Ratio implications

`SWFOC_SetUnitField` LIVE sub-field ratio stays **7/13** post iter-268
(no new LIVE branch). The deferred sub-fields list shrinks
operator-trust-wise (max_speed gets its rationale cleared up) but
remains numerically the same.

## Pattern lessons (preview for iter-268 close-out)

### Lesson #1 — Iter-258's reader-side pattern doesn't transfer to all sub-fields

**Pattern**: when an A1.x arc plans to extend iter-258's TYPE-stats
walk pattern to a NEW sub-field, the FIRST step is to verify a
`rva_get_<field>` engine reader exists in the ledger. If it doesn't,
the arc is HONEST DEFER candidate — don't fabricate a TYPE-stats
offset by guessing.

This is the **inverse** of iter-258's ROI insight ("reader-side ledger
entries are A1.x arc multipliers"): the absence of a reader-side entry
is also a strategic signal. **Pattern enforcement**: every iter-N+1
sub-field arc must run the ledger search BEFORE the design decision
matrix.

### Lesson #2 — HONEST DEFER preserves operator-trust when alternatives exist

iter-249 SetUnitCapOverride had no LIVE alternative anywhere — the
honest defer just queued the work. iter-267 max_speed has TWO existing
LIVE alternatives (iter-99 SWFOC_SetUnitSpeed per-instance + iter-100
SWFOC_SetPerFactionSpeedMultiplier per-faction). The HONEST DEFER cycle
is appropriate here NOT because there's no path forward, but because
the existing LIVE wires already cover the operator's actual use cases
at per-instance + per-faction granularity, and the TYPE-LEVEL
extension would create operator-trust inconsistency with iter-258
max_hull/max_shield without strong evidence of operator demand.

### Lesson #3 — Semantic consistency is operator-trust currency

Promoting max_speed to a per-instance LIVE branch inside
SWFOC_SetUnitField would create the situation:

| Sub-field | LIVE scope |
|---|---|
| hull / shield / speed | per-instance |
| invuln_flag / prevent_death | per-instance (display flag) |
| max_hull / max_shield | **TYPE-LEVEL** |
| max_speed | per-instance (NEW iter-268 if rerouted) |

Operators reading the rationale would see max_hull and max_shield are
TYPE-shared (affects every unit of the type) but max_speed is
per-instance — **same prefix, different semantics**. This is a
documentation-decay trap. iter-260 already taught the lesson that
catalog rationale + dropdown labels must agree on caveat scope; iter-267
applies the lesson by NOT introducing the inconsistency in the first
place.

### Lesson #4 — A1.x arc length depends on ledger-state, not topic-state

Iter 224-228 (SetFireRate): 5 iters, +1 LIVE wire. Iter 230-234
(FreezeCredits): 5 iters, +4 LIVE flips. Iter 236-240 (SetCameraPos): 5
iters, +2 LIVE flips. Iter 242-246 (SetUnitField extras): 5 iters, +2
sub-field flips. Iter 248-249 (SetUnitCapOverride): **2 iters** honest
defer. Iter 257-261 (SetUnitField max_*): 5 iters, +2 sub-field flips.
Iter 267-268 (SetUnitField max_speed): **2 iters** honest defer
(planned).

**Pattern**: arc length is driven by ledger-state preparedness, not
topic complexity. When the ledger has reader-side offsets pre-pinned,
arcs ship in 5 iters. When the ledger lacks the relevant entries and
no quick alternative exists, arcs telescope to 2-iter honest-defer
cycles. **Discipline**: don't pad an honest-defer arc to 5 iters just
to match the canonical shape; ship the defer + extend the rationale +
queue the deep RE arc as a separate future task.

## What's next (iter 268+)

- **Iter 268**: HONEST DEFER close-out (this arc's iter 2/2). Catalog
  rationale extension + NEW pin test + capability surface regen + all
  gates verification + close-out doc.
- **Iter 269** (queued): next A1.x arc kickoff. Candidates:
  - **attack_power via iter-94 retry** — would mirror iter-94's
    rejected design with potentially a MinHook at the
    `Damage_Multiplier` read site. Higher RE risk than iter-258.
  - **respawn_ms (per-hero)** — needs per-hero respawn-timer table
    RVA, not in ledger. Iter-130 confirmed defer; needs RE arc to
    find the table.
  - **is_hero / respawn_enabled** — both higher-risk RTTI/behavior
    write paths; may need 5+ iter arcs.
- **Iter 270+**: Lua Playground preset menu refresh / next reverse-orphan
  audit (~iter-285 per cadence) / next README capstone (~iter-295 per
  cadence) / Phase2HookPending re-audit (~iter-282 per cadence).

## Iter 267 close-out

- This document is the iter 267 deliverable.
- **No bridge / dispatcher / VM / XAML / test changes** (pure RE +
  design doc).
- Bridge harness unchanged 1100/0. Verifier ledger lint unchanged 0/0
  at 318 entries. Editor 8168/0/8168 unchanged. Editor binary
  165,499,723 B unchanged. Bridge binary 406.5 KB unchanged.
- 109 → 109 buttons UNCHANGED. 104 → 104 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- 7th back-to-back A1.x arc kickoff this session — second honest-defer
  arc this session (iter-248-249 + iter-267-268).
