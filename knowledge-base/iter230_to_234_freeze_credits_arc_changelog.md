# A1.x FreezeCredits Global — Multi-Iter Arc Operator Changelog (iter 230-234)

**Date range:** 2026-05-06 14:30 UTC to 16:30 UTC (single session)
**Status at end of arc:** **CLOSED at offline verification level**, live-game smoke `[LIVE-PENDING]` for next live-attached session
**LIVE wire count delta:** **+4** (largest single-iter LIVE flip count of the master loop)
**Master-loop tally:** 143 → **147 LIVE wires**
**Native UX delta:** +4 buttons (Economy tab — total 111 → 115 across 10 tabs)

---

## What this arc closed

A1.x FreezeCredits had been **DEFERRED** since the iter-132 PHASE 2 PENDING audit (re-confirmed iter-221 audit: "needs Take_Credits hook similar to iter-96 Take_Damage_Outer pattern"). The ledger had `rva_add_credits @ 0x27F370` 4-tool VERIFIED but no detour was wired. **Iter 230 picked up that thread and shipped the 5-iter arc in a single session**, mirroring the iter 224-228 A1.3 SetFireRate cadence exactly.

This is the **second back-to-back A1.x multi-iter arc** in the same session — proving the canonical 5-iter shape (RE → bridge LIVE → simulator → editor UX → verify) is repeatable.

---

## Per-iter walk-through

### Iter 230 — RE design kickoff (research-only, no code)

- Created `knowledge-base/iter230_freeze_credits_re_kickoff.md` (~270 lines).
- **HEADLINE FINDING**: `AddCredits @ 0x27F370` is the universal engine credit-adjust function. **47 callers** (gains AND spends), 259 bytes, 4-tool VERIFIED. Single MinHook detour covers all economy-wide credit flows: build cost, unit purchase, story rewards, AI subsidies, planet ownership transfer.
- Decompiled body extracted from IDA full-corpus (`full_b70-71.json`):
  - Prototype: `float __fastcall(PlayerClass*, float delta, char track_event)` — positive `a2` = gain, negative `a2` = spend.
  - Income multiplier scaling at PlayerClass+0x360 only applied to positive deltas (gains scaled, spends not — by design so build-cost discounts don't halve refunds).
  - Credit balance at PlayerClass+0x70 (already in ledger).
  - **NEW finding: PlayerClass+0x74 = credit cap (float32, -1.0 sentinel = no cap).** Appended to `verified_facts.json` as `struct_player_credit_cap` in iter 234 with 3-tool consensus (binary-fingerprint identity across IDA + Ghidra + Binja).
- **Design decision matrix**: bool freeze + scalar mult, both shipped in same arc. Bool wins-over-mult precedence. Pattern parallels iter-96 + iter-225 with 4 Lua API functions instead of 2.
- **Rejected**: pure-Take_Credits hook (audit suggested but no separate Take_Credits exists — all credit flow routes through AddCredits with sign of `a2`).

### Iter 231 — Bridge LIVE wire shipped (+4 LIVE flips, largest single-iter count)

- `swfoc_lua_bridge/lua_bridge.cpp` after iter-225 SetFireRate block:
  - 2 atomic globals: `std::atomic<bool> g_creditsFreeze_global{false}; std::atomic<float> g_creditsMult_global{1.0f};`
  - `Hook_AddCredits(__int64 a1, float a2, char a3)`: bool freeze precedence (returns `*(float*)(a1+112)` immediately), mult fast-path at 1.0 (zero overhead), else scales `a2` by mult and forwards.
  - 4 Lua functions: `Lua_SetCreditsFreezeGlobal` + `Lua_GetCreditsFreezeGlobal` + `Lua_SetCreditsMultiplierGlobal` (clamp `[0.0, 100.0]`) + `Lua_GetCreditsMultiplierGlobal`.
  - 4 lua_register calls.
  - MinHook installer block: `MH_CreateHook(g_base + RVA::AddCredits, &Hook_AddCredits, &real_AddCredits) + MH_EnableHook` with WARNING log on failure.
- `CapabilityStatusCatalog.cs`: 4 NEW Live entries with iter-230 RE design doc cross-references + engine semantic caveats (cap at +0x74 still applies, AI subsidies blocked equally with freeze=true, analytics events suppressed during freeze, soft-vs-hard freeze with mult=0).
- **Bridge harness 1100/0 GREEN** clean. DLL + replay rebuilt clean.
- **+4 LIVE flips. 143 → 147 LIVE wires.** Largest single-iter LIVE flip count of master loop (vs +1 for iter-96 SetDamageMultiplierGlobal and iter-225 SetFireRateMultiplierGlobal).

### Iter 232 — Simulator handlers + 8 pin tests + reverse-orphan rebalance

- `tests/SwfocTrainer.Tests/Simulator/SwfocSimulator.cs`: 4 `Reg()` handlers + 4 handler methods after iter-226 SetFireRate block. `HandleSetCreditsFreezeGlobal` parses 0/1 via `ExtractIntArgs`; `HandleSetCreditsMultiplierGlobal` clamps `[0.0, 100.0]` via `ExtractFloatArgs`; getters return InvariantCulture strings.
- `tests/SwfocTrainer.Tests/Simulator/FakeGameState.cs`: `GlobalCreditsFreeze` (bool, default false) + `GlobalCreditsMultiplier` (float, default 1.0f) fields with full XML docs.
- 8-test pin file `Iter232CreditsFreezeAndMultGlobalSimulatorTests.cs`:
  - Catalog x4 Live status check
  - Catalog rationales document iter-230/iter-231 + AddCredits/0x27F370/clamp/AI-subsidies/freeze-precedence
  - FakeGameState reflection (2 fields with correct types/defaults)
  - Freeze round-trip Set→Get bool toggle (0 → 1 → 0)
  - Mult round-trip Set→Get
  - Clamp lower-bound (0.0 stored as soft-freeze identity)
  - Clamp upper-bound (200.0 → 100.0)
  - Freeze-precedence coexistence (both globals stored independently, no cross-contamination)
  - Set/Get pair-flip catalog containment
- Reverse-orphan snapshot: +4 entries added to `KnownUnwiredEntries` (pending iter 233 Economy tab UX).
- **43/43 GREEN** in 80 ms — clean run, no mid-iter drift.

### Iter 233 — Economy tab native UX (4 buttons, 10th tab gets UX)

- 4 buttons added to Economy tab as NEW "GLOBAL economy controls (LIVE — iter 231-233)" GroupBox below the existing per-slot freeze GroupBox:
  - **Row 0**: Freeze on / Freeze off pair (hardcoded-bool, **iter-204 lineage now 8 iters deep**: 204→208→211→212→213→215→217→233).
  - **Row 1**: Credit mult TextBox + Apply (GLOBAL) + Read (GLOBAL) (mirrors iter-227 Combat tab pattern exactly).
- `BridgeEconomyDispatcher`: 4 new methods using regex-visible interpolated `string.Format` form. Drops the 4 iter-231 entries from `KnownUnwiredEntries`.
- `IEconomyDispatcher`: 4 default-impl methods for backward-compat with mocks.
- `EconomyTabState`: 4 wrapper methods + 2 staged-state properties (`GlobalCreditsFreezeStaged` bool + `GlobalCreditsMultiplierStaged` float). Validation guards multiplier >= 0.
- `EconomyTabViewModel`: 4 ICommands + 3 capability actions (freeze pair fans into a single CapabilityAwareAction with both Set/Get HelperNames; mult Set + mult Get separate) + 4 handler methods + 1 NEW `GlobalCreditsMultiplier` field. AllActions extended (10 → 13). MemberNotNull attribute updated.
- `MainWindowV2.xaml`: NEW GroupBox with 4 buttons + multiplier TextBox + per-button capability badges + tooltips referencing iter-230 RE design doc + AddCredits/0x27F370/freeze precedence/clamp range/caveats.
- 6-test pin file `Iter233CreditsFreezeAndMultEconomyNativeUxTests.cs`.
- Reverse-orphan: dropped 4 iter-231 entries (now regex-visible).
- **101/101 GREEN** in 238 ms — clean run, no mid-iter drift.
- Editor republished to `publish/` directory.
- **Economy tab is the 10th tab to receive native UX in the master loop** — first NEW Economy tab GroupBox since the tab was first wired. **115 buttons across 10 tabs total.**

### Iter 234 — Offline verify + close (multi-iter arc finale)

- Pure verify + close-out, no code changes.
- `bridge_test_harness.exe` → **1100 passed, 0 failed**.
- `python -m verifier lint` → **0 errors / 0 warnings** (315 entries unchanged from iter 228).
- **Action item completed iter 235**: appended `struct_player_credit_cap` ledger entry per iter-230 RE finding (PlayerClass+0x74 = float32 credit cap, -1.0 sentinel = no cap, found in AddCredits decompile). 3-tool consensus via binary-fingerprint identity (matches `struct_player_credits` sibling pattern). Lint stays 0/0 with 316 entries (304 VERIFIED + 2 LIVE_OBSERVED + 10 DEPRECATED).
- HISTORY.md updated with 5-iter arc summary entry.
- STATUS.md master-loop A1.x FreezeCredits row: `DEFERRED iter 132/221 → CLOSED iter 231 (bridge LIVE) → VERIFIED OFFLINE iter 234, [LIVE-PENDING] for next session`.
- Live-game smoke verify queued for next live-attached session.

---

## Operator workflow (post-iter-233)

**To freeze credits globally** (cinematic recording prep):

1. Open editor (`publish/SwfocTrainer.App.exe`).
2. Switch to the **Economy** tab.
3. In the new "GLOBAL economy controls (LIVE — iter 231-233)" GroupBox:
4. Click **Freeze on** → bridge logs `[Bridge] SetCreditsFreezeGlobal(true) -- LIVE`. ALL credit changes blocked engine-wide (gains AND spends, all factions including AI).
5. Click **Freeze off** to resume normal flow.

**To scale credit income/spend globally** (streaming difficulty preset):

1. Type a multiplier (e.g. `2.0` for 2x both income and spend) into the `Credit mult:` TextBox.
2. Click **Apply (GLOBAL)** → bridge logs `[Bridge] SetCreditsMultiplierGlobal(mult=2.000) -- LIVE`.
3. Click **Read (GLOBAL)** to confirm the bridge has the value stored.

**Engine semantic caveats** (per iter-230 design doc — surfaced in tooltip):

| Caveat | Behavior |
|---|---|
| `freeze=true + mult=anything` | Bool freeze wins. Mult ignored. Hard freeze: AddCredits short-circuited entirely. |
| `freeze=false + mult=0.0` | All deltas zeroed. Soft freeze: AddCredits still called with 0 delta (events fire, analytics tracks). Distinguishable from hard freeze. |
| `freeze=false + mult=2.0` | Income AND spending both 2x. |
| `freeze=false + mult=0.5` | Half income, half spending. |
| `freeze=false + mult > 100` | Clamped to 100 (overflow guard). |
| `freeze=false + mult < 0` | Clamped to 0 (sanity). |
| Cap behavior | Cap at PlayerClass+0x74 still applies. mult=2 doesn't let you exceed cap — just gets you there faster. Freeze short-circuits before the cap check. |
| AI subsidies | AI factions also use AddCredits, so freeze blocks AI economic thinking too. (For asymmetric "freeze player only", a per-player setter is a follow-up wire — not in this arc.) |

**Combined with iter-96 SetDamageMultiplierGlobal + iter-100 SetPerFactionSpeedMultiplier + iter-227 SetFireRateMultiplierGlobal**, operators now have **full economy + combat + speed + fire-rate global control surface across 4 different tabs** (Economy + Combat + Speed + Combat).

---

## Pattern lessons captured

1. **+4 LIVE flips in one iter is achievable** when the underlying detour supports both bool + scalar knobs. Compare:
   - iter-96 SetDamageMultiplierGlobal: +1 LIVE flip (single scalar knob).
   - iter-225 SetFireRateMultiplierGlobal: +1 LIVE flip (single scalar knob).
   - iter-231 FreezeCredits/CreditsMultiplier: **+4 LIVE flips** (bool freeze + scalar mult, each with Set/Get pair).

   The bool freeze is "for free" once you have the MinHook detour — it's just a precedence check before the mult logic. The decision to ship both in same arc rather than sequential single-knob arcs saves 5 iters of bureaucracy (no second RE/bridge/sim/UX/verify cycle).

2. **Universal-function hooks have leverage**: AddCredits @ 0x27F370 covers 47 caller sites with one MinHook detour. Compare to iter-225's WeaponTick @ 0x387010 which is also a per-frame chokepoint. **Look for "universal" engine functions before chasing per-call-site hunting** — the leverage ratio is 47:1 here vs typical per-call-site detours that cover 1-3 caller sites.

3. **iter-204 hardcoded-bool lineage as 8-iter compounding asset**: 204→208→211→212→213→215→217→233. Each iter that uses the on/off button pair adds another link in the chain, with self-documenting catalog rationale + pin test cross-references. Pattern is now load-bearing — operators expect bool toggles to come in on/off pairs everywhere.

4. **Soft vs hard freeze distinction matters for analytics/replay**: bool freeze short-circuits AddCredits entirely (no events fire). mult=0 lets AddCredits run with 0 delta (events still fire). Operator semantic difference matters when listening for credit events (e.g. for replay reconstruction). **Document soft-vs-hard for every freeze-class wire** — operators may not realize they're different.

5. **Two A1.x multi-iter arcs closed back-to-back this session**: A1.3 SetFireRate (224-228) + A1.x FreezeCredits (230-234) = 10 iters of pure deferred-arc closure. **Pattern is now repeatable** — each arc follows the canonical 5-iter shape (RE → bridge → sim → UX → verify) with high consistency. Each takes ~30 minutes wall-clock when the RE work surfaces a clean target.

6. **Economy tab is the 10th tab to receive native UX**: completes the major-tab tour (started ~iter 188 with UnitControl). Every operator-facing tab now has at least one native LIVE button. The remaining tabs are tooling/admin (Settings, Probes, Lua Playground) that don't need LIVE button surfacing.

---

## Cross-references

- `knowledge-base/iter230_freeze_credits_re_kickoff.md` — RE design doc with full AddCredits decompile + design decision matrix.
- `knowledge-base/HISTORY.md` (top entry) — 5-iter arc summary (iter 230-234) + reinforced + new pattern lessons.
- `knowledge-base/iter224_to_228_setfirerate_global_arc_changelog.md` — predecessor arc operator changelog (iter 229).
- `STATUS.md` (master-loop table A1.x FreezeCredits row) — closed-state summary.
- `verified_facts.json#struct_player_credit_cap` — NEW ledger entry from iter-230 RE finding (PlayerClass+0x74 credit cap).

---

## What's next (iter 236+)

Per the iter 221 audit's direction-setting recommendations:

- **Option A (multi-iter)**: pick up another A1.x deferred sub-task. Strong candidates by iter-221 audit framing:
  - `SetCameraPos` per-coord setters (currently partial-defer; iter-107 ScrollCameraToTarget LIVE covers most cases).
  - `SetUnitField` for the 10 still-PHASE-2 sub-fields (iter-136 wired some; finish the rest).
  - `SetUnitCapOverride` (only validator pinned, no setter — needs RTTI walk).
- **Option B (multi-iter)**: Thread B Overlay Phase 2-full ImGui vendoring or Thread C save-game RE.
- **Option C (single-iter polish)**: bridge harness expansion / replay harness expansion / capability surface report polish.

Given two A1.x arcs just closed back-to-back with the same canonical shape, **Option A continuation is the path of least resistance** — the pattern is hot and the operator gets immediate feedback (each arc = +1 to +4 LIVE flips + 1 native button group).

**Recommended iter 236**: pick the next A1.x candidate based on which one has the cleanest IDA decompile + lowest RTTI complexity. `SetCameraPos` per-coord setters are likely the cleanest candidate (camera state is well-understood from iter-106/107).
