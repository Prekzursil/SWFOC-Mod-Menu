# Iter 242 — A1.x SetUnitField remaining-fields RE kickoff (multi-iter arc, iter 1 of ~5)

**Date:** 2026-05-06
**Status at end of iter 242:** RE design doc complete; design decision matrix selected; iter 243-246 implementation outline ready.
**Predecessor arcs:** iter 224-228 (A1.3 SetFireRate, every-frame detour), iter 230-234 (A1.x FreezeCredits, bool-precedence detour), iter 236-240 (A1.x SetCameraPos, direct-call). Iter 242 is the **4th A1.x multi-iter arc this session**, extending iter-136's per-field-LIVE-branches mirror pattern.

---

## Headline finding

**3 of the 10 remaining sub-fields have GameObject offsets ALREADY pinned in `rvas.h`'s `GameObj` namespace** — the bridge can ship LIVE branches for them via direct memory writes with **zero new RE work**:

| Sub-field | Offset (in rvas.h) | Type | Strategy | Notes |
|---|---|---|---|---|
| `invuln_flag` | `GameObj::InvulnFlag` = +0x3A7 | byte | Direct write | Already used by iter-110 Make_Invulnerable_LuaWrapper path. Display-only flag (the actual invulnerability is the BehaviorMarker at +0x37D). |
| `prevent_death` | `GameObj::PreventDeath` = +0x3A1 (bit 0x80) | bit-flag | Direct read-modify-write of byte | Set by iter-153 Set_Cannot_Be_Killed(true). Writing +0x3A1 directly bypasses the engine state machine — same caveat as iter-23 flag-flipping memory note. |
| `owner_slot` | `GameObj::OwnerPlayerID` = +0x58 | int32 | Direct write | **HIGH RISK** — bypasses engine ownership change pipeline (iter-108 SWFOC_ChangeUnitOwnerLua is the proper LIVE path that calls `Change_Owner @ 0x574D0E` + propagates to selection/AI). Direct write may desync ownership-derived caches. **Recommend: defer this field to iter 247+ RTTI walk; route operator to iter-108 LIVE wire instead.** |

**Recommended iter 243 scope: 2 sub-fields (`invuln_flag` + `prevent_death`)** as immediate LIVE flips. Defer `owner_slot` due to engine-state-machine bypass risk. The harder 7 fields (`max_hull`, `max_shield`, `max_speed`, `attack_power`, `respawn_ms`, `is_hero`, `respawn_enabled`) need separate RE arcs for their respective offsets (currently neither in `rvas.h` nor `verified_facts.json`).

This is **smaller scope** than iter 230-234 (+4 LIVE) or iter 224-228 (+1 LIVE) — but the **per-field branches mirror pattern** is well-understood from iter-136, so iter 243's bridge work should be ~10-15 LoC for both fields combined.

---

## Pre-existing iter-136 implementation (`Lua_SetUnitField`)

```cpp
// Located at swfoc_lua_bridge/lua_bridge.cpp line 6341
static int Lua_SetUnitField(lua_State* L) {
    // Standard arg parsing + addr validation + ownership check
    // (enemy units READ-ONLY).

    std::string f = raw_f;
    if (f == "hull") {
        *reinterpret_cast<float*>(addr + RVA::GameObj::HP) = val;
        // ... LIVE direct write
    }
    if (f == "shield") {
        // SetFrontShield + SetRearShield engine helpers
        // ... LIVE
    }
    if (f == "speed") {
        // SetSpeedOverride engine helper
        // ... LIVE
    }

    // Phase-1 mirror fall-through for the other 10 fields.
    PendingUnitFieldWrite w;
    w.obj_addr = addrRaw;
    w.field    = raw_f;
    w.value    = val;
    g_pendingUnitFieldWrites.push_back(w);
    Log("[Bridge] SetUnitField(...) -- Phase 1 pending\n");
    fn_pushstring(L, "OK: unit-field write queued (Phase 2 offset-table hook pending)");
    return 1;
}
```

**Iter 243 inserts 2 new branches** between the existing `if (f == "speed")` block and the Phase-1 fall-through.

---

## Per-sub-field RE detail (iter 243 scope)

### `invuln_flag` @ GameObj+0x3A7 (byte)

**Current operator-facing surface**:
- Phase-1 mirror via `g_pendingUnitFieldWrites` (no engine effect).
- iter-110 SWFOC_MakeInvulnerableLua provides the proper engine-state-aware LIVE wire (sets the BehaviorMarker + hardpoint propagation via QueryInterface(0x16)).

**iter 243 LIVE branch**:
```cpp
if (f == "invuln_flag") {
    // Display-only flag. The actual gameplay-effective invulnerability lives
    // in the BehaviorMarker at +0x37D + per-hardpoint INVULNERABLE behavior
    // attachments (iter 110 hardpoint propagation). Writing +0x3A7 directly
    // updates the visual indicator without touching the behavior chain.
    // Operator should pair with iter-110 SWFOC_MakeInvulnerableLua for full
    // gameplay invulnerability.
    *reinterpret_cast<uint8_t*>(addr + RVA::GameObj::InvulnFlag) =
        (val != 0.0f) ? 0x01 : 0x00;
    Log("[Bridge] SetUnitField(addr=0x%llX, field=invuln_flag, value=%d) "
        "— LIVE direct write (display flag only; pair with MakeInvulnerableLua)\n",
        (unsigned long long)addr, (val != 0.0f) ? 1 : 0);
    fn_pushstring(L, "OK: invuln_flag written (LIVE — display only; pair with MakeInvulnerableLua for engine effect)");
    return 1;
}
```

**Engine semantic caveat**: writing +0x3A7 alone does NOT make the unit invulnerable — that requires the BehaviorMarker + hardpoint chain. This is the lesson from the `feedback_flag_flipping_vs_engine_state` memory: byte-flipping gameplay flags directly bypasses the engine state machines.

### `prevent_death` @ GameObj+0x3A1 (bit 0x80)

**Current operator-facing surface**:
- Phase-1 mirror via `g_pendingUnitFieldWrites`.
- iter-153 SWFOC_SetCannotBeKilledLua provides the proper LIVE wire that routes through the engine's Set_Cannot_Be_Killed Lua API.

**iter 243 LIVE branch**:
```cpp
if (f == "prevent_death") {
    // Bit 0x80 of GameObj+0x3A1. iter-153 SWFOC_SetCannotBeKilledLua sets
    // this bit via the engine Lua API (Set_Cannot_Be_Killed); direct write
    // here is for operator convenience when they have the obj_addr but not
    // a Lua handle. Same caveat as invuln_flag — engine-state machinery may
    // expect this bit to be paired with other behavior changes; operator
    // should prefer the iter-153 LIVE wire when possible.
    uint8_t* flag_byte = reinterpret_cast<uint8_t*>(addr + RVA::GameObj::PreventDeath);
    if (val != 0.0f) {
        *flag_byte |= 0x80;
    } else {
        *flag_byte &= 0x7F;
    }
    Log("[Bridge] SetUnitField(addr=0x%llX, field=prevent_death, value=%d) "
        "— LIVE direct bit write (bit 0x80 of +0x3A1)\n",
        (unsigned long long)addr, (val != 0.0f) ? 1 : 0);
    fn_pushstring(L, "OK: prevent_death bit set (LIVE — bit 0x80 of +0x3A1; operator may prefer SWFOC_SetCannotBeKilledLua)");
    return 1;
}
```

**Engine semantic caveat**: same as `invuln_flag` — direct bit-flip is a partial substitute for the engine Lua API. Catalog rationale should reference iter-153 as the preferred path.

### `owner_slot` (DEFERRED)

**Why deferred**: writing +0x58 directly bypasses:
1. Selection-list update (per-player vectors at GameModeClass+0x1C0 — iter 11 selection-system finding).
2. AI brain reassignment (AIPlayerClass instances per slot).
3. UI roster refresh (Diagnostics tab Get_Owner reader).
4. Save-game ownership consistency.

iter-108 `SWFOC_ChangeUnitOwnerLua` calls `Change_Owner @ 0x574D0E` which handles all of the above. Operators wanting to change owner should use that LIVE wire.

**Catalog rationale update for iter 243**: `owner_slot` stays Phase-1 mirror with explicit "use SWFOC_ChangeUnitOwnerLua for engine-aware ownership change" pointer.

---

## The 7 harder sub-fields (deferred to future arcs)

| Sub-field | RE complexity | Recommended arc |
|---|---|---|
| `max_hull` | Per-unit type max-HP table (XML-loaded, runtime-cached). RTTI walk to find runtime-write path. | A1.x MaxHull arc (iter 247+). |
| `max_shield` | Same as max_hull but for shield. Likely shares table with max_hull. | Bundled with MaxHull arc. |
| `max_speed` | Per-unit speed cap (different from `speed` override). Behavior layer at component array. | A1.x MaxSpeed arc. |
| `attack_power` | iter-94 finding: damage_multiplier consume site is per-unit attack scalar. RVA may already be partially pinned. | Re-audit iter-94 ledger findings; standalone arc. |
| `respawn_ms` | Per-unit respawn timer (different from global `Default_Hero_Respawn_Time` at 0xB169F0 from iter 130). | Per-hero respawn arc (iter 130 confirmed defer for per-hero path). |
| `is_hero` | Probably a vftable/RTTI bit (HeroClass derived). Direct write may break RTTI dispatch. | A1.x IsHero arc — high RTTI complexity. |
| `respawn_enabled` | Boolean toggle for per-unit respawn. Likely a behavior at the component array. | Bundled with respawn_ms arc. |

**Estimated arc count**: 4-5 future multi-iter arcs covering 7 fields. Each arc shares the iter-136/iter-243 pattern of inserting per-field LIVE branches into `Lua_SetUnitField`.

---

## Design decision matrix for iter 243

| Strategy | Pros | Cons | Verdict |
|---|---|---|---|
| **Direct memory write (chosen for invuln_flag + prevent_death)** | Zero new RE work; offsets already in rvas.h; ~5 LoC per field. | Bypasses engine state machines; partial-effect-only. | **CHOSEN with caveat docs** — operator gets convenience while catalog rationale points to LIVE alternatives (iter-110, iter-153). |
| **Engine-Lua-API wrapping (e.g. via DoString)** | Engine-state-aware. | Requires obj_addr → Lua handle conversion; some objects don't have stable Lua handles. | Rejected — already covered by iter-110 + iter-153 LIVE wires. |
| **Defer entirely** | No risk of partial-effect confusion. | Operator can't write these fields by obj_addr. | Rejected for invuln_flag + prevent_death — iter-136 already created the dispatcher precedent and operators expect field-name access. **Accepted for owner_slot** since the engine-state-machine bypass is too high-risk. |

---

## Implementation outline (iter 243-246)

### Iter 243 — Bridge LIVE wire shipped (~15 LoC, +2 LIVE flips)

**Note**: `SWFOC_SetUnitField` is already catalogued as Live (iter 136). Iter 243 doesn't add new catalog entries — it extends the existing entry's rationale to document the 2 new LIVE sub-fields. **+2 LIVE sub-field flips** but the catalog wire count stays at 149.

- `swfoc_lua_bridge/lua_bridge.cpp` `Lua_SetUnitField` function: insert 2 new branches (`invuln_flag` + `prevent_death`) after the existing `if (f == "speed")` block.
- `CapabilityStatusCatalog.cs`: extend `SWFOC_SetUnitField` rationale to list **5/13 sub-fields LIVE** (was 3/13): hull, shield, speed (iter 136) + invuln_flag, prevent_death (iter 243). Cross-reference iter-110 + iter-153 as preferred LIVE alternatives for the deeper engine-state-aware paths.
- Bridge harness 1100/0 GREEN. DLL + replay rebuilt clean.

### Iter 244 — Simulator handlers + 4-6 pin tests + reverse-orphan rebalance

- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs`: `HandleSetUnitField` (already exists for iter 136 hull/shield/speed) extended with branches for invuln_flag + prevent_death. Stores into existing `FakeUnit.InvulnFlag` + `FakeUnit.PreventDeath` byte fields.
- 4-6 test pin file `Iter244SetUnitFieldExtraFieldsSimulatorTests.cs` (catalog rationale updated + iter-242/iter-243 cross-references + simulator round-trip Set→Read for both fields + bit-flip semantics for prevent_death + display-only-vs-engine-effect caveat documentation).
- No reverse-orphan changes (SWFOC_SetUnitField already wired since iter 136).

### Iter 245 — UnitStatEditor tab native UX (if needed)

- Existing UnitStatEditor "Apply staged edits" button already routes through SWFOC_SetUnitField. Iter 245 may be a no-op if the staging UI already handles the new fields.
- If the staging UI doesn't have invuln_flag/prevent_death checkboxes, add them — 2 buttons (or checkboxes) + capability action references.
- Pin test for the staging-UI surface.

### Iter 246 — Live verify + close

- Bridge harness + verifier lint clean.
- HISTORY.md update with the 5-iter arc summary.
- STATUS.md master-loop SetUnitField row updated to **5/13 sub-fields LIVE**.
- Iter 247 = operator changelog (mirrors iter 229 / iter 235 / iter 241).

---

## Risks + open questions

1. **Direct memory write of game state bytes is the iter-23 anti-pattern**: the `feedback_flag_flipping_vs_engine_state` memory entry warns against byte-flipping gameplay flags directly because it bypasses engine state machines. **Mitigation**: catalog rationale must explicitly call out the partial-effect-only nature + point operators to engine-state-aware alternatives (iter-110, iter-153). The justification for iter-243 is operator convenience, not gameplay correctness.

2. **`prevent_death` bit-flip vs engine flag chain**: Set_Cannot_Be_Killed(true) likely sets multiple bits in addition to +0x3A1's 0x80. iter-243's bit-only write may not fully replicate the engine flag chain. **Mitigation**: catalog rationale documents this as "prevents the lethal-damage check, but does NOT engage hardpoint INVULNERABLE behavior or RTTI hero-respawn pathway."

3. **Per-unit-type ranges**: `invuln_flag` is a single byte (0/1) but other fields (max_hull, etc.) are unit-type-specific. The Phase-1 mirror's float type may need per-field reinterpretation. **Mitigation**: scope iter-243 to byte/bit fields only; the float-typed fields (max_hull, etc.) wait for their own RE arc.

4. **Pattern parity with iter-136**: iter-136 shipped 3/13 fields. iter-243 ships 2/13 → cumulative 5/13. The remaining 8 fields (after deferring owner_slot per the iter-242 design above) wait for future RE work. This is **steady incremental progress**, not a "big bang" SetUnitField close.

---

## Iter 242 close-out

- This document is the iter 242 deliverable.
- No bridge / dispatcher / VM / XAML / test changes. Pure RE + design doc.
- 109 → 109 buttons UNCHANGED. 111 → 111 native UX UNCHANGED.
- Verifier ledger lint untouched (no new entries this iter — the 2 sub-fields use existing pinned offsets in rvas.h's `GameObj` namespace).
- Iter 243 task creation queued at end of iter 242 close-out.

**Pattern lesson reinforced**: A1.x multi-iter arcs scale **down** as well as up. iter 224-228 shipped +1 LIVE flip (FireRate); iter 230-234 shipped +4 (FreezeCredits bool+mult bundle); iter 236-240 shipped +2 (Camera Set/Get); iter 242-246 will ship +2 sub-field LIVE flips (extending an existing wire). **Marginal cost = ~5-10 LoC per sub-field branch when the offset is already pinned.** The 5-iter shape stays invariant; the *contents* of each iter scale to the work required.
