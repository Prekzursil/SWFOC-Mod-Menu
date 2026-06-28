# Iter 230 — A1.x FreezeCredits global RE kickoff (multi-iter arc, iter 1)

**Date:** 2026-05-06
**Status at end of iter 230:** RE design doc complete; design decision matrix selected; iter 231-234 implementation outline ready.
**Predecessor arc:** iter 224-228 closed A1.3 SetFireRate global via WeaponTick @ 0x387010 MinHook detour. Iter 230 mirrors iter 224's shape exactly.

---

## Headline finding

**`AddCredits` @ RVA `0x27F370` is the universal engine credit-adjust function.** Both gains AND spends route through it (positive `a2` = gain, negative `a2` = spend). 47 callers across the binary including hooks for build cost, unit purchase, story rewards, AI subsidies, planet ownership transfer, etc. — detouring this single function gives complete economy control with one MinHook installer.

This is the **simplest possible target** in the deferred A1.x set:
- Single function, 259 bytes, clean prototype `float __fastcall(__int64 player, float amount, char track)`
- 4-tool consensus VERIFIED in `verified_facts.json` (`rva_add_credits`)
- Field offsets already pinned in ledger (`struct_player_credits` = PlayerClass+0x70 float32)
- Hot but small — typical caller count is 1-4 calls per caller, so detour overhead is negligible
- No RTTI dissection needed (unlike SetUnitCapOverride or SetCameraPos)

---

## Decompile body (extracted from `knowledge-base/decompile_corpus/ida_full/full_b70-71.json`)

```c
// AddCredits @ 0x14027F370 (RVA 0x27F370). 259 bytes. 47 callers.
// Prototype: float __fastcall(PlayerClass*, float delta, char track_event)
float __fastcall sub_14027F370(__int64 a1, float a2, char a3)
{
  float v4 = a2;

  // Positive deltas (income) get scaled by the player's income multiplier.
  // Negative deltas (spends) are NOT scaled — they cost what the engine says
  // they cost. This is by-design (you don't want a "build cost discount" to
  // also halve the refund when a unit is sold).
  if (a2 > 0.0) {
    __int64 v6 = *(_QWORD *)(a1 + 864);  // PlayerClass+0x360: scaling-context pointer
    if (v6)
      v4 = a2 * *(float *)(sub_1404B0500(v6) + 32);  // income multiplier
  }

  // Apply the (possibly scaled) delta to the credits balance.
  float v7 = v4 + *(float *)(a1 + 112);   // PlayerClass+0x70 = credits (float32)
  *(float *)(a1 + 112) = v7;

  // Negative-balance guard.
  if (v7 < 0.0)
    *(_DWORD *)(a1 + 112) = 0;

  // Cap check. PlayerClass+0x74 = credit cap. -1.0 in cap means "no cap".
  // The middle clause is some game-state guard (probably "is in skirmish?").
  if ( *(float *)(a1 + 116) >= 0.0
    && (qword_140B15418
     && (*(unsigned __int8 (__fastcall **)(__int64))(*(_QWORD *)qword_140B15418 + 248LL))(qword_140B15418) == 1
     || !HIBYTE(word_140B155C4))
    && *(float *)(a1 + 112) > *(float *)(a1 + 116) )
  {
    *(_DWORD *)(a1 + 112) = *(_DWORD *)(a1 + 116);  // clamp to cap
  }

  // Notify economy event listeners (UI, AI brain, achievement system).
  sub_14028F570(&qword_140B153E0, 2, *(unsigned int *)(a1 + 76));
  sub_140326400(qword_140B15418 + 160, a1, (unsigned int)(int)*(float *)(a1 + 112));

  // Tracking-flag callback (used for analytics/replays — only fires when a3=1).
  if (a3 && *(_QWORD *)(a1 + 864))
    sub_1404B0310();

  return *(float *)(a1 + 112);  // return new balance
}
```

### Field offsets confirmed

| Offset | Type | Meaning | Source |
|---|---|---|---|
| `PlayerClass+0x70` (`+112`) | `float32` | Current credits balance | `struct_player_credits` ledger entry + this decompile |
| `PlayerClass+0x74` (`+116`) | `float32` | Credit cap (-1 = no cap) | **NEW finding from this decompile** |
| `PlayerClass+0x360` (`+864`) | pointer | Scaling-context (income mult source) | This decompile |

### New finding for the ledger

`PlayerClass+0x74` = credit cap as float32. The cap is the `-1.0`-sentinel "no cap" pattern (negative cap value disables clamping). This will become a candidate for a follow-up `SetCreditCap` LIVE wire if the operator wants to lift caps dynamically — but iter 230 stays scoped to FreezeCredits + CreditMultiplier, leaves the cap field as documentation only.

**Action item for iter 234 close-out**: append `struct_player_credit_cap` entry to `verified_facts.json` referencing this decompile + AddCredits + the iter 230-234 arc.

---

## Design decision matrix

The detour can be one of three shapes:

| Shape | Lua API surface | Pros | Cons | Verdict |
|---|---|---|---|---|
| **Bool freeze** | `Lua_SetCreditsFreezeGlobal(bool)` | Simplest. One bool atomic. Operator-mental-model is "click freeze on, click freeze off." | All-or-nothing; can't "halve income but allow full spending." | **PARTIAL — ship as one-half** |
| **Scalar multiplier** | `Lua_SetCreditsMultiplierGlobal(float)` | Pattern parity with iter-96 SetDamageMultiplierGlobal + iter-225 SetFireRateMultiplierGlobal. mult=0 effectively freezes. mult=2 double income. | mult=0 also blocks negative deltas (spends), so units become free. Same engine-effect as bool freeze when mult=0 but harder for operator to reason about. | **PARTIAL — ship as the other half** |
| **Bool freeze + Scalar mult** | Both above | Operator gets both knobs. Bool wins when both are set (freeze short-circuits before mult applies). | Slightly more bridge code (~25 LoC vs ~15). Two atomics instead of one. | **CHOSEN** |

**Why both**: the operator wants two distinct workflows:
1. *"Pause economy entirely"* during a cinematic recording → bool freeze (no spend, no income, no AI economic thinking — clean snapshot).
2. *"Easy/Hard preset"* for streaming → scalar multiplier (boost or reduce income proportionally — different game feel without breaking spending).

The bool wins-over-mult precedence avoids ambiguity: when freeze=true, ignore the mult entirely.

### Reject: Take_Credits-only hook

The iter 221 audit suggested "Take_Credits hook (similar to iter-96 Take_Damage_Outer pattern)." Rejected because **there is no separate Take_Credits function** — both gains and spends route through `AddCredits` with the sign of `a2` distinguishing them. Hooking AddCredits is the iter-96-equivalent here.

---

## Detour pseudocode

```cpp
// In lua_bridge.cpp (mirrors iter-225 WeaponTick block exactly)
static std::atomic<bool>  g_creditsFreeze_global{false};
static std::atomic<float> g_creditsMult_global{1.0f};
typedef float (__fastcall *pfn_AddCredits)(__int64, float, char);
static pfn_AddCredits real_AddCredits = nullptr;

static float __fastcall Hook_AddCredits(__int64 a1, float a2, char a3) {
    // Bool freeze precedence: short-circuit ENTIRELY when freeze active.
    // Don't even call the original — that prevents the +112 write, the
    // event notification, and the tracking callback. Returns the unchanged
    // balance the same way the original returns the new balance.
    if (g_creditsFreeze_global.load(std::memory_order_relaxed)) {
        return *reinterpret_cast<float*>(a1 + 112);
    }

    // Multiplier mode: scale the delta. mult=1.0 fast-path like iter-225.
    const float mult = g_creditsMult_global.load(std::memory_order_relaxed);
    if (mult == 1.0f) {
        return real_AddCredits(a1, a2, a3);
    }
    return real_AddCredits(a1, a2 * mult, a3);
}

static int Lua_SetCreditsFreezeGlobal(lua_State* L) {
    bool freeze = (fn_tonumber(L, 1) != 0.0);
    g_creditsFreeze_global.store(freeze, std::memory_order_relaxed);
    Log("[Bridge] SetCreditsFreezeGlobal(%s) -- LIVE\n", freeze ? "true" : "false");
    fn_pushstring(L, freeze
        ? "OK: credits frozen (LIVE -- AddCredits short-circuited)"
        : "OK: credits unfrozen (LIVE)");
    return 1;
}

static int Lua_GetCreditsFreezeGlobal(lua_State* L) {
    fn_pushnumber(L, g_creditsFreeze_global.load() ? 1.0 : 0.0);
    return 1;
}

static int Lua_SetCreditsMultiplierGlobal(lua_State* L) {
    double mult = fn_tonumber(L, 1);
    if (mult < 0.0) mult = 0.0;        // no negative deltas
    if (mult > 100.0) mult = 100.0;    // overflow guard
    g_creditsMult_global.store(static_cast<float>(mult), std::memory_order_relaxed);
    Log("[Bridge] SetCreditsMultiplierGlobal(mult=%.3f) -- LIVE\n", mult);
    fn_pushstring(L, "OK: credit multiplier applied (LIVE -- AddCredits delta scaling)");
    return 1;
}

static int Lua_GetCreditsMultiplierGlobal(lua_State* L) {
    fn_pushnumber(L, static_cast<double>(g_creditsMult_global.load()));
    return 1;
}
```

### Engine semantic caveats

| Caveat | Behavior |
|---|---|
| `freeze=true + mult=anything` | Bool freeze wins. Mult ignored. |
| `freeze=false + mult=0.0` | All deltas zeroed. Effectively a soft freeze (still calls real_AddCredits with 0 delta — fires events, clamps to negative-balance-guard, etc.). Distinguishable from hard freeze if operator listens for AddCredits events. |
| `freeze=false + mult=2.0` | Income AND spending both 2x. Dev should pick whether they want this for streaming difficulty or a ratio-only toggle. |
| `freeze=false + mult=0.5` | Half income, half spending. Useful for "low-economy" matches without a ban on spending. |
| `freeze=false + mult=-1` | Clamped to 0 (sanity). |
| `freeze=false + mult > 100` | Clamped to 100 (overflow guard). |
| Cap behavior | Cap at +0x74 still applies — multiplier-induced gains hit the cap normally. Freeze short-circuits before the cap check. |
| AI subsidies | AI factions also use AddCredits, so freeze blocks AI economic thinking too. (For asymmetric "freeze player only", a per-player setter is a follow-up wire — not in this arc.) |

### Pattern parity check

| Pattern | Iter-96 (Damage) | Iter-225 (FireRate) | Iter-230 (Credits) |
|---|---|---|---|
| Hook target | `Take_Damage_Outer @ 0x38A350` | `WeaponTick @ 0x387010` | `AddCredits @ 0x27F370` |
| Atomic globals | `g_dmgMult_global` | `g_fireRateMult_global` | `g_creditsFreeze_global` + `g_creditsMult_global` |
| Fast-path for mult=1.0 | Yes | Yes | Yes (and freeze=false) |
| Sanity clamp | `< 0 → 0` | `[0, 100]` | `[0, 100]` for mult; bool for freeze |
| Lua API count | 2 (Set/Get) | 2 (Set/Get) | 4 (SetFreeze/GetFreeze + SetMult/GetMult) |
| Catalog entries | 2 Live | 2 Live | 4 Live |
| Operator UX | Combat tab Apply (GLOBAL) | Combat tab Apply (GLOBAL) iter-227 | Economy tab? Or PlayerState? **TBD iter 233** |

---

## iter 231-234 implementation outline

### iter 231 — Bridge LIVE wire shipped (~20 LoC bridge)

- `swfoc_lua_bridge/rvas.h`: `RVA::Add_Credits = 0x27F370`
- `swfoc_lua_bridge/lua_bridge.cpp`: pseudocode block above (Hook_AddCredits + 4 Lua functions + 4 registrations + MinHook installer entry).
- `CapabilityStatusCatalog`: 4 NEW Live entries (`SWFOC_SetCreditsFreezeGlobal`, `SWFOC_GetCreditsFreezeGlobal`, `SWFOC_SetCreditsMultiplierGlobal`, `SWFOC_GetCreditsMultiplierGlobal`). Phase-1 mirror Note for `SWFOC_FreezeCredits` updated to point at the iter-231 LIVE alternative.
- Bridge harness 1100/0 GREEN. DLL + replay rebuilt clean.
- **+4 LIVE flips. 143 → 147 LIVE wires.** (Largest single-iter LIVE flip count of the master loop — iter-225 was +1, iter-96 was +1; this is +4 because of the 2-pair design.)

### iter 232 — Simulator handlers + pin tests

- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs`: 4 `Reg()` handlers + Set/Get methods for each global.
- `tests/SwfocTrainer.Tests/Simulator/FakeGameState.cs`: `bool GlobalCreditsFreeze` + `float GlobalCreditsMultiplier` fields + handler logic that mirrors bridge: freeze=true → block credit changes; mult applied to delta; clamp range.
- 8-test pin file `Iter232CreditsFreezeAndMultGlobalSimulatorTests.cs` (catalog x4 Live + iter-230/231 cross-references + 2 FakeGameState fields + freeze on/off round-trip + mult round-trip + clamp lower/upper + freeze-precedence check).
- Reverse-orphan snapshot: add 4 entries pending iter 233 UX.
- Verify 50/50+ filtered tests GREEN.

### iter 233 — Editor native UX

**Tab choice — TBD between Economy and PlayerState:**
- Economy tab is the natural home, BUT it's currently mostly tooling-stub (no rich UX). Putting native LIVE buttons there might require building out the tab.
- PlayerState tab already has 16 buttons covering player-receiver wires; adding a 5th "GLOBAL" group there would mix per-player and global semantics confusingly.
- **Recommendation**: Economy tab. Adds 1 GroupBox "GLOBAL economy controls (LIVE)" with: Freeze/Unfreeze toggle + multiplier slider + Apply (GLOBAL) + Read (GLOBAL). 4 buttons total.
- Mirrors iter-227 pattern (Combat tab Apply (GLOBAL) + Read (GLOBAL)) but with a bool toggle on top.
- 6-test pin file `Iter233CreditsFreezeAndMultNativeUxTests.cs`.
- Editor republished.

### iter 234 — Live verify + close

- Bridge harness verify (1100/0).
- Verifier ledger lint (0/0). **Append `struct_player_credit_cap` entry as described above.**
- HISTORY.md updated with the 5-iter arc summary (similar to iter-228's A1.3 entry).
- STATUS.md master-loop table for FreezeCredits row updated to **CLOSED iter 231 + offline VERIFIED iter 234**, `[LIVE-PENDING]` for next live-attached session.
- **Operator changelog iter 235** (single-iter docs polish, mirrors iter 229 pattern) covering the iter 230-234 5-iter arc.

---

## Risks + open questions

1. **Cap clamp interaction with mult > 1**: when mult=2 and the player is under-cap, gains 2x normally. When the new balance would exceed cap, engine clamps. So mult=2 doesn't let you exceed cap — it just gets you there faster. This is correct behavior; flag in catalog rationale.

2. **Negative-balance guard already engine-side**: line `if (v7 < 0.0) *(...) = 0;` means even with freeze=false + mult=2, deductions can't push balance negative. Engine handles this; bridge doesn't need extra logic.

3. **Tracking flag (a3) and side effects**: when `a3 && *(_QWORD *)(a1 + 864)`, sub_1404B0310 fires (analytics/replay tracker). Freeze short-circuits this, so analytics will see fewer events when frozen. This is intentional but worth a catalog note.

4. **AI subsidies are also blocked**: AI factions use AddCredits for income too. Freeze=true blocks ALL factions equally. For "freeze player only", a follow-up per-player wire is needed — out of scope for this arc.

5. **Performance**: 47 callers of AddCredits is hot but each call is small (returns a float). The fast-path (`mult==1.0 && freeze==false`) skips the wrapper logic and hits the original directly, so steady-state overhead is one atomic load + one branch. Negligible.

6. **No engine-state corruption risk**: the function returns a float (the new balance). The detour respects the prototype, so callers that read the return value continue to work even when frozen.

---

## Iter 230 close-out

- This document is the iter 230 deliverable.
- No bridge / dispatcher / VM / XAML / test changes. Pure RE + design doc.
- 109 → 109 buttons UNCHANGED. 111 → 111 native UX UNCHANGED.
- Verifier ledger lint + bridge harness untouched (no code).
- iter 231 task creation queued at end of iter 230 close-out.

**Pattern lesson reinforced**: multi-iter RE arcs (iter 224-228 + iter 230-234) follow `~1 RE-design + ~1 bridge LIVE + ~1 sim+tests + ~1 editor UX + ~1 verify+close` shape. Iter 230 uses the same template as iter 224.
