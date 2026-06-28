# Iter 377 — UI/UX polish arc kickoff: survey of Combat / UnitControl / Galactic tabs + 1 stale-header fix shipped

**Date:** 2026-05-07
**Arc class:** UI/UX polish (concrete operator-visible work; ~5-10 iter arc; deferred ~106 iters since iter-271 NON-A1.x pivot)
**Predecessor:** iter-376 (cheap-insurance republish; empirically confirmed 0-source-impact for iter 365-374 audit-organization cluster)
**Successor (queued):** iter-378 (specific UX improvement TBD per "Next iter" below)

## What this iter does

1. **Surveys** 3 high-traffic V2 tabs (Combat, UnitControl, Galactic) for clutter / inconsistency / stale text.
2. **Documents** 6 distinct UX patterns that recur across the 3 surveyed tabs and likely apply to the other 19+ tabs.
3. **Ships 1 concrete fix** — the smallest atomic improvement that demonstrates the pattern: stale UnitControl GroupBox header `"Selected Unit Lua Actions (iter 117-118 LIVE)"` → `"Selected Unit Lua Actions (~24 LIVE wires; see per-button badges)"`.
4. **Queues 4 iter-378+ improvements** at increasing scope: simple → moderate → complex.

## Tab footprint analysis

`MainWindowV2.xaml` is a single 4910-line file containing all 24 V2 tabs inline. Per-tab line ranges (computed from TabItem boundary positions):

| Tab | Line range | Lines |
|---|---|---|
| Connection & Diagnostics | 295-774 | **479** |
| Unit Control | 986-1380 | **394** |
| Combat | 2482-2860 | **378** |
| Spawning | 3008-3333 | 325 |
| Galactic | 3333-3655 | 322 |
| Inspector | 2196-2482 | 286 |
| Settings | 1685-1926 | 241 |
| Quick Actions | 4679-4910 | 231 |
| World State | 1380-1607 | 227 |
| Camera & Debug | 4006-4218 | 212 |
| Player State | 774-986 | 212 |
| Hero Lab | 3655-3843 | 188 |
| Economy | 2025-2196 | 171 |
| Speed | 2860-3008 | 148 |
| Director Mode | 4363-4494 | 131 |
| Battle Control | 3843-3949 | 106 |
| Tactical Units | 1926-2025 | 99 |
| Lua Playground | 4218-4307 | 89 |
| Probes & Scripts | 1607-1685 | 78 |
| Cross-Faction | 4494-4565 | 71 |
| Unit Stat Editor | 4565-4630 | 65 |
| Story Events | 3949-4006 | 57 |
| Event Stream | 4307-4363 | 56 |
| Asset Browser | 4630-4679 | 49 |

**Top 5 cluttered tabs**: Connection & Diagnostics (479L), UnitControl (394L), Combat (378L), Spawning (325L), Galactic (322L).

The fact that the entire UI lives in one ~5000-line file is itself a clutter-by-co-location signal. UI refactor to per-tab UserControl files is out of scope for the polish arc — high-risk, low-marginal-value churn — but worth flagging for any future restructure project.

## Survey findings: 6 recurring UX patterns

### Pattern 1: Stale GroupBox headers reference long-superseded iter ranges

**Example A (UnitControl, line 1086 — FIXED THIS ITER):**
```xaml
<GroupBox Grid.Row="7" Header="Selected Unit Lua Actions (iter 117-118 LIVE)">
```
Header claims iter 117-118 but the section actually contains 8 sub-batches:
- iter 110-112 (per-unit Lua actions, ~11 buttons)
- iter 167/171/172 (read-side, 4 buttons)
- iter 118 (Change owner, 1 button)
- iter 194 → iter 163 wires (combat orders, 3 buttons)
- iter 211 → iter 156 wires (extension, 6 buttons)
- iter 212 → iter 157 wires (mega-batch, 8 buttons)
- iter 213 → iter 153/162 wires (bool batch, 5 buttons)
- iter 218 → iter 180 wires (Underworld, 1 button)

Total: ~39 buttons across ~24 distinct LIVE wires.

**Example B (Combat, line 2716):**
```xaml
<GroupBox Header="Per-unit combat actions (iter 193 — iter 154 LIVE wires)">
```
This one is less stale — only iter 193 reference, but iter 219 added Suspend_AI to the same section. Still surfaces internal codename.

**Operator impact**: Internal iter codenames are meaningless to operators. They scan headers for "what does this section do?", not "what month did Claude ship this?".

### Pattern 2: Internal `iter N` references in user-facing tooltips

Throughout all 3 surveyed tabs, button tooltips lead with `"iter <N> LIVE — <description>"`:
- `"iter 110 LIVE — calls (unit):Make_Invulnerable(true) via DoString. Propagates to all hardpoints."`
- `"iter 154 LIVE — (unit):Heal() no-arg. Restores the unit to full hull."`
- `"iter 167 LIVE — calls (unit):Get_Hull(). Returns current HP as a float in the Bridge responses below."`

**Recurrence count** (UnitControl row-7 alone): 30+ tooltips with `"iter <N>"` prefix.

**Operator impact**: Per-button capability badges (the `<TextBlock Text="{Binding *.Badge}">` controls) ALREADY surface LIVE / PHASE 2 PENDING status in the UI. The `iter N` prefix in tooltips is internal traceability noise — it doesn't help operators decide whether to click. Drop the prefix; keep the description.

### Pattern 3: Multiple stacked amber "REPLAY MIRROR ONLY" warning banners per tab

**Combat tab (line 2482-2860)** has TWO amber Borders:
1. Line 2488 — tab-level Phase2 banner (binds to `HasPhase2PendingAction`)
2. Line 2560 — Scalars-section banner ("⚠ REPLAY MIRROR ONLY — not yet live in the running game")

**Galactic tab (line 3333-3651)** has THREE amber Borders:
1. Line 3348 — tab-level Phase2 banner
2. Line 3428 — Change-owner section banner
3. Line 3507 — Story-arrival section banner

**Operator impact**: When a tab has 2-3 stacked amber warnings within 100 lines of XAML, operators tune them out (amber-fatigue). The redundancy makes the warnings less effective, not more. Tab-level banner already covers the tab's PHASE 2 PENDING surface; section-level banners should only fire if their section's contents differ from the tab-level summary.

### Pattern 4: Scattered TextBox input fields without grouping (UnitControl row 7)

UnitControl's mega-section has **7 distinct TextBox inputs** scattered across 280 lines:
1. `SelectedUnitLuaExpr` (line 1092)
2. `TargetPlayerLuaExpr` (line 1138)
3. `TargetForCombatOrderLuaExpr` (line 1158)
4. `AbilityNameLuaExpr` (line 1195)
5. `SpecialWeaponSlotLuaExpr` (line 1246)
6. `MaxSpeedOverrideLuaExpr` (line 1313)
7. `CorruptAmountLuaExpr` (line 1356)

Each TextBox is paired with an explanatory paragraph above it AND immediately followed by a WrapPanel of buttons that consume that field. Operator workflow: scroll until I see the right description, type into the TextBox below, then click the button below that.

**Operator impact**: The "scattered input fields" pattern means an operator who wants to use multiple actions in sequence must scroll up/down repeatedly. A single "Inputs (Lua expressions)" GroupBox at the top of row 7 — listing all 7 fields with labels — would let operators set up their state once, then click any button without scrolling.

### Pattern 5: WrapPanel button-badge interleave breaks visual rhythm

**Pattern repeated across all 3 tabs**:
```xaml
<Button Content="..." Command="{Binding XCommand}" />
<TextBlock Text="{Binding XAction.Badge}" FontSize="9" ... VerticalAlignment="Center" />
<Button Content="..." Command="{Binding YCommand}" />
<TextBlock Text="{Binding YAction.Badge}" FontSize="9" ... VerticalAlignment="Center" />
```

When badges render with `MutedForeground` color and 9pt SemiBold, they take horizontal space without strong visual signal. Result: a row of 8 buttons becomes a row of 16 controls (button-badge-button-badge-...). The badges' "PHASE 2 PENDING" / "LIVE" text fragments fragment the row visually.

**Operator impact**: Hard to scan a row of 8 buttons when each is split by an inline badge text. Better: badges as small colored dots (•/⚠/✓) using the WarningForeground/AccentForeground theme brushes, or render badges only on hover/tooltip, or pin badges to button corners via Adorner.

### Pattern 6: Long unwrapped tooltips reference past iter context

```xaml
ToolTip="iter 154 LIVE — (unit):Set_Damage_Modifier(mult). Per-unit OUTGOING damage multiplier (different surface from the iter-96 GLOBAL multiplier above)."
```

The trailing parenthetical "different surface from the iter-96 GLOBAL multiplier above" requires operators to remember what iter-96 is. This works for a project archaeologist; not for an operator clicking buttons.

**Operator impact**: Tooltips should describe what this button does NOW, in operator-visible terms. Past arc context belongs in commit messages and changelogs, not in the live UI.

## What was shipped iter-377 (concrete improvement)

Single 1-line XAML change to fix Pattern 1 example A:

**Before**:
```xaml
<GroupBox Grid.Row="7" Header="Selected Unit Lua Actions (iter 117-118 LIVE)">
```

**After**:
```xaml
<!-- 2026-05-07 (iter 377): UX polish — header was stale at "(iter 117-118 LIVE)"
     despite the section growing through 8 sub-batches over iter 110-218
     (~24 distinct LIVE wires). Operators see the full per-button capability
     badges + tooltips below; the header just says how many actions live here. -->
<GroupBox Grid.Row="7" Header="Selected Unit Lua Actions (~24 LIVE wires; see per-button badges)">
```

**Cost**: 1-line text change + 4-line provenance comment.
**Operator visibility**: every operator who opens UnitControl tab sees an honest header instead of a stale 100-iter-old-claim header.
**Risk**: zero — header is binding-free string literal; no tests grep for the literal "iter 117-118".

## Iter-378+ queued UX improvements (in priority order)

Each is sized as **~1 iter** of concrete operator-visible work.

### iter-378 (simple, ~30 LoC, low risk)
Apply the iter-377 stale-header fix pattern to **Combat tab line 2716** ("Per-unit combat actions (iter 193 — iter 154 LIVE wires)" → "Per-unit combat actions (5 LIVE wires)") + audit ALL other GroupBox headers across all 24 tabs for similar staleness via a single grep pass.

### iter-379 (moderate, ~60-100 LoC, low risk)
**Demote internal iter references from user-facing tooltips** across UnitControl tab. Pattern: `"iter <N> LIVE — <desc>"` → `"<desc>"` (drop the iter prefix; capability badge already surfaces LIVE/PHASE2 status). Single tab; ~30 tooltips.

### iter-380+ (multi-iter, larger scope)
**Extend tooltip cleanup** to Combat / Galactic / remaining 21 tabs. ~5 iter sub-arc covering ~150 tooltips total.

### iter-381 (moderate, ~80 LoC, medium risk)
**De-duplicate amber warning banners**. When tab-level Phase2 banner is visible, suppress section-level banners (or vice-versa). Use existing `HasPhase2PendingAction` binding + a new `SectionPhase2Visibility` computed property. Combat + Galactic tabs.

### iter-382+ (multi-iter, ~150-300 LoC, medium-high risk)
**Consolidate UnitControl input-field scattering** by extracting the 7 TextBox fields into a single "Inputs (Lua expressions)" GroupBox at the top of row 7. Each field gets a label + tooltip; the 6 sub-batch WrapPanels lose their per-batch explanation+textbox preamble and just show their button group. ~150 LoC restructure + binding paths preserved.

### iter-383+ (multi-iter, ~50-100 LoC, low risk)
**Replace inline TextBlock badges with colored Border+TextBlock dots**. Per-button badge becomes a 12px circle with color-coded background (green = LIVE, amber = PHASE 2 PENDING, gray = LIVE-ONLY). Tooltip on the dot still shows the full badge text. WrapPanel rows become visually crisp.

### iter-384+ (multi-iter, ~200-300 LoC, low-medium risk)
**Tooltip historical-context cleanup**: rewrite tooltips that reference `iter N` patterns or "different surface from..." cross-references into present-tense operator-visible descriptions.

## Why ship 1 small fix THIS iter (vs survey-only iter)

Iter-377 is the explicit pivot from meta-codification (iter 359-374) to concrete operator-visible work. A pure-survey-only iter would mirror the iter-375 meta-reflection style and risk re-entering the cluster-saturation pattern. Shipping ONE small fix:

1. **Empirically validates** the survey-then-ship pattern is sustainable (binary timestamp will advance from iter-364 baseline; `dotnet publish` will write).
2. **Demonstrates** an iter-378 template (small focused fix + provenance comment + survey doc reference) for the rest of the arc.
3. **Avoids** the iter-376 "0 source impact" diagnostic that flagged the audit-organization cluster.

## Verification gates ALL GREEN

| Gate | Result |
|------|--------|
| `dotnet publish` | **Build succeeded** (exit code 0) |
| Binary size | **157.89 MB** (advanced +0.01 MB from iter-364 baseline 157.88 MB) |
| Binary LastWriteTime | **2026-05-07 11:08:38** (advanced from iter-364 timestamp 10:19:09 — ~50 min later) |
| Filtered test verify | **Passed! Failed: 0, Passed: 22, Skipped: 0, Total: 22, Duration: 193 ms** |
| Bridge harness | inherits 1100/0 |
| Verifier ledger lint | inherits 0/0 at 318 entries |

The binary timestamp ADVANCED (vs iter-376's preserved-from-iter-364 timestamp), empirically confirming iter-377 produced concrete source impact. The XAML change reached the build artifact end-to-end.

## Pattern observation flagged

### NEW pattern observation (1/3 trigger): `feedback_stale_groupbox_header_drift.md`

When a GroupBox header includes a specific iter range (e.g., `"(iter 117-118 LIVE)"`), and the section continues to receive new sub-batches over many iters without header updates, the header becomes a misleading historic artifact. Recurrence likelihood: HIGH — current MainWindowV2.xaml has 5+ similar instances. Codify if 3rd recurrence happens (e.g., another tab's iter-N header drifts substantially behind its actual iter range).

## Codification queue update (post-iter-377)

| Class | Pre-iter-355 | Post-iter-377 |
|---|---|---|
| Class A (high-recurrence) | 4 | 4 (unchanged) |
| Class B (medium-recurrence) | 5 | 5 (unchanged) |
| Class C (retire/promote) | 2 | 2 (unchanged) |
| Class C low-priority watch | 1 | 1 (unchanged) |
| iter-355→376 candidates | 0 | +16 (6 codified iter-359/363/368/371/373/374 + 10 at 1/3 trigger) |
| **iter-377 NEW** | 0 | **+1 NEW** (`stale_groupbox_header_drift` at 1/3 trigger) |

**Codification queue NOW: 29 candidates total** (was 28 pre-iter-377; +1 NEW).

## Net iter-377 outcome

| Aspect | Value |
|--------|-------|
| Source code shipped | 1 XAML edit (1 LoC text change + 4 LoC provenance comment) |
| Doc shipped | 1 survey doc (~270 lines) + this close-out doc embedded |
| UX patterns inventoried | 6 distinct patterns documented |
| iter-378+ tasks queued | 6 specific improvements (small → multi-iter) |
| Pattern observations flagged | 1 NEW at 1/3 trigger |
| Cycle time | ~30 min (survey + ship + doc) |

**iter-377 is the deliberate pivot from meta-codification cluster (iter 359-374) to concrete UI/UX polish work.** The 6-pattern inventory above gives ~5-10 iters of operator-visible improvements without further RE work or new arcs needed.

47th post-iter-323 arc iter (6 LIVE + 9 codification + 4 republish + 2 XAML + 18 docs/audit/inventory/promote/verification + 1 warning-cleanup + 1 build-verify + 3 test-verify + 2 P2HP audit + 1 reverse-orphan audit + 2 pre-compound + 1 pre-compound-verify + 1 meta-reflection + **1 UX-polish kickoff**); 108th consecutive NON-A1.x iter per iter-269 lesson #2.
