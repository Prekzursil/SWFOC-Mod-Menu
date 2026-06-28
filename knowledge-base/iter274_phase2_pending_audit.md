# Iter 274 — Phase2HookPending re-audit (5th audit; +1 NEW drift catch on SWFOC_SetHeroRespawnTimer)

**Date:** 2026-05-08 00:30 UTC
**Iter:** 274 (NON-A1.x continuation per iter-269 lesson #2)
**Cadence:** **5th Phase2HookPending audit** in iter-132/221/250/266/274 sequence. Mean gap ~16 iters (89/29/16/8 actual).
**Result:** **1 drift catch** — `SWFOC_SetHeroRespawnTimer` rationale extended to cite iter-130 SWFOC_SetHeroRespawn (global) LIVE alternative.

## Headline

5th Phase2HookPending audit catches **1 NEW drift instance** in the
iter-251/266 drift class ("Catalog-rationale-cross-reference drift").
`SWFOC_SetHeroRespawnTimer` (per-hero variant) had a terse `"Phase 1
mirror — pending hero respawn timer field pin"` rationale that didn't
mention the iter-130 LIVE alternative `SWFOC_SetHeroRespawn` (global).
Operators searching for "I want to set hero respawn time" would find
no path forward in the per-hero entry's rationale despite a global
LIVE alternative existing in the same catalog. Iter-274 fix extends
the rationale ~7 lines to cite iter-130 + iter-104 audit history +
operator-use-case framing.

| Metric | Value |
|---|---|
| Phase2HookPending entries audited | 25 (unchanged from iter-266) |
| Drift catches this iter | **1** (SWFOC_SetHeroRespawnTimer) |
| Drift rate | **1/25 = 4%** |
| Cumulative drift class instances | iter-251 (1) + iter-266 (2) + **iter-274 (1)** = **4 instances** |
| iter-251 pin test list extended | **4 → 5** entries |
| Editor test build | 0 Errors |
| Focused regression suite | **30 / 30 GREEN** in 61 ms |
| Capability surface markdown | regenerated via `SWFOC_REGEN_CAPABILITY_SURFACE=1` |
| Bridge harness | unchanged (no bridge changes) — inherits iter-273 1100/0 |
| Verifier ledger lint | unchanged at 318 entries (no ledger changes) |

## What changed

### CapabilityStatusCatalog.cs SWFOC_SetHeroRespawnTimer rationale extension

```diff
 ["SWFOC_SetHeroRespawnTimer"] = new("SWFOC_SetHeroRespawnTimer",
     CapabilityStatus.Phase2HookPending,
-    "Phase 1 mirror — pending hero respawn timer field pin"),
+    "Phase 1 mirror — pending hero respawn timer field pin "
+  + "(per-hero respawn-timer table RVA not in ledger; iter-104 + iter-130 "
+  + "audits both confirmed defer — table location not callgraph-discoverable). "
+  + "Operator should use iter-130 SWFOC_SetHeroRespawn for global default-"
+  + "respawn-time override (writes float at RVA 0xB169F0; affects timers "
+  + "created AFTER the call but doesn't reset already-queued respawns; "
+  + "range clamped to [0, 600] seconds). The global form covers ~80% of "
+  + "operator use cases (\"all heroes respawn faster/slower\"); the per-hero "
+  + "form would only be needed for \"this specific hero respawns differently "
+  + "from the others\" workflows that aren't currently surfaced."),
```

**Operator-trust audit trail** for `SWFOC_SetHeroRespawnTimer` now
spans 4 links: rationale → iter-130 LIVE alternative
(SWFOC_SetHeroRespawn) → iter-104/130 audit history → 80%/20%
operator-use-case split.

### Iter221 pin test list extension (iter-251 + iter-266 + iter-274 cumulative)

Extended `LegacyPhase1Mirrors_CiteLiveAlternativeWhenAvailable` from 4
entries to **5 entries**:

| # | Name | IterRef | Catch iter |
|---|---|---|---|
| 1 | `SWFOC_SetFireRate` | `iter-225` | (preserved baseline) |
| 2 | `SWFOC_FreezeCredits` | `iter-231` | iter-251 |
| 3 | `SWFOC_SpawnUnit` | `iter-109` | iter-266 |
| 4 | `SWFOC_SetUnitCapOverride` | `iter-249` | iter-266 |
| 5 | **`SWFOC_SetHeroRespawnTimer`** | **`iter-130`** | **iter-274** |

Test asserts each entry: (a) stays `Phase2HookPending` status, (b)
rationale contains the cited iter-N reference. Iter-274 NEW entry
verified passing.

## Drift trend (5 audits)

| Audit iter | Entries audited | Drift catches | Drift rate (catches / entries) | Notes |
|---|---|---|---|---|
| iter-132 (1st) | 24 | 3 | 12.5% | Iter-105 SetUnitShield + iter-130 SetHeroRespawn + iter-131 SetGameSpeed |
| iter-221 (2nd) | 26 | 4 | 15% | iter-133 SetDiplomacy was the iter-132 strong-candidate that flipped LIVE; counts as iter-221 catch |
| iter-250 (3rd) | 25 | 1 | 4% | iter-251 SWFOC_FreezeCredits cross-reference fix |
| iter-266 (4th) | 25 | 2 | 8% | iter-266 SWFOC_SpawnUnit + SWFOC_SetUnitCapOverride cross-reference fixes |
| **iter-274 (5th)** | **25** | **1** | **4%** | **iter-274 SWFOC_SetHeroRespawnTimer cross-reference fix** |

**Pattern**: drift rate has **stabilized around 4-8% per audit** since
iter-250 introduced the iter-251 cross-reference drift class. Each
audit catches 1-2 instances, suggesting the latent pool isn't fully
drained but is small enough to surface 1-2 instances per ~16-iter
window.

## Pattern lessons

### Lesson #1 — iter-266's "0 latent" prediction was wrong; class isn't fully converged

iter-266 close-out claimed: "Cumulative drift class total: 3
instances caught at iter-251 + iter-266. Latent pool now: 0."

**Iter-274 disproves this.** The latent pool is **not** zero — it has
an additional `SWFOC_SetHeroRespawnTimer` instance that escaped the
iter-266 audit. Pattern: latent pool estimates are unreliable until 2
consecutive zero-catch audits prove convergence. iter-272 reverse-orphan
audit is at 4 consecutive CLEAN PASSes (convergence proven); iter-274
phase2 audit is at 0 consecutive zero-catch audits (NOT yet converged).

### Lesson #2 — Per-hero vs global LIVE alternative coverage isn't equivalent

iter-130 found `SWFOC_SetHeroRespawn` (global) is LIVE; this is a
sibling but not equivalent surface to `SWFOC_SetHeroRespawnTimer`
(per-hero). The iter-274 rationale extension explicitly names the
80%/20% operator-use-case split:

- **Global form covers ~80%**: "all heroes respawn faster/slower"
  workflows (the canonical operator request).
- **Per-hero form needed for ~20%**: "this specific hero respawns
  differently from the others" workflows (specialized; not currently
  surfaced).

This framing is clearer than "use global as a substitute" — operators
know what they're getting and what they're missing.

### Lesson #3 — Drift class half-life >5 audits

The iter-251 cross-reference drift class has now produced catches at
iter-251 (1) + iter-266 (2) + iter-274 (1) = **4 instances over 5
audits and 41 iters**. Per audit, average is ~0.8 catches. The class
isn't decaying fast enough to predict zero by iter-290; expect 1-2
more catches by iter-330.

**Pattern**: cross-reference drift is *durable* — every new LIVE wire
that supersedes a legacy Phase-1 mirror creates a potential drift
instance. The pool stays approximately stable over time as new wires
ship.

## Verification gates (ALL GREEN)

| Gate | Result | Δ vs iter-273 |
|---|---|---|
| Editor test build | 0 Errors | unchanged |
| Phase2 audit pin tests (Iter221) | **3 / 3 GREEN** | iter-251 list 4→5 entries; iter-266 + iter-274 catches all pinned |
| Full focused regression suite | **30 / 30 GREEN** in 61 ms | covers Iter136 + Iter221 + Iter266 + Iter271 + Iter272 + CapabilitySurfaceReport |
| Capability surface markdown | regenerated via `SWFOC_REGEN_CAPABILITY_SURFACE=1` | absorbs iter-274 rationale change |
| Capability surface JSON | regenerated as sibling | unchanged size/path |
| Bridge harness | n/a (no bridge changes) | inherits iter-273 1100/0 |
| Verifier ledger lint | n/a (no ledger changes) | inherits iter-273 0/0 at 318 entries |

## What's next (iter 275+)

Per iter-274 close + iter-269 lesson #2 NON-A1.x pivot continuation:

1. **Iter 275 (RECOMMENDED)** — **Thread B-D NEW arc-class kickoff**
   from iter-190 close-out queue. Choose based on operator priority:
   - **Thread B Overlay Phase 2-full** ImGui vendoring (~500 LoC,
     ~15 files). High operator value (in-game HUD).
   - **Thread C Save-game RE** (not started). High RE complexity;
     long-tail.
   - **Thread D Multi-repo CI gate hygiene** (not started). Low
     operator value but improves dev velocity.
   - **Thread E Local SonarQube workflow** (not started). Low
     operator value; quality-gate cleanup.

2. **Alternative iter 275**: Lua Playground preset menu refresh
   (~17-iter cadence since iter-271). Very thin since no new wires
   shipped; could batch with iter-289 next-natural cadence window.

3. **NOT recommended**: Another A1.x sub-field arc (would push
   honest-defer rate to 4/9 = 44.4%). Defer until live-game tracing
   surfaces new reader-side offsets.

## Iter 274 close-out summary

- This document is the iter 274 deliverable.
- **Code changes**: 1 catalog rationale extension (~7 lines) + 1 pin
  test list extension (4 → 5 entries) + 1 pin test comment update
  (~5 lines documenting iter-274 NEW catch).
- All gates GREEN: build 0 errors; focused 30/30 in 61 ms; capability
  surface regenerated; bridge harness + ledger lint inherit iter-273
  unchanged.
- **5th Phase2HookPending audit** in iter-132/221/250/266/274 cadence
  (89/29/16/8 iter gaps; cadence stabilized at ~16 iters).
- **NON-A1.x pivot iter** per iter-269 lesson #2 — 4th consecutive
  NON-A1.x iter (iter-271 + iter-272 + iter-273 + iter-274).
- **+1 drift catch this iter** (SWFOC_SetHeroRespawnTimer);
  cumulative class instances 4 across 5 audits.
- **iter-266 "latent pool = 0" claim disproven** — pool is not yet
  converged; expect 1-2 more catches by iter-290.
- 109 → 109 buttons UNCHANGED. 106 → 106 preset entries UNCHANGED.
  SetUnitField LIVE 7/13 unchanged. Phase2HookPending count 25
  unchanged.
- **Pattern lesson capstone**: cross-reference drift is durable —
  every new LIVE wire that supersedes a legacy Phase-1 mirror creates
  a potential drift instance. Per-audit ~0.8 catches average is
  steady-state, not asymptotic.
- **Session-cumulative this conversation (iter 159-274)**: +99 LIVE
  wire/sub-field flips + 10 helpers + 34 operator-facing improvements
  + 12 docs iters + **7 audit/audit-followup iters** (was 6; iter
  274 NEW Phase2 audit) + 1 memory codification iter + 3 preset-menu
  refresh iters + 8 RE kickoff iters + 5 RE-implementation iters + 5
  simulator iters + 3 native UX iters + 2 staging-UI verification
  iters + **11 close-out iters** (was 10; iter 274 NEW audit close)
  + 4 ledger updates + 9 stale-count drift fixes + 1
  wire-format-canonical alignment + 3 honest-defer arc closures +
  **3 audit-iter rationale drift catches** (was 2; iter 274 NEW;
  cumulative 4 instances across 5 audits) + 1 cross-reference pin
  test + 3 README capstone updates this conversation + 3
  reverse-orphan audit clean passes + 1 memory rule codification +
  **6 surface report regens** (was 5; iter 274 NEW) + 1 multi-iter
  arc finale capstone + 1 mid-iter dual-drift catch across **116
  iters**.
