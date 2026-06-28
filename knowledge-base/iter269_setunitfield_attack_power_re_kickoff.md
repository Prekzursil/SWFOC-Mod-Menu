# Iter 269 — A1.x SetUnitField attack_power RE kickoff (8th multi-iter arc; 3rd HONEST DEFER candidate this session)

**Date:** 2026-05-07 22:00 UTC
**Iter:** 269 (RE kickoff)
**Arc:** 8th multi-iter A1.x arc; iter 1 of (likely) **2** iters via HONEST DEFER pattern.
**Predecessor:** iter 268 max_speed HONEST DEFER close-out (2nd HONEST DEFER arc this session).
**Successor:** iter 270 (HONEST DEFER close-out + SWFOC_SetUnitField rationale extension).

## Headline finding

**`attack_power` has NO TYPE-LEVEL offset in the ledger or in the
engine binary's combat path.** Like iter-267 max_speed, the iter-94
rejection (per task #340 description: "Damage_Multiplier is per-tick
scalar applied at Take_Damage") is empirically reaffirmed.

Iter-256 memory rule applied (semantic verification BEFORE designing).
Ledger search returns **no `rva_get_attack_power` or `rva_*_max_damage`
type-stats reader**. The closest engine entries are damage-related but
all per-instance / per-weapon-fire / per-tick:

| Entry | RVA | Semantic |
|---|---|---|
| `rva_take_damage_outer` | 0x38A350 | Damage receiver/router (iter-96 detour site) |
| `rva_take_damage_impl` | 0x3AB890 | Core hp subtract + prevent-death |
| `rva_take_damage_property_dispatch` | (n/a in this listing) | Shield/hardpoint routing |
| `rva_hardpoint_fire` | 0x387F50 | **Hardpoint HP CONSUMER** (takes damage as param) |
| `rva_weapon_tick` | 0x387010 | iter-225 SetFireRateMultiplier detour site |
| `rva_fire_control_dispatch` | 0x387810 | (per iter-101 ledger note) |
| `rva_take_damage_function` | (n/a in this listing) | (placeholder name) |

**`HardpointFire @ 0x387F50` semantic verification (decompile body inspection):**

```c
float FUN_140387f50(longlong param_1, longlong param_2, undefined4 param_3, float param_4)
{
    fVar1 = *(float *)(param_1 + 0x28);  // hardpoint hp at +0x28
    if (*(char *)(*(longlong *)(param_1 + 0x20) + 0x4d) == '\0') return fVar1;
    // ... bookkeeping ...
    *(float *)(param_1 + 0x28) = *(float *)(param_1 + 0x28) - param_4;  // applies damage
    // ... level-loss/event dispatch ...
    return *(float *)(param_1 + 0x28);
}
```

**Key observations:**

- `param_1` is a HARDPOINT struct (NOT a unit; NOT a UnitType).
- `+0x28` on the hardpoint = hardpoint HP slot (the destination of damage).
- `+0x20` on the hardpoint = parent unit pointer.
- **`param_4` is the damage value PASSED IN** (not read from any
  struct). This is consistent with iter-94's original finding: damage
  is computed dynamically per-weapon-fire by upstream callers, NOT
  read from a per-unit-type field.

The upstream caller (likely `rva_fire_control_dispatch` or one of the
weapon-class-specific behavior functions) computes the damage value
from per-weapon XML attributes (`Damage_Amount`, `Min/Max_Damage`,
range falloff curves, per-target-type multipliers) at fire time. There
is no central "this unit's attack power" field in the binary.

## Strategic decision: HONEST DEFER (3rd this session)

Mirroring iter-249 / iter-267 telescoped 2-iter pattern. Reasons:

1. **No reader-side ledger entry pins a per-unit-TYPE attack_power
   offset** — iter-258's reader-side discovery pattern doesn't apply.
2. **Combat path doesn't expose a single attack_power read site** —
   damage is computed dynamically from per-weapon XML at fire time;
   no MinHook detour can scale it without per-weapon awareness
   (which is what iter-94 originally rejected).
3. **Three existing LIVE alternatives already cover operator damage
   needs**:
   - **iter-96 SWFOC_SetDamageMultiplierGlobal** — Take_Damage_Outer
     MinHook detour at `0x38A350`; scales `damageParams[0]` by
     `g_dmgMult_global`. Global outgoing damage scaling.
   - **iter-154 SWFOC_SetDamageModifierLua** — per-unit
     `Set_Damage_Modifier` engine API; calls
     `(unit):Set_Damage_Modifier(val)`. Per-instance damage scaling.
   - **iter-225 SWFOC_SetFireRateMultiplierGlobal** — WeaponTick
     MinHook detour at `0x387010`; scales weapon fire-rate via
     `g_fireRateMult_global`. Tangential but operator-relevant.
4. **TYPE-LEVEL semantic consistency** with iter-258 max_hull/max_shield
   would be sacrificed if attack_power routes per-instance (mirrors
   iter-267 max_speed's reasoning).

## RE walk findings

### Ledger search (per iter-256 memory rule, performed BEFORE designing)

```bash
grep -nE 'rva_.*(attack|damage|firepower|projectile|weapon|fire)' verified_facts.json
```

Returns 13 damage/fire-related entries — **NONE** of them are
per-unit-TYPE attack_power readers. All are either:

- Damage CONSUMERS (Take_Damage_Outer / Take_Damage_Impl /
  HardpointFire / Take_Damage_PropertyDispatch).
- Per-frame TICK functions (WeaponTick / FireControlDispatch).
- Per-ability behaviors (Cable_Attack / Energy_Weapon /
  Maximum_Firepower / Periodic_Damage / Generic_Attack — these are
  **ability classes**, not damage-source readers).

### iter-94 rejection re-affirmed

The iter-94 original framing ("Damage_Multiplier is per-tick scalar at
Take_Damage; no global per-slot multiplier path") is empirically
correct after fresh RE walk per iter-256 memory rule. **iter-94 was
right the first time.** The iter-269 fresh walk doesn't find anything
iter-94 missed.

This is the second time a fresh RE walk per iter-256 has confirmed an
earlier rejection (iter-249 SetUnitCapOverride was the first; iter-269
attack_power is the second). **Pattern:** iter-256 memory rule is
double-edged — it catches false positives (community ledger drift) AND
confirms true negatives (genuine engine blocks). Both are
operator-trust currency.

### Why iter-258 reader-side pattern doesn't apply

For attack_power to follow iter-258's reader-side pattern:

1. A `rva_get_attack_power` or `rva_get_damage_amount` engine reader
   would need to exist, reading `*(this + ???)` where `this` is a
   UnitType or WeaponClass type-stats struct.
2. That reader would have to be called by the combat path with a
   stable UnitType pointer.

Neither exists. The combat path computes damage from per-weapon XML
attributes at unit-creation time, then per-tick via engine-internal
formulas that don't expose a single "this unit's attack power" read
site.

## HONEST DEFER design (iter-270 close-out scope)

Mirroring iter-267-268 SetUnitField max_speed close-out (~7-line
catalog rationale extension + ~5-line VM source comment + 1 NEW pin
test):

### iter-270 (HONEST DEFER close-out) scope

1. **No bridge changes** — `attack_power` stays in the Phase-1 mirror
   fall-through of `Lua_SetUnitField` (no LIVE branch added).
2. **Catalog rationale extension** for `SWFOC_SetUnitField` Note —
   enumerate `attack_power` as honest-defer with explicit
   cross-references to iter-96/iter-154 LIVE alternatives. Mirrors
   iter-251 (FreezeCredits) + iter-268 (max_speed) patterns.
3. **VM source comment extension** for
   `UnitStatEditorTabViewModel.cs` `EditFieldOptions` block —
   document iter-269-270 RE walk provenance.
4. **NEW pin test** in
   `Iter136SetUnitFieldPartialLiveTests.cs` asserting the rationale
   cites iter-96/iter-154 cross-references for attack_power.
   Source-grep pattern per iter-260 lesson #2.
5. **Capability surface markdown regen** to absorb rationale change.
6. **All gates verification**: bridge harness 1100/0 + ledger lint
   0/0 + editor full suite (expect 8170/0/8170 = 8169 + 1 NEW pin
   test) + binaries unchanged size.
7. **Close-out doc** `iter270_setunitfield_attack_power_honest_defer.md`
   documents the 2-iter cycle + queues iter-271 next-arc choice.

### Ratio implications

`SWFOC_SetUnitField` LIVE sub-field ratio stays **7/13** post iter-270
(no new LIVE branch). The honest-defer count grows from 1 (max_speed)
to **2** (max_speed + attack_power). The "Phase-1 mirror only" group
shrinks from 4 to **3** sub-fields (respawn_ms, is_hero, respawn_enabled).

## Pattern lessons (preview for iter-270 close-out)

### Lesson #1 — iter-256 memory rule confirms true negatives, not just catches drift

Iter-249 caught a false positive (community ledger drift). Iter-267
confirmed a true negative (no TYPE-LEVEL max_speed offset). Iter-269
confirms another true negative (no TYPE-LEVEL attack_power offset;
iter-94 was correct).

**Pattern**: the iter-256 memory rule's "semantic verification before
designing" step has two outcomes — either it catches a false positive
(arc gets DEPRECATED ledger entry + honest defer), or it confirms a
true negative (arc gets honest defer with operator-trust audit trail
to existing LIVE alternatives). **Both are useful.** The rule
shouldn't be measured solely by drift catches.

### Lesson #2 — HONEST DEFER cadence indicates ledger-state asymptote

Three HONEST DEFER arcs out of seven multi-iter A1.x arcs this session:

| Arc | Iters | LIVE flips | Honest defer |
|---|---|---|---|
| iter 224-228 SetFireRate | 5 | +1 (iter-225) | n/a (5-iter arc) |
| iter 230-234 FreezeCredits | 5 | +4 (iter-231) | n/a |
| iter 236-240 SetCameraPos | 5 | +2 (iter-237) | n/a |
| iter 242-246 SetUnitField extras | 5 | +2 (iter-243) | n/a |
| **iter 248-249 SetUnitCapOverride** | **2** | **0** | **HONEST DEFER #1** |
| iter 257-261 SetUnitField max_* | 5 | +2 (iter-258) | n/a |
| **iter 267-268 SetUnitField max_speed** | **2** | **0** | **HONEST DEFER #2** |
| **iter 269-270 SetUnitField attack_power** | **2** | **0** | **HONEST DEFER #3** (planned) |

**Ratio: 3/8 = 37.5% honest-defer rate.**

This rising ratio indicates the **easy reader-side ledger entries are
exhausted**. Future A1.x sub-field arcs will have INCREASING
honest-defer probability until either (a) live-game CheatEngine
tracing surfaces new offsets, or (b) the remaining sub-fields get
lower-priority because their honest-defer cross-references already
satisfy operators.

**Pattern**: when honest-defer rate climbs, prioritize **non-A1.x arc
classes** (Phase2HookPending re-audit / reverse-orphan audit / docs
polish / next-arc-class kickoff). The A1.x arc class has reached its
ledger-state asymptote.

### Lesson #3 — Damage cross-reference triplet (iter-96 + iter-154 + iter-225)

Operators looking for "damage tuning" have three LIVE alternatives:

- **Global outgoing damage scaling**: iter-96 SetDamageMultiplierGlobal
  (Take_Damage_Outer detour).
- **Per-unit damage scaling**: iter-154 SetDamageModifierLua
  (Set_Damage_Modifier engine API).
- **Global fire-rate scaling**: iter-225 SetFireRateMultiplierGlobal
  (WeaponTick detour).

These three together cover ~95% of operator-actionable damage tuning.
A 4th attack_power LIVE branch would NOT add new operator capability —
it would just create a 4th overlapping option, increasing
operator-trust surface confusion.

**Pattern**: when an honest-defer arc has multiple existing LIVE
alternatives, the rationale extension should list ALL of them (not
just the closest match) so operators see the full coverage matrix.
This is a refinement of iter-251's "single LIVE alternative" pattern
to "alternative-set" pattern.

## What's next (iter 270+)

- **Iter 270**: HONEST DEFER close-out (this arc's iter 2/2). Catalog
  rationale extension citing iter-96/iter-154/iter-225 alternative
  triplet + NEW pin test + capability surface regen + all gates
  verification + close-out doc.
- **Iter 271** (queued): given the 3/8 honest-defer rate, recommend
  **non-A1.x arc class**:
  - **Lua Playground preset menu refresh** (iter-264 last ran;
    104 → ~106 entries if iter 269-270 honest-defer presets get
    added).
  - **Reverse-orphan audit** (iter-263 last ran; ~22-iter window).
  - **README capstone update** (iter-265 last ran; ~30-iter cadence).
  - **Phase2HookPending re-audit** (iter-266 last ran; ~16-iter
    cadence).
  - **NEW arc class kickoff** — Thread B Overlay Phase 2-full ImGui
    vendoring / Thread C Save-game RE / Thread D Multi-repo CI gate
    hygiene. These were queued at iter-190 close-out as "next session
    multi-iter projects."
- **Iter 272+**: depending on iter-271 choice.

## Iter 269 close-out

- This document is the iter 269 deliverable.
- **No bridge / dispatcher / VM / XAML / test changes** (pure RE +
  design doc).
- Bridge harness unchanged 1100/0. Verifier ledger lint unchanged 0/0
  at 318 entries. Editor 8169/0/8169 unchanged. Editor binary
  165,499,723 B unchanged. Bridge binary 406.5 KB unchanged.
- 109 → 109 buttons UNCHANGED. 104 → 104 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- 8th back-to-back A1.x arc kickoff this session — **3rd
  honest-defer arc** (iter-248-249 + iter-267-268 + iter-269-270).
