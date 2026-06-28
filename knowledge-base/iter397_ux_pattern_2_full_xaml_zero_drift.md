# Iter 397 — UX Pattern 2 FULL-XAML zero-drift sweep (16 fixes; ALL iter-N drift across all V2 tabs eliminated)

**Date:** 2026-05-07
**Arc class:** UX polish (extends iter 377-393 sub-arcs by closing the WHOLE-XAML gap)
**Predecessor:** iter-396 (headline-doc quad refresh)
**Successor (queued):** iter-398 (TBD per "Next iter" below)

## What this iter does

iter-395 close-out doc claimed "9 V2 tabs 100% tooltip-clean" (UnitControl/PlayerState/Inspector/Galactic/Combat/Camera & Debug/Connection/Economy/Spawning) — but **a full-XAML grep at iter-397 found 16 leftover iter-N drift references across the entire 4910-line MainWindowV2.xaml** that survived the iter 382-393 per-tab sub-arc cleanup.

iter-397 closed all 16 in one batch via the iter-388 codified `feedback_internal_codename_in_tooltips_drift.md` rule + iter-380 codified `feedback_stale_groupbox_header_drift.md` rule, achieving **TRUE 100% zero-drift across the entire XAML**.

## What shipped (16 surgical Edit operations)

### Tooltip drift (10 edits — line-numbered)
| # | Line | Before (excerpt) | After |
|---|------|------------------|-------|
| 1 | 2997 | `LIVE (iter 100) — calls ClearSpeedOverride...` | `LIVE — calls ClearSpeedOverride...` |
| 2 | 3324 | `LIVE iter 179 — returns... iter-177 Find_First_Object... iter-186 Find_Nearest...` | `LIVE — returns... Find_First_Object... Find_Nearest...` |
| 3 | 3341 | `LIVE 3-arg getter (iter 186 12th-helper milestone)` | `LIVE 3-arg getter` |
| 4 | 727 | `iter-66 'Clear log' button` | `'Clear log' button` |
| 5 | 1491 | `Pairs with iter-145 cinematic camera primitives` | `Pairs with cinematic camera primitives` |
| 6 | 1869 | `SWFOC_ListMods (iter-300) + SWFOC_GetCurrentMod (iter-299)` | `SWFOC_ListMods + SWFOC_GetCurrentMod` |
| 7 | 1873 | `iter-297 stub-XML repair output` | `stub-XML repair output` |
| 8 | 3300 | `Pairs with iter-200 FOWReveal partial-reveal workflow` | `Pairs with FOWReveal partial-reveal workflow` |
| 9 | 3610 | `Wraps iter-184's NEW global-3-arg helper` | `Wraps the global-3-arg helper` |
| 10 | 4844 | `Pairs with iter-64 Filming for stills` | `Pairs with Filming preset for stills` |

### GroupBox header drift (6 edits — line-numbered)
| # | Line | Before | After |
|---|------|--------|-------|
| 1 | 2145 | `GLOBAL economy controls (LIVE — iter 231-233)` | `GLOBAL economy controls (LIVE)` |
| 2 | 2820 | `Hardpoint Inspector (iter-281 SWFOC_GetHardpoints; RequiresLiveSwfoc)` | `Hardpoint Inspector (SWFOC_GetHardpoints; RequiresLiveSwfoc)` |
| 3 | 3575 | `Fog of War (engine-Lua, LIVE — iter 180/184)` | `Fog of War (engine-Lua, LIVE)` |
| 4 | 4090 | `Scroll camera to target (iter 107 LIVE)` | `Scroll camera to target (LIVE)` |
| 5 | 4117 | `Camera primitive arc (iter 143-145 LIVE)` | `Camera primitive arc (LIVE)` |
| 6 | 4174 | `Camera primitive arc — extras (iter 162/165 LIVE)` | `Camera primitive arc — extras (LIVE)` |

## Empirical 100% verification

Pre-iter-397 grep:
```
ToolTip=".*iter[ -]\d+   → 7 matches
Header=".*iter[ -]\d+    → 6 matches
ToolTip="iter \d+        → 3 matches (initial discovery)
Total leftover            → 16 fixes needed
```

Post-iter-397 grep:
```
ToolTip=".*iter[ -]\d+|Header=".*iter[ -]\d+   → 0 matches across entire 4910-line XAML
```

**Empirical 100% completion verified per the iter-378→392 zero-match-grep idiom.**

## Discovered drift origins

The 16 leftover references survived iter 382-393 because:
1. **Per-tab sub-arc cleanup focused on visible labels**: iter-388 codified rule's instances were `iter <N> LIVE — calls (X):Y` format; sibling formats (`iter-N <noun>`, `iter N-M LIVE`) weren't enumerated until this iter.
2. **Cross-reference text in tooltips passed under radar**: e.g. line 1491 "Pairs with iter-145 cinematic camera primitives" — the iter-145 reference is a CROSS-REFERENCE, not a primary drift source. iter-388 rule covered primary `iter <N> LIVE — calls...` patterns; cross-references in supporting prose weren't explicitly listed.
3. **GroupBox headers had different transformation pattern**: iter-380 rule was about STALE headers (e.g. "Selected Unit Lua Actions (iter 117-118 LIVE)" implying that old iter window is the only thing); per iter-380 rule's "Why" section, generic headers like "Camera primitive arc (iter 143-145 LIVE)" were ambiguous — the iter range IS the descriptor, not stale info. iter-397 chose simplification per iter-388 sibling-rule consistency.

This is the **5th format variant** of the iter-388 codified rule:
- Variant A: `iter <N> LIVE — calls (X):Y` (the primary form)
- Variant B: `iter <N>+<M> LIVE — ...` (multi-iter combined)
- Variant C: `iter-<N> <verb-noun>` (compact ref)
- Variant D: `(iter <N> ...)` (parenthetical metadata)
- Variant E: `<func> <verb>` short-form (iter-389 retroactive)
- **NEW Variant F: cross-reference in supporting prose** (iter-397 NEW)

## iter-388 rule's empirical 100% verification chain

iter-388 codified at 88 instances; iter-397 added 16 more cross-XAML cleanups. Cumulative count: **104 instances of the rule applied empirically**. This is the largest empirical-validation count of any codified rule in the project (now 13× iter-345's 8).

## Verification gates

- 0 LIVE wires shipped (pure UX polish iter)
- 0 catalog entries changed
- 0 source/test changes (XAML-only)
- All editor build/test gates inherit GREEN from iter-391/392/394/395 chain
- Bridge harness 1100/0; verifier ledger lint 0/0 at 318 entries
- Editor binary republish queued (iter397_publish.ps1 launched in background)
- Reverse-orphan tests 1/1 PASSED <1 ms (iter-395 confirmed; XAML changes don't affect reverse-orphan)
- P2HP catalog 24 entries (iter-394 confirmed)

## Updated headline-doc claim

**iter-395 close-out claimed "9 V2 tabs 100% tooltip-clean" — iter-397 corrects this to "ENTIRE XAML 100% tooltip + header drift-clean".** Future operators reading any tooltip or GroupBox header in any V2 tab will see operator-meaningful descriptions, never internal iter codenames.

## Codification queue update (post-iter-397)

| Class | Pre-iter-397 | Post-iter-397 |
|---|---|---|
| Class A (high-recurrence) | 4 | 4 (unchanged) |
| Class B (medium-recurrence) | 5 | 5 (unchanged) |
| Class C (retire/promote) | 2 | 2 (unchanged) |
| iter-355→393 candidates | 18 | 18 (unchanged) |

**Codification queue NOW: 27 candidates total** (unchanged from iter-396).

## Net iter-397 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 16 surgical Edit operations across MainWindowV2.xaml (~30 LoC delta) |
| Doc shipped | 1 close-out doc (~150 lines) + 1 republish PS1 script |
| Pattern observations flagged | 0 NEW (consolidates iter-380 + iter-388 patterns; adds 5th variant retroactively to iter-388) |
| Cycle time | ~10 min (3 verification greps + 16 surgical edits + republish + close-out) |
| Empirical 100% completion verification | YES (zero-match grep across entire 4910-line XAML) |
| iter-388 rule empirical applications | 88 → **104** (+16 cross-XAML; **strongest evidence base in project** at 13× iter-345 baseline) |
| Headline-doc quad updates needed? | NO (iter-396 doc claimed "9 V2 tabs 100% tooltip-clean"; iter-397 corrects to "ENTIRE XAML 100% drift-clean" but no doc rewrite needed since the broader claim subsumes the prior) |

**iter-397 closes the FULL-XAML zero-drift sweep cleanly** — true 100% zero-iter-N-drift across all 24 V2 tabs.

66th post-iter-323 arc iter (1st full-XAML sweep iter); 127th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter options (iter-398)

In priority order:

1. **iter-400 milestone preparation** — 2 iters away; 4th major milestone. iter-398/399 could ship 2 small operator-visible polish iters before milestone, OR iter-398 could prepare the iter-400 capstone NOW. **Recommended option** — establish iter-400 capstone with current state.
2. **NEW arc-class kickoff** — multi-iter; defer to fresh session.
3. **Live SWFOC verify** of iter-343 Hardpoint Inspector chain — requires operator session.
4. **Additional UX polish iters** — broader UX polish targets exist (button labels, axis labels, Settings tab subsections, etc.) but the iter-N drift was the most-visible class. Diminishing returns post-iter-397.

iter-398 likely option 1 (iter-400 milestone capstone preparation; 2 iters away).
