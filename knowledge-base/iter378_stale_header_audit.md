# Iter 378 — Stale-header audit + 2 concrete fixes shipped (Combat line 2720 + PlayerState line 884; extends iter-377 Pattern 1)

**Date:** 2026-05-07
**Arc class:** UI/UX polish (iter 2/N of arc; concrete operator-visible work)
**Predecessor:** iter-377 (UX polish arc kickoff; 3-tab survey + UnitControl line 1090 fix)
**Successor (queued):** iter-379 (TBD per "Next iter" below)

## What changed (2 XAML edits + 1 audit doc; ~14 LoC source)

### Fix 1: Combat tab line 2720 (was known-stale per iter-377 survey)
**Before**:
```xaml
<GroupBox Header="Per-unit combat actions (iter 193 — iter 154 LIVE wires)">
```
**After**:
```xaml
<!-- 2026-05-07 (iter 378): UX polish — header was stale ... -->
<GroupBox Header="Per-unit combat actions (5 LIVE wires)">
```
**Why**: section actually mixes iter-154 (4 wires: Heal/Take_Damage/Set_Damage_Modifier/Set_Rate_Of_Fire_Modifier) + iter-162 (Suspend_AI). The "iter 193" prefix was the surfacing-iter codename — internal noise to operators.

### Fix 2: PlayerState tab line 884 (NEW — discovered during audit)
**Before**:
```xaml
<GroupBox Grid.Row="4" Header="Selected Player Read-side (iter 169 + iter 199)">
```
**After**:
```xaml
<!-- 2026-05-07 (iter 378): UX polish — header was DOUBLY stale ... -->
<GroupBox Grid.Row="4" Header="Selected Player Actions (~13 LIVE wires; mixed read/write)">
```
**Why**: header was doubly stale —
1. **"Read-side" label was wrong**: section now contains write-side wires too (Lock_Tech, Make_Ally, Make_Enemy, Disable_Orbital_Bombardment, GLOBAL diplomacy from iter-209/210/217)
2. **iter range was incomplete**: section spans iter 161/164/169/170/179/182/199/210/217 (~13 distinct LIVE wires across 5 sub-batches), not just iter 169 + 199

## Stale-header audit catalog (all 12 GroupBoxes with iter-N references)

Single grep `Header=".*iter \d+` against MainWindowV2.xaml — 12 hits:

| # | Line | Tab | Current header | Status | iter-378 action |
|---|------|-----|---------------|--------|-----------------|
| 1 | 884 | Player State | `Selected Player Read-side (iter 169 + iter 199)` | **STALE** (drift to write-side; iter range incomplete) | **FIXED** → "Selected Player Actions (~13 LIVE wires; mixed read/write)" |
| 2 | 1456 | World State | `Story & Audio (engine Lua, LIVE — iter 159)` | LIKELY STALE (iter-201/202/204 added wires?) | DEFERRED — verify in iter-379 |
| 3 | 2136 | Economy | `GLOBAL economy controls (LIVE — iter 231-233)` | ACCURATE (3-iter arc; section unchanged since) | KEEP |
| 4 | 2249 | Inspector | `Selected Unit Lua Read-side (iter 191)` | LIKELY STALE (iter-197/198/214 added) | DEFERRED — verify in iter-379 |
| 5 | 2720 | Combat | `Per-unit combat actions (iter 193 — iter 154 LIVE wires)` | **STALE** (iter-219 added Suspend_AI) | **FIXED** → "Per-unit combat actions (5 LIVE wires)" |
| 6 | 3178 | Spawning | `Spawn unit via Lua (iter 119 LIVE)` | LIKELY STALE (iter-195/206 added spawn variants?) | DEFERRED — verify in iter-379 |
| 7 | 3254 | Spawning | `Discovery helpers (engine Lua, LIVE — iter 177 + 186)` | LIKELY STALE (iter-203/206 added?) | DEFERRED — verify in iter-379 |
| 8 | 3552 | Galactic | `Fog of War (engine-Lua, LIVE — iter 180/184)` | MOSTLY ACCURATE (iter-200 surfaced; same wires) | KEEP |
| 9 | 4067 | Camera & Debug | `Scroll camera to target (iter 107 LIVE)` | ACCURATE (single-iter section) | KEEP |
| 10 | 4094 | Camera & Debug | `Camera primitive arc (iter 143-145 LIVE)` | ACCURATE (iter 143-145 wires only) | KEEP |
| 11 | 4151 | Camera & Debug | `Camera primitive arc — extras (iter 162/165 LIVE)` | ACCURATE (iter 192 added native UX, didn't add wires) | KEEP |
| 12 | 4248 | Lua Playground | `Iter 100-300 LIVE wires (+2 honest-defer notes)` | NEEDS REFRESH (iter 301-373 wires shipped; preset menu updated iter-335 covered iter 188-300) | DEFERRED — preset-menu refresh is multi-iter task |

**Audit summary**: 12 instances total — 2 STALE (fixed iter-378), 4 LIKELY STALE (iter-379 verify queue), 5 ACCURATE (keep), 1 needs preset-menu refresh.

## Pattern recurrence count

iter-377 NEW pattern observation `feedback_stale_groupbox_header_drift.md` is now at **3/6 trigger** per Tier 1/2 production-pattern threshold:

| # | Iter | Tab | Header before | Header after |
|---|------|-----|---------------|--------------|
| 1 | iter-377 | UnitControl | `(iter 117-118 LIVE)` | `(~24 LIVE wires; see per-button badges)` |
| 2 | iter-378 | Combat | `(iter 193 — iter 154 LIVE wires)` | `(5 LIVE wires)` |
| 3 | iter-378 | Player State | `Read-side (iter 169 + iter 199)` | `Actions (~13 LIVE wires; mixed read/write)` |

Codification deferred — Tier 1/2 thresholds need 6+ instances. Pattern is reinforced by 3 instances; 3-4 more expected via iter-379+ verification of the 4 LIKELY STALE candidates.

## Why iter-378 stops at 2 fixes (not all 4 likely-stale)

Each "LIKELY STALE" candidate requires reading the actual section content (50-100 lines of XAML) to verify which iter wires it contains and pick a replacement header. 4 candidates × ~5 min each = 20 min — exceeds the iter-378 cycle envelope. Better: queue iter-379 to handle all 4 as a single batch.

## Verification gates

| Gate | Result |
|------|--------|
| `dotnet publish` | TBD (post this doc write) |
| Filtered test verify | TBD |
| Bridge harness | inherits 1100/0 |
| Verifier ledger lint | inherits 0/0 at 318 entries |

## Net iter-378 outcome

| Aspect | Value |
|--------|-------|
| Source code shipped | 2 XAML edits (~14 LoC source: 2 × 1-line text + 2 × 6-line provenance comment) |
| Doc shipped | 1 audit doc (~120 lines) + this close-out |
| Stale-header fixes | 2 (Combat + PlayerState; cumulative 3 across iter-377+iter-378) |
| Audit candidates catalogued | 12 GroupBox headers across 24 tabs |
| iter-379 verification queue | 4 LIKELY STALE candidates (lines 1456, 2249, 3178, 3254) |
| Pattern recurrence | 3/6 toward Tier 1/2 codification trigger |

48th post-iter-323 arc iter; 109th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter options (iter-379)

In priority order:

1. **Verify-and-fix the 4 LIKELY STALE candidates** (WorldState 1456 / Inspector 2249 / Spawning 3178 / Spawning Discovery 3254) as a batch. Reading + fixing 4 sections × ~5 min each = ~20 min. Likely produces 3-4 more stale-header instances → triggers Tier 1/2 codification (5-7/6 threshold). **Recommended** — closes the audit cleanly.
2. **Pivot to UX Pattern 2** (demote iter-N references from user-facing tooltips per iter-377 inventory) — ~30 tooltips in UnitControl alone.
3. **Pivot to UX Pattern 3** (de-duplicate amber warning banners) — Combat + Galactic.
4. **Operator changelog supplement** for iter 348-378 (~30-iter window since iter-347 supplement; canonical post-arc cadence).
5. **Live SWFOC verify** of iter-343 chain — requires operator session.
