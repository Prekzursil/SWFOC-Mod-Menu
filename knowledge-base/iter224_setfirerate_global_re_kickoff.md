# Iter 224 — A1.3 SetFireRate global-level RE kickoff (2026-05-06)

Multi-iter arc kickoff for SetFireRate at the GLOBAL level. iter-101 + iter-130 + iter-132 + iter-221 audits all confirmed defer at the catalog level — no `rva_set_fire_rate` setter exists in the verified ledger. Iter-154 closed the per-unit form via Set_Rate_Of_Fire_Modifier; this arc closes the global form.

## RE findings

### Three verified consumer entries
- `rva_weapon_tick` @ 0x140387010 (size 0x158 = 344 bytes) — per-frame weapon update, cooldown via delta-time (3-tool consensus: ida + ghidra + binary_ninja)
- `rva_hardpoint_fire` @ 0x140387F50 (size 0x1A8 = 424 bytes) — per-hardpoint damage (3-tool consensus)
- `rva_fire_control_dispatch` @ 0x14038D730 (size 0x6C3 = 1731 bytes) — master fire control + LOS checks (3-tool consensus)

### WeaponTick callgraph
- 1 caller: 0x1403A76B0 (likely the per-tick game loop driver)
- 7 callees: sub_14012D2A0/D430 (animation state), sub_14035F790 (a guard), sub_140381FF0 (turret aim tracking), sub_140385CF0, **sub_140387170, sub_140387400** (the cooldown-tick dispatcher)

### Critical fields (from WeaponTick decompile body)

```c
void __fastcall WeaponTick(__int64 a1, int a2)
{
    if (!*(_QWORD *)(a1 + 32)) return;  // a1+32 = WeaponClass* (definition)
    __int64 v4 = *(_QWORD *)(a1 + 16);  // a1+16 = parent unit handle
    if (!v4) return;
    if ((*(_BYTE *)(v4 + 928) & 2) || ...) return;  // unit-flags guard

    __int64 v5 = *(_QWORD *)(a1 + 32);  // WeaponClass*
    unsigned int v6 = a2 - *(_DWORD *)(a1 + 96);  // dt = current_tick - last_tick
    *(_DWORD *)(a1 + 96) = a2;                     // store last_tick

    if ((unsigned int)(*(_DWORD *)(v5 + 72) - 5) <= 5
        && *(_BYTE *)(a1 + 108) == 1)
    {
        sub_140387400(a1, v6);  // <<< ACTUAL COOLDOWN DECREMENT >>>
        v5 = *(_QWORD *)(a1 + 32);
    }
    // ... aim tracking + animation state updates ...
}
```

### Critical field offsets identified
- **`a1 + 16`**: parent unit handle (`__int64`)
- **`a1 + 32`**: WeaponClass* (definition pointer; read-only at this layer)
- **`a1 + 40`**: current fire-cooldown timer (`float`); guard check `<= 0.0` at +0x14038709A in sub_140387400
- **`a1 + 96`**: last-tick timestamp (`int`); fire-rate dt source
- **`a1 + 108`**: weapon-active flag (`byte`); guard
- **`a1 + 110`**: another weapon flag (`byte`); guard
- **`v5 + 72`** (i.e. `WeaponClass + 72`): weapon-state enum (5..10 valid; outside range skips cooldown tick)
- **`v5 + 77`** (`WeaponClass + 77`): another weapon-class flag
- **`v5 + 78`** (`WeaponClass + 78`): another weapon-class flag

### sub_140387400 (cooldown dispatcher, 1904 bytes)
First few guards:
```c
void __fastcall sub_140387400(__int64 a1, unsigned int a2)
{
    __int64 v4 = *(_QWORD *)(a1 + 32);  // WeaponClass*
    if (!v4 || (unsigned int)(*(_DWORD *)(v4 + 72) - 5) > 5) return;
    // ...
    if (!byte_140A28640 || !*(_BYTE *)(a1 + 108)
        || (*(_BYTE *)(v4 + 77) == 1 && *(float *)(a1 + 40) <= 0.0)
        || ...) return;
    // ... cooldown decrement using a2 (dt) ...
}
```

The `a2` parameter (dt) is the per-tick delta time. **This is the scaling target for global fire-rate multiplier.**

## Design decision: Per-tick MinHook detour at WeaponTick (RECOMMENDED)

### Pattern selection: per-tick MinHook detour vs RTTI-driven setter

| Approach | Pros | Cons |
|---|---|---|
| **Per-tick MinHook detour at WeaponTick** | Same pattern as iter-96 (Take_Damage_Outer for damage); minimal code (~10-15 LoC bridge); single bool-mult global; doesn't touch per-weapon class instances; affects ALL units uniformly | Affects every active weapon; can't do per-weapon-class scaling; works at runtime only (not save-game) |
| **RTTI-driven WeaponClass setter** | Per-weapon-class control; persistent across saves; matches iter-100 SetSpeedOverride pattern | Needs RTTI walk for per-class field offset; many WeaponClass-derived classes (estimated 30+ from binary); 5-10 iter implementation |

**Verdict: Per-tick MinHook detour at WeaponTick.** Consistent with iter-96 SetDamageMultiplierGlobal pattern. Operator surface is `SWFOC_SetFireRateMultiplierGlobal(mult)`; bridge writes `g_fireRateMult_global` and detour scales `dt` arg passed to sub_140387400 by the multiplier.

### Implementation outline (iter 225-228)

**Iter 225 — Bridge LIVE wire**:
```cpp
// In bridge globals:
static std::atomic<float> g_fireRateMult_global{1.0f};

// New SWFOC function:
int Lua_SetFireRateMultiplierGlobal(lua_State* L) {
    float mult = static_cast<float>(luaL_checknumber(L, 1));
    if (mult < 0.0f) mult = 0.0f;  // negative would reverse cooldown
    if (mult > 100.0f) mult = 100.0f;  // sanity cap
    g_fireRateMult_global.store(mult);
    lua_pushboolean(L, 1);
    return 1;
}

int Lua_GetFireRateMultiplierGlobal(lua_State* L) {
    lua_pushnumber(L, g_fireRateMult_global.load());
    return 1;
}

// MinHook detour:
typedef void (__fastcall *pfn_WeaponTick)(__int64, int);
static pfn_WeaponTick g_origWeaponTick = nullptr;

void __fastcall HookedWeaponTick(__int64 a1, int a2) {
    int last_tick = *(int*)(a1 + 96);
    int dt = a2 - last_tick;
    float mult = g_fireRateMult_global.load();
    int scaled_dt = static_cast<int>(static_cast<float>(dt) * mult);
    int scaled_a2 = last_tick + scaled_dt;  // pretend more time passed
    g_origWeaponTick(a1, scaled_a2);
}

// In InstallHooks:
MH_CreateHook(reinterpret_cast<LPVOID>(GameBase + 0x387010),
              reinterpret_cast<LPVOID>(HookedWeaponTick),
              reinterpret_cast<LPVOID*>(&g_origWeaponTick));
```

**Iter 226 — Simulator handler + smoke tests**:
- Add simulator handler for `SWFOC_SetFireRateMultiplierGlobal` + `SWFOC_GetFireRateMultiplierGlobal`
- Bridge harness round-trip tests (set 0.5, get 0.5; set 2.0, get 2.0; clamp test set -1.0 → 0.0; clamp test set 200.0 → 100.0)
- Catalog entries (Live status; rationale references this design doc)

**Iter 227 — Editor button surface (Combat tab)**:
- New "Set GLOBAL fire rate multiplier" button alongside iter-96 "Set GLOBAL damage multiplier"
- Same UI pattern (single TextBox for multiplier value + Apply button)
- AllActions extension (Combat 14 → 15)
- 5-test pin file `Iter227SetFireRateMultiplierGlobalTests.cs`

**Iter 228 — Verify + republish + close-out**:
- Live test against running game (engine effect verified)
- Editor binary republish
- State doc sync
- Operator changelog 2026-05-06 supplement #2 documenting the iter 224-228 arc
- Mark A1.3 CLOSED in master-loop table

## Engine semantic caveat

Setting the multiplier scales the dt PASSED INTO sub_140387400, not the cooldown value itself. This means:
- `mult = 2.0f` → cooldown advances 2x as fast → fire-rate appears 2x as fast
- `mult = 0.5f` → cooldown advances at half speed → fire-rate appears halved
- `mult = 0.0f` → no time passes → weapons never tick → effectively freezes fire (use Suspend_AI for proper pause; this is an emergent side-effect)
- `mult > 0` always; negative values would reverse cooldown which the engine doesn't expect (clamped to 0.0)

This is operator-friendly: positive multiplier in the [0.5, 5.0] range covers the slow-mo to bullet-hell spectrum. Sanity cap at 100 prevents accidental int overflow in the dt math.

## Pattern lesson

This arc demonstrates that **multi-iter RE arcs need ~1 RE-design iter + ~3-4 implementation iters**. The iter-224 RE iter found:
1. Three verified consumer functions in the ledger (no setter)
2. The per-tick dispatcher (sub_140387400) is the cooldown logic
3. The dt parameter (a2 in WeaponTick → passed to sub_140387400) is the scaling lever
4. iter-96 Take_Damage_Outer pattern applies directly — same MinHook-detour-scaling-a-param technique

**Iter-224 conclusion: A1.3 SetFireRate global path UNBLOCKED.** Implementation queued for iter 225-228.
