# Iter 281 — HudSnapshot multiplier extension: damage_mult LIVE via iter-96 getter; firerate_mult stays honest-defer pending iter-282 bridge getter

**Date:** 2026-05-08 06:00 UTC
**Iter:** 281 (Tier-2 honest-defer resolution; post-arc cleanup)
**Predecessor:** iter 280 operator changelog supplement (Thread B Phase 2-full arc complete + post-arc docs).
**Successor:** iter 282 (queued: SWFOC_GetFireRateMultiplierGlobal bridge getter to resolve the remaining honest-defer).

## Headline

**iter-279 Tier 2 honest-defer for damage_mult RESOLVED** via existing
iter-96 SWFOC_GetDamageMultiplierGlobal LIVE getter. HudSnapshot model
extended 7 → 9 fields (damage_mult + firerate_mult; both float with
-1.0f sentinel). Worker BuildSnapshot adds probe step #5 for damage;
step #6 left as comment placeholder for iter-282 fire-rate getter.
Render path color-codes damage_mult (amber when scaled, gray when
neutral). Fire-rate row stays TextDisabled honest-defer pointing to
iter-282+ bridge getter addition.

| Metric | Value |
|---|---|
| `hud_state.h` LoC | 75 → **89** (+14 lines: 2 fields + comment block) |
| `hud_state.cpp` LoC | 229 → **252** (+23 lines: probe step #5 + step #6 honest-defer comment) |
| `overlay.cpp` LoC | 597 → **615** (+18 lines: damage_mult color-coded render branch) |
| **Total LoC delta** | **+55** across 3 files |
| DLL size | 1,036,288 → **1,037,824 B** (+1,536 B; +0.15%) |
| HudSnapshot fields | **7 → 9** (added damage_mult + firerate_mult) |
| Tier 2 honest-defers | 2 → **1** (damage_mult resolved; firerate_mult queued for iter-282) |
| Build | 4/4 GREEN; 0 errors / 0 warnings |
| Bridge harness | unchanged (no bridge changes; consumes existing iter-96 getter) |
| Verifier ledger lint | unchanged at 318 entries |

## What shipped

### `swfoc_overlay/hud_state.h` — 2 fields appended to HudSnapshot

```cpp
// 2026-05-08 (iter 281): Tier 2 multiplier values; resolves
// iter-279 honest-defer for the in-game HUD's damage / fire-rate
// rows. Worker probes via iter-96 SWFOC_GetDamageMultiplierGlobal
// (LIVE getter pair to iter-96 SWFOC_SetDamageMultiplierGlobal
// Take_Damage_Outer detour). Sentinel -1.0f means "not yet probed
// or probe failed"; render side shows TextDisabled placeholder
// in that case. firerate_mult stays -1.0f because iter-225
// SetFireRateMultiplierGlobal does NOT have a paired getter yet
// — see iter-281 close-out + iter-282 follow-up plan. Append-only
// field additions preserve the iter-275 design's binary layout
// stability across phases.
float damage_mult = -1.0f;
float firerate_mult = -1.0f;
```

**Append-only**: fields placed BEFORE `generated_tick` (preserving its
position as last field for any cross-section size assertions) but
AFTER `last_error` (the previous-last user-data field). Existing 7
fields unchanged in position and type — binary-compatible with
iter-279 worker thread state.

### `swfoc_overlay/hud_state.cpp` — probe step #5 added; step #6 honest-defer comment

```cpp
// 5) 2026-05-08 (iter 281): Tier 2 damage-multiplier probe via
//    iter-96 SWFOC_GetDamageMultiplierGlobal (LIVE getter pair).
//    Resolves iter-279 honest-defer for the HUD's damage row.
//    Bridge returns a stringified float (e.g. "2.0" or "1.0"); on
//    parse failure the field stays at -1.0f sentinel and the
//    render side falls back to TextDisabled placeholder.
if (BridgeProbe("return SWFOC_GetDamageMultiplierGlobal()", resp))
{
    try { snap.damage_mult = std::stof(resp); }
    catch (...) { /* leave at -1.0f sentinel */ }
}

// 6) Fire-rate multiplier — HONEST DEFER continues at iter-281.
//    iter-225 shipped SWFOC_SetFireRateMultiplierGlobal (WeaponTick
//    detour at 0x387010 scaling g_fireRateMult_global) but did NOT
//    pair-ship a SWFOC_GetFireRateMultiplierGlobal getter. iter-282
//    queued to add the getter on the bridge side; this iter leaves
//    snap.firerate_mult at -1.0f sentinel so the render side keeps
//    the iter-279 TextDisabled placeholder pointing to iter-282+.
```

**Pattern**: probe step ordering is sequential (1-6); step #6 is left
as a comment placeholder pointing to the iter-282 reader. Mirrors
iter-249 honest-defer cross-reference pattern: ship the slot, name
the iter that fills it.

### `swfoc_overlay/overlay.cpp` — RenderImGuiPanel damage_mult render branch

Replaces iter-279's `ImGui::TextDisabled("Damage mult: bridge-query
pending (iter 280+)")` placeholder with a 3-state branch:

```cpp
if (snap.damage_mult >= 0.0f)
{
    const bool scaled = (snap.damage_mult < 0.99f
        || snap.damage_mult > 1.01f);
    if (scaled)
    {
        ImGui::PushStyleColor(ImGuiCol_Text,
            ImVec4(1.0f, 0.706f, 0.0f, 1.0f));  // amber
        ImGui::Text("Damage mult: %.2fx", snap.damage_mult);
        ImGui::PopStyleColor();
    }
    else
    {
        ImGui::TextDisabled("Damage mult: %.2fx (neutral)",
            snap.damage_mult);
    }
}
else
{
    ImGui::TextDisabled("Damage mult: probe pending");
}
// Fire-rate stays honest-defer (iter-282 needs bridge getter).
ImGui::TextDisabled("Fire-rate mult: bridge getter pending (iter 282+)");
```

**Color coding**: amber `(1.0, 0.706, 0.0)` matches editor's
`WarningForeground` brand for "scalar is active" semantic. Neutral
(0.99 ≤ damage_mult ≤ 1.01 within float epsilon) renders gray to
de-emphasize the row when nothing's modifying damage.

**Footer iter tag bumped**: "F1 toggles | Phase 2-full @ iter 281
(Tier 2 partial)" — `(Tier 2 partial)` reflects the 1-of-2 honest-defer
resolution.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-280 |
|---|---|---|
| Overlay build [1/4] MinHook | clean | unchanged |
| Overlay build [2/4] overlay sources | **clean** (Tier 2 multiplier render + worker probe) | NEW |
| Overlay build [3/4] ImGui | clean | unchanged |
| Overlay build [4/4] link | clean | unchanged |
| Overlay DLL size | **1,037,824 B** | +1,536 B (+0.15%) |
| Bridge harness | n/a (no bridge changes) | inherits iter-274 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-274 0/0 at 318 entries |

## Pattern lessons

### Lesson #1 — Post-arc HudSnapshot extension is safe

iter-275 design committed to "no HudSnapshot model changes during the
Phase 2-full arc" — and iter 275-279 honored that constraint, with
iter-279 explicitly deferring to a post-arc iter for the multiplier
extension. iter-281 (post-arc) took advantage of the lifted constraint
and shipped the extension cleanly: 9 LoC of struct fields + 8 LoC of
worker probe + 18 LoC of render branch = 35 LoC total functional
code. No build issues, no protocol changes, no compatibility shims.

**Pattern**: model-stability constraints during an arc protect the
arc from cascading rework but DON'T forbid post-arc extensions.
Treating "during" and "after" as different windows is the right
discipline.

### Lesson #2 — Pair-getter availability determines honest-defer surface area

iter-96 SetDamageMultiplierGlobal shipped with a paired getter
(SWFOC_GetDamageMultiplierGlobal); iter-281 could resolve the damage
honest-defer without bridge changes. iter-225 SetFireRateMultiplierGlobal
shipped WITHOUT a paired getter; iter-281 cannot resolve the fire-rate
honest-defer without first shipping a getter pair (queued for
iter-282).

**Pattern**: when shipping a write-side LIVE wire (Set*), always
pair-ship a read-side getter (Get*) even if no immediate consumer
exists. Future consumers (overlay HUDs, simulator round-trip tests,
operator diagnostics) all benefit from the symmetric pair. iter-167
NEW unit-getter helper established the read-side helper infrastructure;
write-only wires are a missed opportunity.

### Lesson #3 — Color-coded scalars use float epsilon, not exact compare

The damage_mult render branch checks `< 0.99f || > 1.01f` rather than
`!= 1.0f` because the bridge returns a stringified float that's been
through `std::stof` parsing — exact equality to 1.0 is fragile to
formatting. The 0.99-1.01 window covers "neutral or near-neutral"
robustly while remaining narrow enough to detect operator-set
multipliers (which are typically 0.5x, 2.0x, 4.0x — far outside the
epsilon).

**Pattern**: float scalar equality checks at UI boundaries should use
small epsilons (1-2% range) rather than exact comparison. Robust to
parser quirks + display rounding while preserving operator intent
detection.

## What's next (iter 282+)

1. **Iter 282 (RECOMMENDED)** — **SWFOC_GetFireRateMultiplierGlobal
   bridge getter pair**. Scope: ~20 LoC bridge (mirror iter-96
   SWFOC_GetDamageMultiplierGlobal pattern reading
   `g_fireRateMult_global`) + harness round-trip test + simulator
   handler (extends iter-225 simulator coverage). Resolves the 2nd
   Tier 2 honest-defer; iter-283 then extends overlay's worker probe
   step #6 + render branch (mirror iter-281 damage branch).

2. **Iter 282 (alternative)** — **Phase 4 Camera projection matrix
   RE kickoff**. Multi-iter; would unlock per-unit Inspector overlay
   + drag-to-spawn workflows.

3. **Iter 282 (alternative)** — **A1.x pivot back** if user surfaces
   new ledger entries.

4. **NOT recommended for iter 282**: NEW arc-class kickoff (Thread C
   Save-game RE / Thread D Multi-repo CI / Thread E SonarQube). All
   are LOW operator value vs the just-completed Thread B HUD.

## Iter 281 close-out summary

- This document is the iter 281 deliverable.
- **Code changes**: +55 LoC across 3 files (hud_state.h +14;
  hud_state.cpp +23; overlay.cpp +18). All append-only or
  comment-extension; no removed code.
- All gates GREEN: build 4/4 clean; DLL **1,037,824 B** (+1,536 B
  vs iter-280); bridge harness + ledger lint inherit iter-274
  unchanged.
- **HudSnapshot fields 7 → 9** (damage_mult + firerate_mult
  appended).
- **Tier 2 honest-defers 2 → 1** (damage_mult RESOLVED via iter-96
  getter; firerate_mult queued for iter-282).
- **11th consecutive NON-A1.x iter** per iter-269 lesson #2 (iter-271
  + iter-272 + iter-273 + iter-274 + iter-275 + iter-276 + iter-277
  + iter-278 + iter-279 + iter-280 + **iter-281**).
- **3 NEW pattern lessons**: post-arc HudSnapshot extension is safe;
  pair-getter availability determines honest-defer surface area;
  color-coded scalars use float epsilon not exact compare.
- 109 → 109 buttons UNCHANGED. 106 → 106 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- **First post-arc Tier-2 honest-defer resolution iter** — extends
  the iter-249 honest-defer pattern's life-cycle from "arc closes
  with deferral" to "arc closes with deferral + post-arc resolution
  iter unblocks it."
- **Session-cumulative this conversation (iter 159-281)**: +99 LIVE
  wire/sub-field flips + 10 helpers + 34 operator-facing improvements
  + 13 docs iters + 7 audit/audit-followup iters + 1 memory
  codification iter + 3 preset-menu refresh iters + 9 RE kickoff
  iters + **10 RE-implementation iters** (was 9; iter 281 NEW
  HudSnapshot extension) + 5 simulator iters + 3 native UX iters + 2
  staging-UI verification iters + **16 close-out iters** (was 15;
  iter 281 NEW) + 4 ledger updates + 9 stale-count drift fixes + 1
  wire-format-canonical alignment + 3 honest-defer arc closures +
  **1 honest-defer post-arc resolution** (NEW; iter 281 damage_mult
  via iter-96 getter) + 3 audit-iter rationale drift catches + 1
  cross-reference pin test + 3 README capstone updates + 3
  reverse-orphan audit clean passes + 1 memory rule codification + 6
  surface report regens + 2 multi-iter arc finale capstones + 1
  mid-iter dual-drift catch + 1 NEW arc-class implementation start +
  1 NEW arc-class plumbing wire + 1 phase-transition implementation
  + 1 multi-iter arc-class completion across **123 iters**.
