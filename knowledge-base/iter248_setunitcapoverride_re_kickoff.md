# Iter 248 — A1.x SetUnitCapOverride RE kickoff (multi-iter arc, iter 1 of ~5)

**Date:** 2026-05-06
**Status at end of iter 248:** RE design doc complete; design decision matrix selected (Option A — reader-detour + per-slot override map); iter 249-252 implementation outline ready.

---

## ⚠️ ITER 249 CORRECTION (2026-05-06 23:50 UTC)

**The iter-248 strategy is INVALIDATED by the iter-249 RE walk.** Reading the disassembly of `sub_14028DBE0` at offset 0x38F (= 0x14028DF6F − 0x14028DBE0) revealed:

```
14028df6b  call j_j_free        ; <-- standard string deallocation thunk
14028df6f  mov [rbp+var_30], rdi ; <-- post-free cleanup, NOT unit-cap calc
14028df70  mov [rbp+var_30], rdi
14028df74  mov [rbp+var_28], 0Fh
```

The address 0x14028DF6F is **inside a string-deallocation cleanup block**, not a unit-cap calculation. The surrounding function `sub_14028DBE0` references string `aThestorymodeLo` ("TheStoryMode_Long" or similar) — this is an **event-handler / script-loader**, not a cap-reading function.

**The Apocalypticx CE community ledger entry `rva_apocalypticx_unit_cap_gc @ 0x28DF6F` is MIS-LABELED in the current 2026 binary fingerprint.** The AOB likely matched a different function in the original (older) binary the CE table was authored against. This is the **same pattern as iter-105 SetUnitShield → iter-128 correction** (the legacy ledger entry was wrong; iter-128 found the actual function via callgraph).

**Iter 249 actions taken**:
1. Marked `rva_apocalypticx_unit_cap_gc` as **DEPRECATED** in `verified_facts.json` with the iter-249 correction reason.
2. Updated `Lua_SetUnitCapOverride` bridge stub comment to reflect "DEFERRED CONFIRMED iter 249" (no catalog flip — entry stays Phase2HookPending).
3. Updated this design doc with the correction (this section).

**Iter 250+ recommendation**: SetUnitCapOverride LIVE wire **DEFERRED to a future deeper-RE arc** that requires either:
- Live-game CheatEngine tracing to find the canonical cap reader via "find what reads this address" on a known unit-cap memory location.
- IDA MCP interactive analysis to walk Build_Validate xrefs and identify the cap-query chokepoint.
- A community CE table refresh to re-AOB the cap calculation against the current 2026 binary.

This matches the **iter-130 SetFireRate "DEFERRED CONFIRMED"** + **iter-131 SetGameSpeed "CONFIRMED DEFER"** pattern — genuine RE blocks are honest defer with clear next-step requirements.

**The 5-iter arc shape collapses to a 2-iter "RE kickoff + correction-with-defer" cycle** (iter 248 + iter 249). This is also a valid arc completion shape — proving the cadence is the right unit and not every arc must produce a LIVE flip. **Honest defers are arc completions too.**

**Pattern lesson new for iter 249**: **community CE table AOBs lose accuracy across binary versions**. The 2026 SWFOC binary has different code layout than the binary the Apocalypticx table was authored against. Future RE that uses community CE tables must verify the AOB-pinned function is semantically what the table label claims, not just that the address resolves. **AOB drift across binary versions is a NEW drift class** distinct from catalog drift / simulator drift.

---

## Original iter 248 design (PRESERVED for archival; superseded by iter 249 correction)
**Predecessor arcs:** iter 224-228 (A1.3 SetFireRate, every-frame detour), iter 230-234 (A1.x FreezeCredits, bool-precedence detour), iter 236-240 (A1.x SetCameraPos, direct-call), iter 242-246 (A1.x SetUnitField extras, direct memory write inside existing wire). **5th A1.x multi-iter arc this session**.

---

## Headline finding

**The Apocalypticx CE community ledger entry `rva_apocalypticx_unit_cap_gc @ 0x28DF6F` is a READER pin (mid-function AOB), not a setter.** It's at offset 0x38F into `sub_14028DBE0` (3102-byte function). The actual SETTER for unit cap doesn't exist as a discrete function — caps are stored in a per-faction/per-slot data structure that's loaded from XML at game-start and queried by reader functions throughout the build-validate / UI-update pipeline.

**Recommended strategy: Option A — MinHook detour at the canonical cap reader.** Pattern matches iter-96 Take_Damage_Outer + iter-225 WeaponTick exactly: hook the reader, swap in operator-supplied per-slot override when present, fall through to original when absent. The detour is reversible (operator clears override → reader returns true engine value).

Bridge already has a Phase-1 stub `Lua_SetUnitCapOverride(slot, cap)` at line 3357 storing into `g_pendingUnitCapOverride` map. **Iter 249 just flips the catalog from Phase2HookPending → Live and adds the detour.**

---

## Existing ledger entry

| Entry | RVA | Tool consensus | Type | Notes |
|---|---|---|---|---|
| `rva_apocalypticx_unit_cap_gc` | 0x28DF6F | 5-tool VERIFIED (binary_ninja, cheat_engine, ghidra, ida_pro, test_harness) | AOB pin (mid-function) | "Unit cap calculation (GC)" — Apocalypticx CE community reference. Mid-function in `sub_14028DBE0`. Likely a load-from-table or a per-frame query site. |

**Containing function**: `sub_14028DBE0 @ 0x14028DBE0` (size 0xC1E = 3102 bytes; prototype `char __fastcall(int, int, int, int, int, void *Src)`). Likely a galactic-conquest mode tick / build-validate handler given the position and 6-arg signature.

---

## Existing bridge surface

`swfoc_lua_bridge/lua_bridge.cpp` line 3357 — `Lua_SetUnitCapOverride(slot, cap)`:

```cpp
static int Lua_SetUnitCapOverride(lua_State* L) {
    EnsureEconomyLock();
    int slot = static_cast<int>(fn_tonumber(L, 1));
    int cap  = static_cast<int>(fn_tonumber(L, 2));
    if (slot < 0) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitCapOverride: slot must be >= 0");
        return 1;
    }
    if (cap < -1) {
        fn_pushstring(L, "ERR: SWFOC_SetUnitCapOverride: cap must be >= -1 (-1 = unlimited)");
        return 1;
    }
    EnterCriticalSection(&g_economyLock);
    g_pendingUnitCapOverride[slot] = cap;
    LeaveCriticalSection(&g_economyLock);
    Log("[Bridge] SetUnitCapOverride(slot=%d, cap=%d) -- Phase 1 pending\n", slot, cap);
    fn_pushstring(L, "OK: unit-cap override recorded (Phase 2 hook pending)");
    return 1;
}
```

**Wire format**: `(slot, cap)` — slot is 0-indexed PlayerWrapper slot; cap is unit count (-1 = unlimited / removes override).

**Catalog status (iter 132 → iter 221 audit)**: Phase2HookPending with note "BLOCKED-NO-RVA". Iter 248 finding: **the BLOCKED-NO-RVA was wrong** — the Apocalypticx entry is reader-side, but a detour-based override doesn't need a setter RVA to begin with. **Iter 249 will catalog-flip.**

---

## Design decision matrix

| Strategy | Pros | Cons | Verdict |
|---|---|---|---|
| **Option A — MinHook detour at canonical reader (CHOSEN)** | Reversible (clear override → true value); covers all read sites in one hook; pattern matches iter-96/225 exactly; per-slot map already exists in bridge. | Need to find THE canonical reader (might be 1 hot path in `sub_14028DBE0` vs 5 cold paths). Iter 249 will do the xref walk. | **CHOSEN** — proven pattern, mature, reversible. |
| **Option B — Per-faction/per-slot table direct write** | No detour needed; pure memory write. | Need to find table base + verify all readers go through it (might miss UI-cached copies). Higher risk of partial-effect (cap UI lags vs build validator). | Rejected — riskier without exhaustive xref audit. |
| **Option C — Engine Lua API wrapping** | Engine-state-aware. | No `Set_Unit_Cap` or `Override_Unit_Cap` Lua API exists in docs/lua-api.md. | Rejected — no engine API surface. |
| **Option D — Defer entirely** | No risk. | Operator can't override caps — gap stays open. Iter-132 audit already flagged this as a candidate. | Rejected — Option A is well-understood and the bridge stub already exists. |

**Justification for Option A**: same risk profile as iter-225 WeaponTick detour (proved successful) + iter-96 Take_Damage_Outer detour (134 callers, 1 detour covered all). The unit-cap reader is queried per-frame in the GC mode HUD + per-build-attempt in the Build_Validate dispatch — a single MinHook detour intercepting before the load instruction is the cleanest replacement.

---

## Iter 249-252 implementation outline

### Iter 249 — RE walk + bridge LIVE wire shipped (~80-120 LoC)

**RE phase (iter 249a)**: walk from the AOB pin at 0x28DF6F backward to find:
- The function entry containing the cap-load instruction.
- The instruction at 0x28DF6F (likely a `mov` from a per-slot cap field in a struct; Apocalypticx CE notes describe this as "the calculation site for current cap").
- Whether the read uses a `[rax+disp]` form (struct member access — ideal for detour) or an immediate-encoded constant (would require code patch).

**Bridge phase (iter 249b)**:
- `swfoc_lua_bridge/rvas.h`: NEW constant `RVA::UnitCapReader = <found in 249a>` (function entry of the cap reader, likely `sub_14028DBE0` if that's the canonical reader, or a smaller helper if iter-249a finds one).
- `swfoc_lua_bridge/lua_bridge.cpp`:
  - `pfn_UnitCapReader` typedef + `g_originalUnitCapReader` trampoline.
  - `HookedUnitCapReader(args...)` detour: read slot from args, look up `g_pendingUnitCapOverride[slot]`, return override if set + non-negative, else fall through to `g_originalUnitCapReader(args...)`.
  - MinHook installer in `InstallHooks()`.
  - `Lua_SetUnitCapOverride` updated: keep the `g_pendingUnitCapOverride` map write but flip log message from "Phase 1 pending" to "LIVE" and update response string.
- `CapabilityStatusCatalog.cs`: SWFOC_SetUnitCapOverride flipped Phase2HookPending → Live with iter-248 RE design doc cross-reference + iter-249 detour-at-reader pattern note + reversibility caveat (-1 cap clears the override).
- Bridge harness 1100/0 GREEN. DLL + replay rebuilt clean.

### Iter 250 — Simulator handler + 4-6 pin tests + reverse-orphan rebalance

- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs`: extend the existing Phase-1 handler (if any) or add a NEW `HandleSetUnitCapOverride` that stores into `FakeGameState.PerFactionUnitCap` (existing field per `FakeGameState.cs:1983`). Mirror the bridge's clamp + slot semantics.
- 4-6 test pin file `Iter250SetUnitCapOverrideSimulatorTests.cs`:
  - Catalog status Live + iter-249 + iter-248 cross-references in rationale.
  - Bridge wire format `SWFOC_SetUnitCapOverride(slot, cap)` round-trip.
  - Per-slot independence (slot 0 / slot 1 / slot 7 store separately).
  - cap=-1 clears override.
  - cap=0 means "no units allowed" (legitimate — denial-of-service mode for testing).
  - Negative slot rejection.
- Reverse-orphan check: SWFOC_SetUnitCapOverride was already wired since iter-132 catalog audit (Phase2HookPending entry); flipping LIVE doesn't change wiring. **+0 reverse-orphan changes expected.**

### Iter 251 — Spawn tab native UX (per-slot cap input + Apply button)

- Spawn tab is the operator-facing surface for per-slot unit caps (operators need this for tournament/sandbox scenarios where they want to enforce unit limits on AI players).
- Add a NEW "Unit cap override" GroupBox to Spawn tab:
  - Slot dropdown (0-7 player slots, populated via existing iter-217 V2FactionRegistry).
  - Cap NumericTextBox (range -1 to 999; -1 = unlimited).
  - "Apply override" button → `SWFOC_SetUnitCapOverride(slot, cap)` via dispatcher.
  - "Clear (set -1)" sibling button for quick reset.
- 4-test pin file:
  - Catalog SWFOC_SetUnitCapOverride status Live.
  - Slot dropdown is populated (faction roster via iter-217).
  - Apply button command exists + is enabled when slot selected.
  - Clear button command exists + commands cap=-1.

### Iter 252 — Live verify + close (multi-iter arc finale)

- Bridge harness 1100/0 + verifier lint 0/0 (no new ledger entries this arc — Apocalypticx existing entry already covers it; no new struct entries needed since the detour reads function args, not struct fields).
- HISTORY.md prepend the 5-iter arc summary (iter 248-252) with the detour-at-reader pattern + Apocalypticx-AOB-as-RE-anchor lesson.
- STATUS.md master-loop SWFOC_SetUnitCapOverride row updated to **CLOSED iter 249 (bridge LIVE) + offline VERIFIED iter 252, [LIVE-PENDING]**.
- Iter 253 = operator changelog (mirrors iter 229/235/241/247 precedents).

---

## Risks + open questions

1. **The canonical reader might not be a discrete function**: iter-249a RE walk could reveal the cap is read inline via `[rax+0x???]` from many call sites without a centralized helper. **Mitigation**: if no single chokepoint exists, fall back to Option B (per-faction table direct write) — find the table base via `sub_14028DBE0` decompile + write to the slot offsets. Iter 249 design doc would need to be re-revised at that point.

2. **Per-slot vs per-faction-type slot**: the Apocalypticx note says "(GC)" — galactic-conquest mode. There may be a separate tactical-mode unit cap reader. **Mitigation**: scope the iter-249 wire to galactic mode initially; document tactical-mode as a future arc if operators request.

3. **Multiplayer/save-game implications**: cap overrides written to in-memory state may not persist through save/load, and may desync in multiplayer. **Mitigation**: catalog rationale should explicitly call out "single-player offline only" + "does not survive save/load" to set operator expectations.

4. **Engine consistency on cap reduction**: if operator sets cap=2 while player already has 5 units, does the engine cull? Likely no — the cap typically gates BUILD validation, not existing units. **Mitigation**: design doc + catalog rationale clarify "prevents new builds; doesn't cull existing units." Future arc could add a "cull-to-cap" composite if operator requests.

5. **Mid-function AOB vs function-entry detour**: 0x28DF6F is mid-function. MinHook needs a function-entry to detour. **Mitigation**: walk backward from the AOB pin in iter-249a to find the smallest enclosing function (or the dispatcher that calls into the cap-reading basic block) and detour there. Worst case, write a code-patch (5-byte JMP at 0x28DF6F to a stub) but that's higher-risk than a clean MinHook entry detour.

6. **Phase2 → LIVE catalog flip cascading test obligations** (iter-237 / iter-243 lesson): iter 249 must re-check `Iter221Phase2PendingReAuditTests.Phase2PendingEntryCount_Is25` — the count drops to 24 when SetUnitCapOverride flips Live. Add this to the close-out checklist.

---

## Iter 248 close-out

- This document is the iter 248 deliverable.
- No bridge / dispatcher / VM / XAML / test changes. Pure RE + design doc.
- 109 → 109 buttons UNCHANGED. 111 → 111 native UX UNCHANGED.
- Verifier ledger lint untouched (no new entries this iter — the Apocalypticx entry is pre-existing).
- Iter 249 task creation queued at end of iter 248 close-out.

**Pattern lesson reinforced**: **AOB community-ledger entries are reader-side anchors that point at calculation sites, not setter functions.** When a community CE table marks an AOB as "X calculation," the operator-facing override path is detour-based (Option A — hook the reader, return override when set), not direct-write (Option B). This has cost-of-implementation ≈ iter-225 WeaponTick (one detour, one trampoline, per-slot map lookup) but covers all UI + validator read sites in one place.

**5th back-to-back A1.x arc this session.** Iter 248-252 will be the 5th 5-iter cycle — proving the cadence is the right unit, not the technique. Each arc has a different RE entry point (function start / consume site / engine helper / pre-pinned offset / community AOB), but the 5-iter shape stays invariant.
