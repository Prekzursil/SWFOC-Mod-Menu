# Iter 271 — Lua Playground preset menu refresh covering iter 267-268 max_speed + iter 269-270 attack_power honest-defer arcs

**Date:** 2026-05-07 23:00 UTC
**Iter:** 271 (NON-A1.x pivot per iter-269 lesson #2 — ledger-state asymptote at 3/8 = 37.5% honest-defer rate)
**Cadence:** 5th preset-menu refresh iter (mirrors iter-183/223/252/264). Last ran iter-264 with 102→104 entries.
**Result:** 104 → **106** entries; +2 informational honest-defer presets surfacing alternative-set pattern at the operator-trust audit-trail layer.

## Headline

**Operator-trust audit-trail closure for iter 267-270 honest-defer arcs.** When
the catalog rationale reasons that a sub-field is honest-deferred and points
operators to LIVE alternatives, the Lua Playground preset menu must surface
that reasoning at the dropdown level too. Otherwise an operator searching for
"max_speed" or "attack_power" finds nothing in the dropdown and assumes those
features are simply absent — which is wrong, because their LIVE alternatives
ARE present elsewhere in the same dropdown but unindexed by sub-field name.

| Metric | Value |
|---|---|
| Bridge changes | **0** |
| Catalog changes | **0** |
| LuaPlaygroundTabViewModel.cs presets | 104 → **106** (+2 informational honest-defer entries) |
| MainWindowV2.xaml GroupBox header | `Iter 100-258 LIVE wires` → `Iter 100-270 LIVE wires (+2 honest-defer notes)` |
| NEW pin test file | `Iter271PresetMenuRefreshTests.cs` (5 source-grep pin tests) |
| Existing iter-252 pin test | 1 line updated to track new header (per iter-260 lesson #3) |
| Editor test build | **0 errors / 18 pre-existing warnings** unchanged from iter-270 |
| Focused preset-menu regression suite | **59 / 59 passed in 63 ms** across iter-271 + iter-264 + iter-252 + iter-223 + iter-183 + iter-136 + iter-221 + iter-266 + CapabilitySurfaceReportIntegration |
| Capability surface | not regenerated (catalog unchanged) |
| Bridge harness | unchanged (no bridge changes) — assumed 1100/0 |
| Verifier ledger lint | unchanged (no ledger changes) |

## What shipped

### LuaPlaygroundTabViewModel.cs +2 informational honest-defer presets

#### Entry #105 — `[267-268] max_speed HONEST DEFER → see iter-99 SetUnitSpeed (per-instance) or iter-100 SetPerFactionSpeedMultiplier`

Click-pasted Lua script:
```lua
-- iter 267-268: max_speed has NO TYPE-LEVEL offset (semantic verification per iter-256 memory rule).
-- Use iter-99 SWFOC_SetUnitSpeed for per-instance speed override OR iter-100 SWFOC_SetPerFactionSpeedMultiplier for per-faction.
-- Both call SetSpeedOverride @ 0x3A8C90 directly. See catalog rationale for SWFOC_SetUnitField + UnitStatEditor comment for full audit trail.
-- Example LIVE alternative (per-instance):
return SWFOC_SetUnitSpeed(0x12345678, 2.0)
```

#### Entry #106 — `[269-270] attack_power HONEST DEFER → alternative-set: iter-96 (global) / iter-154 (per-unit) / iter-225 (fire-rate)`

Click-pasted Lua script:
```lua
-- iter 269-270: attack_power has NO central per-unit read site (HardpointFire confirms damage is param-passed, computed from per-weapon XML).
-- Alternative-set pattern (iter-270 NEW, refines iter-251/268 single-alternative) — pick by SCOPE:
--   1. GLOBAL outgoing damage scaling   → iter-96  SWFOC_SetDamageMultiplierGlobal (Take_Damage_Outer detour)
--   2. PER-INSTANCE damage scaling      → iter-154 SWFOC_SetDamageModifierLua    (Set_Damage_Modifier engine API)
--   3. GLOBAL fire-rate scaling         → iter-225 SWFOC_SetFireRateMultiplierGlobal (WeaponTick detour)
-- See catalog rationale for SWFOC_SetUnitField + UnitStatEditor comment + iter270_setunitfield_attack_power_honest_defer.md for full audit trail.
-- Example LIVE alternative (per-instance):
return SWFOC_SetDamageModifierLua('Find_First_Object("Empire_AT_AT")', '2.0')
```

**Both entries are RUNNABLE** (not just comments) — the operator can paste,
strip the comment lines, and execute. The default body is the most common
LIVE alternative (per-instance speed override / per-instance damage modifier).

### MainWindowV2.xaml GroupBox header bump

`Iter 100-258 LIVE wires` → `Iter 100-270 LIVE wires (+2 honest-defer notes)`.
The "(+2 honest-defer notes)" annotation explicitly flags that the dropdown
contains 2 informational entries that are NOT runnable LIVE wires in the
naive sense — they're cross-references to runnable LIVE alternatives.

### NEW pin test file `Iter271PresetMenuRefreshTests.cs`

Five source-grep pin tests (per iter-260 lesson #2 — bypass VM construction;
~1 ms execution; ~10x faster than instantiating LuaPlaygroundTabViewModel):

| Test | Substring assertions |
|---|---|
| `Preset_MaxSpeedHonestDefer_IsPresent_WithIter267To268Tag` | `[267-268] max_speed HONEST DEFER` + `iter-99 SetUnitSpeed` + `iter-100 SetPerFactionSpeedMultiplier` + `SWFOC_SetUnitSpeed(0x12345678, 2.0)` (4) |
| `Preset_AttackPowerHonestDefer_IsPresent_WithIter269To270Tag` | `[269-270] attack_power HONEST DEFER` + `alternative-set` (2) |
| `Preset_AttackPowerHonestDefer_CitesAllThreeAlternativesByScope` | `iter-96  SWFOC_SetDamageMultiplierGlobal` + `iter-154 SWFOC_SetDamageModifierLua` + `iter-225 SWFOC_SetFireRateMultiplierGlobal` (3) |
| `Preset_HonestDeferEntries_ReferenceIter256MemoryRule` | `iter-256 memory rule` (1) |
| `GroupBoxHeader_ReflectsIter270Coverage` | `Iter 100-270 LIVE wires (+2 honest-defer notes)` present + `Iter 100-258 LIVE wires` absent (2) |

**Total**: 12 substring assertions across 5 tests — comparable to iter-264's
5 substring assertions across 5 tests but with 2.4x more cross-reference
coverage reflecting the alternative-set pattern's larger surface.

### Existing iter-252 pin test 1-line update (per iter-260 lesson #3)

Iter-252 test `GroupBoxHeader_ReflectsIter258Coverage` was pinning the old
header `Iter 100-258 LIVE wires`. Updated the assertion target to match
iter-271's new header. Test name kept tagged "Iter 252" so the
header-history audit trail stays live without splitting the test file
across multiple iter-files (per iter-260 lesson #3 — file is named for
the iter that introduced it; assertion target evolves with the source).

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-270 |
|---|---|---|
| Editor test build | **0 errors / 18 pre-existing warnings** | unchanged |
| Iter271 pin tests | **5 / 5 passed in 43 ms** | NEW (file added) |
| Full preset-menu regression suite | **59 / 59 passed in 63 ms** | covers iter-271 + iter-264 + iter-252 + iter-223 + iter-183 + iter-136 + iter-221 + iter-266 + CapabilitySurfaceReportIntegration |
| Stale-count drift (iter-252 header pin) | caught + fixed in same iter | iter-260 lesson #3 applied |
| Capability surface markdown | not regenerated (catalog unchanged) | unchanged |
| Bridge harness | n/a (no bridge changes) | inherits iter-270 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-270 0/0 at 318 entries |

## Pattern lessons

### Lesson #1 — Honest-defer informational presets are operator-trust artifacts

When a sub-field is honest-deferred and the catalog rationale points to LIVE
alternatives, the dropdown must echo that pointer. Otherwise the dropdown is
a "filtered view of reality" that misleads operators into thinking the
sub-field is absent.

The naming pattern `[<iter-range>] <field> HONEST DEFER → <alternatives-by-scope>`
distinguishes informational entries from runnable LIVE wires at first glance
in the ComboBox.

### Lesson #2 — Stale-count drift catches at the same iter that creates them

Iter-252's `Iter 100-258 LIVE wires` pin was about to silently break with
iter-271's header bump. Caught in the same iter (not 8-15 iters later as
in the iter-195 / iter-208 / iter-256 stale-count drift incidents) because
my own iter-271 NEW test included `xaml.Should().NotContain("Iter 100-258 LIVE wires")`.

The defensive `NotContain` clause acts as a self-check: if I miss updating
sibling tests, my own iter-271 test fails AND the sibling test fails, so
both are surfaced together.

### Lesson #3 — `Iter <N>` test file naming with header-history-tracking

iter-260 lesson #3 said: file is named for the iter that introduced it;
assertion target evolves with the source. Iter-252 test stays tagged "Iter
252" but now pins the iter-271 header. This is cleaner than creating a NEW
`Iter271_GroupBoxHeader_PinsIter271Header` test that duplicates the iter-252
assertion shape. The history is preserved in the test's own comment block
("iter-264 update: ... iter-271 update: ..."), and `git blame` recovers
the timeline.

## What's next (iter 272+)

Per iter-269 lesson #2 (ledger-state asymptote signal at 3/8 = 37.5%
honest-defer rate), continue prioritizing NON-A1.x classes. Iter-271 closed
the operator-trust audit-trail loop for iter-267-270 honest-defer arcs;
remaining priorities:

1. **Iter 272 (RECOMMENDED)** — **Reverse-orphan snapshot audit**
   (~22-iter window since iter-263; pure tooling, ~20 min). Verify
   wiring-graph invariant `actuallyUnwired.Count == KnownUnwiredEntries.Count`.
   Mirrors iter-238/255/263 cadence.

2. **Alternative**: README capstone update (~30-iter cadence since iter-265),
   Phase2HookPending re-audit (~16-iter cadence since iter-266), or Thread
   B-D NEW arc-class kickoff.

3. **NOT recommended for iter 272**: Another A1.x sub-field arc (would push
   honest-defer rate to 4/9 = 44.4%). Defer until live-game tracing surfaces
   new reader-side offsets.

## Iter 271 close-out summary

- This document is the iter 271 deliverable.
- **Code changes**: +2 LuaPlaygroundTabViewModel.cs preset entries (~24 lines
  total inc. comment block) + 1 MainWindowV2.xaml GroupBox header bump +
  1 NEW Iter271PresetMenuRefreshTests.cs file (~115 lines, 5 tests) + 1
  Iter252PresetMenuRefreshTests.cs assertion update.
- **No bridge / catalog / ledger changes**.
- All gates GREEN: build 0 errors, focused 59/59 in 63 ms.
- **5th preset-menu refresh iter** this loop (iter-183 first, iter-223 second,
  iter-252 third, iter-264 fourth, iter-271 fifth). Cadence proven at ~17-iter
  intervals when honest-defer arcs ship.
- **NON-A1.x pivot iter** per iter-269 lesson #2.
- **Stale-count drift CAUGHT in same iter** that introduced it (vs 8-15 iter
  delay in past incidents) — defensive `NotContain` clause in NEW pin test
  surfaced sibling iter-252 test simultaneously.
- 109 → 109 buttons UNCHANGED. **104 → 106 preset entries** (+2 honest-defer
  informational). SetUnitField LIVE 7/13 unchanged. Phase2HookPending count
  25 unchanged.
- **Session-cumulative this conversation (iter 159-271)**: +99 LIVE wire/sub-field
  flips + 10 helpers + 34 operator-facing improvements + 11 docs iters + 6
  audit/audit-followup iters + 1 memory codification iter + **3 preset-menu
  refresh iters** (was 2; iter 271 NEW) + 8 RE kickoff iters + 5 RE-implementation
  iters + 5 simulator iters + 3 native UX iters + 2 staging-UI verification
  iters + **8 close-out iters** (was 7; iter 271 NEW) + 4 ledger updates +
  **9 stale-count drift fixes** (was 8; iter 271 NEW catch-in-same-iter) + 1
  wire-format-canonical alignment + 3 honest-defer arc closures + 2 audit-iter
  rationale drift catches + 1 cross-reference pin test + 2 README capstone
  updates + 2 reverse-orphan audit clean passes + 1 memory rule codification
  + 5 surface report regens + 1 multi-iter arc finale capstone + 1 mid-iter
  dual-drift catch across **113 iters**.
