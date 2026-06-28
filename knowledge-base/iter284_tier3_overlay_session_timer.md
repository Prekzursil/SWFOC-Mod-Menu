# iter-284 — Tier 3 overlay content (session timer LIVE; kill/death/units-alive honest-deferred)

**Date:** 2026-05-08
**Arc class:** Thread B Overlay Phase 2-full → post-arc Tier 3 partial
**Predecessors:** iter-281 (Tier 2 damage_mult), iter-282 (Tier 2 firerate_mult), iter-283 (codify bidirectional claim-drift rule)
**Successor (queued):** iter-285 (bridge wires for kill/death/units-alive — being scoped by parallel agent right now)

## What changed (3 files, ~50 net LoC)

- **`swfoc_overlay/hud_state.h`** — appended 4 fields below the iter-281/iter-282 multiplier block:
  - `uint64_t session_elapsed_seconds = 0;` (LIVE — local clock)
  - `int local_kills = -1;` (HONEST DEFER — needs iter-285 bridge wire)
  - `int local_deaths = -1;` (HONEST DEFER)
  - `int total_units_in_play = -1;` (HONEST DEFER)
  HudSnapshot field count: 9 → **13**.

- **`swfoc_overlay/hud_state.cpp`** — added:
  - File-scope `std::atomic<uint64_t> g_session_start_tick{0}` for the session clock seed.
  - Worker probe step #7: CAS-seed the session-start tick at first successful bridge probe + compute `(now - seed) / 1000` for `session_elapsed_seconds`.
  - Worker probe step #8: honest-defer comment block citing iter-284 grep evidence (3 missing wires) + iter-285 followup pointer.

- **`swfoc_overlay/overlay.cpp`** — Tier 3 row group below iter-282's Tier 2 row group:
  - Conditional `Session: MM:SS` text (TextDisabled `--:--` when sentinel).
  - `renderCounterRow(label, count)` lambda — mirrors iter-282's `renderMultRow`. Handles `>= 0 → "Kills (you): N"` vs `< 0 → "Kills (you): awaits iter-285+"` (TextDisabled).
  - 3 `renderCounterRow` invocations: kills / deaths / units-in-play.
  - Footer iter-tag bumped: `"Phase 2-full @ iter 282 (Tier 2 complete)"` → `"Phase 2-full @ iter 284 (Tier 3 partial — session live)"`.

## The iter-283-codified rule applied

Per `feedback_infra_claim_drift_bidirectional.md` (codified iter-283), iter-284 GREPPED the bridge BEFORE writing addition code:

```bash
grep -nE 'SWFOC_GetPlayerKills|SWFOC_GetPlayerDeaths|SWFOC_GetTotalUnitsAlive' lua_bridge.cpp
# → ZERO matches (only SWFOC_KillUnit, which is write-side)
```

Result: confirmed all 3 candidate wires are GENUINELY MISSING (not pre-existing). iter-284 correctly scoped to "ship what doesn't need bridge work + honest-defer the rest" rather than ship a stale-claim duplicate.

This is the FIRST iter to apply the iter-283 rule prospectively (iter-282 caught the pattern retroactively). Pattern is now load-bearing in the loop.

## Session-timer mechanism (no bridge needed)

The session timer uses purely local state:

1. `g_session_start_tick` is `std::atomic<uint64_t>{0}` at DLL load.
2. When the worker's first bridge probe succeeds (step #1, `bridge_reachable = true`), it calls `compare_exchange_strong(0, GetTickCount64())` to atomically seed the start tick.
3. Subsequent worker ticks compute `seconds = (GetTickCount64() - seed) / 1000`.
4. CAS-once-only semantics mean a stale 0 doesn't get re-seeded when the bridge briefly disconnects + reconnects — the session clock is monotonic across reconnects.

**Why not engine mission time?** SWFOC has no `SWFOC_GetMissionElapsedMs` bridge wire (iter-285 candidate to add). For now, the local clock approximates "how long has the operator been playing this session" which is itself useful for stream operators wanting a session timer in their HUD without OBS plugins.

## Build verification

```
[1/4] Compiling MinHook ✓
[2/4] Compiling overlay sources ✓
[3/4] Compiling ImGui v1.91.5 ✓
[4/4] Linking swfoc_overlay.dll ✓

DLL: 1,039,360 bytes (+512 B vs iter-282)
```

`grep -iE 'error|warning|undefined'` on full build log: **zero matches**.

## DLL-size growth across iter-275 → iter-284

| Iter | DLL size (bytes) | Δ | Cumulative since iter-275 baseline |
|---|---|---|---|
| 275 (Phase 2-lite final) | 274,432 | — | 0 |
| 276 (ImGui vendored) | 1,038,848 | +764,416 | +764,416 |
| 277 (ImGui Init) | 1,037,824 | -1,024 | +763,392 |
| 278 (Tier 1 HUD) | 1,036,800 | -1,024 | +762,368 |
| 279 (Tier 2 partial) | 1,036,288 | -512 | +761,856 |
| 281 (Tier 2 damage) | 1,037,824 | +1,536 | +763,392 |
| 282 (Tier 2 firerate) | 1,038,848 | +1,024 | +764,416 |
| **284 (Tier 3 partial)** | **1,039,360** | **+512** | **+764,928** |

Per-iter Tier 2/3 deltas average +682 B. Phase 3 (interactive widgets) should stay well under +5 KiB if budget-bounded.

## Tasks queued for iter-285

3 parallel agents are running in the background to scope:

1. **Thread C savegame RE research** — RGMH chunk format + crash points + corruption-fix strategies. Will inform iter-285+ if loop pivots to Thread C, OR enrich the long-term backlog.
2. **Overlay drag-drop UX research** — ImGui drag-drop API + bridge wires usable from overlay + alternative interactive features brainstorm + phased delivery plan (Phase 3-6).
3. **Iter-285 bridge wires implementation plan** — kill counter (Take_Damage detour extension vs Object_Death_Event detour), units-alive counter (poll vs detour vs internal-counter probe), local-player-slot resolution.

When agents report back, iter-285 task description will be refined from agent #3's spec. Iter-286+ will be informed by agents #1 and #2.

## NEW pattern lesson — opportunistic-honest-defer

iter-284 demonstrates a NEW pattern variant of the iter-249 honest-defer life-cycle:

| Pattern | Origin | Shape |
|---|---|---|
| Arc-end honest-defer | iter-249 | Multi-iter arc closes deferred, future arc unblocks |
| Telescoped 2-iter cycle | iter-267/268 | RE kickoff iter + close-out iter, both within arc |
| Post-arc resolution | iter-281 | Honest-defer from prior arc closed by single follow-up iter |
| Mid-iter pre-existing-infra catch | iter-282 | Grep mid-iter discovers infra already exists, scope shrinks |
| **Opportunistic honest-defer** | **iter-284** | **Iter ships what's possible without external dep + cleanly defers rest with grep-evidence + concrete next-iter scope** |

Iter-284's variant is the most operator-friendly: the user gets SOMETHING immediately (session timer renders LIVE) while the harder work (3 bridge wires) is queued with full evidence. Compare to a "wait until everything is ready" approach which would have shipped 0 visible progress this iter.

Codification candidate: `feedback_opportunistic_honest_defer.md` — but only if the pattern recurs in 2-3 more iters. Memory-rule codification cost is real; don't codify singletons.

## Verification checklist

- [x] HudSnapshot field count 9 → 13 (4 new fields, append-only).
- [x] Worker probe step #7 (session timer) implemented + CAS-seeded.
- [x] Worker probe step #8 (kill/death/units-alive) honest-defer comment with iter-285 pointer.
- [x] Render Tier 3 row group with 1 LIVE + 3 placeholders.
- [x] Footer iter-tag bumped to "Tier 3 partial — session live".
- [x] Build clean: 0 errors / 0 warnings / 0 undefined symbols (full log greps).
- [x] DLL +512 B (within budget).
- [x] iter-283 rule applied: grep-first verified missing wires before honest-defer.
- [ ] State docs synced (.remember/now.md, .remember/ralph_loop_state.md, STATUS.md).
- [ ] Task #534 completed; iter-285 queued (pending agent #3 spec).
- [ ] 3 parallel agents reporting back to refine iter-285+ scope.
