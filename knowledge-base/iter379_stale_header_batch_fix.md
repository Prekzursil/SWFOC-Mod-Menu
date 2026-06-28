# Iter 379 — 4-candidate stale-header batch fix; Tier 1/2 codification trigger reached at 7 instances

**Date:** 2026-05-07
**Arc class:** UI/UX polish (iter 3/N of arc; concrete operator-visible work)
**Predecessor:** iter-378 (12-candidate stale-header audit catalog + 2 fixes)
**Successor (queued):** iter-380 (Tier 1/2 codification of `feedback_stale_groupbox_header_drift.md`)

## What changed (4 XAML edits + 1 close-out doc; ~28 LoC source)

All 4 LIKELY STALE candidates from iter-378 audit catalog confirmed STALE upon section-content verification. Fixed in batch.

### Fix 1: WorldState tab line 1461
**Section content** (verified): 4 sub-batches across iter-201 + iter-202 + iter-204 + iter-208 = **12 buttons** wrapping iter 159/160/166/180/181 wires.
**Before**: `Header="Story & Audio (engine Lua, LIVE — iter 159)"`
**After**: `Header="Cinematic & Audio Controls (12 LIVE wires)"`
**Why**: header was triply stale — (a) "iter 159" only named the FIRST sub-batch; (b) "Story & Audio" undercounted scope (now also has cinematic input lock + SFX VO); (c) renamed to better reflect actual operator workflow (filming setups).

### Fix 2: Inspector tab line 2254
**Section content** (verified): 4 sub-batches across iter-191 + iter-197 + iter-198 + iter-214 = **18 buttons** wrapping iter 168/169/171/172/173/174 read-side wires.
**Before**: `Header="Selected Unit Lua Read-side (iter 191)"`
**After**: `Header="Selected Unit Lua Read-side (~18 read-side wires)"`
**Why**: "Read-side" label still accurate (all 18 are read-only); only iter range was stale.

### Fix 3: Spawning tab line 3187
**Section content** (verified): iter-119 (1 button) + iter-195 (3 buttons) = **4 buttons** wrapping iter 109/185 wires.
**Before**: `Header="Spawn unit via Lua (iter 119 LIVE)"`
**After**: `Header="Spawn unit via Lua (4 LIVE wires)"`

### Fix 4: Spawning Discovery tab line 3263
**Section content** (verified): iter-203 (4 buttons: Find_Object_Type/FindPlanet/Find_First_Object/Find_Nearest) + iter-206 (1 button: Find_All_Objects_Of_Type) = **5 buttons** wrapping iter 177/179/186 wires.
**Before**: `Header="Discovery helpers (engine Lua, LIVE — iter 177 + 186)"`
**After**: `Header="Discovery helpers (5 LIVE wires)"`
**Why**: iter-179 wire surfaced via iter-206 was unnamed in the header. Header now reflects full first/all/nearest discovery trio count.

## Verification gates ALL GREEN

| Gate | Result |
|------|--------|
| `dotnet publish` | Build succeeded (exit code 0) |
| Binary size | **157.89 MB** (165,759,659 bytes; +0.01 MB from iter-378's 165,747,403 bytes) |
| Binary LastWriteTime | **2026-05-07 11:17:00** (advanced from iter-378's 11:14:00, ~3 min later) |
| Filtered test verify | **Passed! Failed: 0, Passed: 22, Skipped: 0, Total: 22, Duration: 313 ms** |
| Bridge harness | inherits 1100/0 |
| Verifier ledger lint | inherits 0/0 at 318 entries |

3 consecutive iters (iter-377/378/379) of monotonically advancing binary timestamps confirms concrete-source-impact pattern is sustained — fully reverses the iter 365-376 audit-organization cluster's 0-source-impact period.

## Pattern recurrence count — TIER 1/2 CODIFICATION TRIGGER REACHED

`feedback_stale_groupbox_header_drift.md` is now at **7/6 trigger** per Tier 1/2 production-pattern threshold:

| # | Iter | Tab | Header before | Header after |
|---|------|-----|---------------|--------------|
| 1 | iter-377 | UnitControl | `(iter 117-118 LIVE)` | `(~24 LIVE wires; see per-button badges)` |
| 2 | iter-378 | Combat | `(iter 193 — iter 154 LIVE wires)` | `(5 LIVE wires)` |
| 3 | iter-378 | Player State | `Read-side (iter 169 + iter 199)` | `Actions (~13 LIVE wires; mixed read/write)` |
| 4 | iter-379 | World State | `Story & Audio (engine Lua, LIVE — iter 159)` | `Cinematic & Audio Controls (12 LIVE wires)` |
| 5 | iter-379 | Inspector | `Selected Unit Lua Read-side (iter 191)` | `Selected Unit Lua Read-side (~18 read-side wires)` |
| 6 | iter-379 | Spawning | `Spawn unit via Lua (iter 119 LIVE)` | `Spawn unit via Lua (4 LIVE wires)` |
| 7 | iter-379 | Spawning Discovery | `Discovery helpers (engine Lua, LIVE — iter 177 + 186)` | `Discovery helpers (5 LIVE wires)` |

**7 instances >= 6-instance Tier 1/2 threshold** — codification trigger fires next iter (iter-380). Pattern has fully recurred + been validated empirically across 5 distinct V2 tabs (UnitControl + Combat + PlayerState + WorldState + Inspector + Spawning ×2).

## Why batch 4 fixes in 1 iter instead of 1 per iter

Each candidate's verification cycle (read 50-100 lines + count buttons + categorize) takes ~5 min. The XAML edit itself is ~30 sec. Batching all 4 in one iter:
- ~25 min total cycle vs ~80 min spread across 4 iters
- All fixes verified against same audit catalog (iter-378 source-of-truth)
- Single publish + filtered-test cycle (vs 4 cycles)
- Cleaner cumulative recurrence count for codification trigger

## What's NOT done in iter-379

- **Codification of `feedback_stale_groupbox_header_drift.md`**: deferred to iter-380 (codification iter)
- **Lua Playground line 4248 preset menu refresh** ("Iter 100-300 LIVE wires"): noted in iter-378 audit catalog as needing multi-iter preset-menu refresh; deferred
- **Combat line 2716 + Galactic 3552 + Camera & Debug 4067/4094/4151** headers (iter-378 catalog "ACCURATE" / "MOSTLY ACCURATE"): no change needed

## Codification queue update (post-iter-379)

| Class | Pre-iter-355 | Post-iter-379 |
|---|---|---|
| Class A (high-recurrence) | 4 | 4 (unchanged) |
| Class B (medium-recurrence) | 5 | 5 (unchanged) |
| Class C (retire/promote) | 2 | 2 (unchanged) |
| Class C low-priority watch | 1 | 1 (unchanged) |
| iter-355→378 candidates | 0 | +17 (6 codified iter-359/363/368/371/373/374 + 11 at 1/3 trigger; iter-377 stale-header now at 7/6 trigger ready to codify) |

**Codification queue NOW: 29 candidates total** (unchanged from iter-377/378; pattern recurrence achieved threshold instead of adding new candidates).

## Net iter-379 outcome

| Aspect | Value |
|--------|-------|
| Source code shipped | 4 XAML edits (~28 LoC source: 4 × 1-line text + 4 × 6-line provenance comments) |
| Doc shipped | 1 close-out doc (~140 lines) |
| Stale-header fixes | 4 (cumulative iter 377-379: 7 across 6 V2 tabs) |
| Pattern recurrence | **7/6 — TIER 1/2 CODIFICATION TRIGGER REACHED** |
| Cycle time | ~25 min (4 reads + 4 edits + publish + test verify + close-out) |

49th post-iter-323 arc iter (6 LIVE + 9 codification + 5 republish + 7 XAML + 19 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 5 test-verify + 2 P2HP audit + 1 reverse-orphan audit + 2 pre-compound + 1 pre-compound-verify + 1 meta-reflection + **3 UX-polish iters**); 110th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter options (iter-380)

In priority order:

1. **CODIFY `feedback_stale_groupbox_header_drift.md` at 7/6 trigger** — would become 18th codified rule; first Tier 1/2 codification of iter-377 NEW pattern observation. Per iter-302/334/345 production-pattern precedent, ship the rule with 11-section template + 7-instance evidence base + "How to apply" prospective uses. **Recommended** — closes the 3-iter UX polish arc cleanly with a codified rule artifact for future operators.
2. **Pivot to UX Pattern 2** (demote iter-N references from user-facing tooltips per iter-377 inventory) — multi-iter sub-arc; ~30 tooltips in UnitControl alone.
3. **Pivot to UX Pattern 3** (de-duplicate amber warning banners) — Combat + Galactic; ~80 LoC.
4. **Operator changelog supplement** for iter 348-379 (~32-iter window since iter-347 supplement; canonical post-arc cadence).
5. **Live SWFOC verify** of iter-343 chain — requires operator session.

iter-380 codification continues the natural codification cadence (next codified rule expected per iter-374's "1 rule per ~16 iters" trend) while pivoting back from concrete UX work — but this codification is GROUNDED in 7 empirical instances spread across iter-377/378/379 concrete-work arc, NOT cluster-saturation meta-codification. This is fundamentally different from the iter-359-374 audit-organization cluster pattern (which was Tier 4 meta-rules accelerating to ~1 per ~3 iters).
