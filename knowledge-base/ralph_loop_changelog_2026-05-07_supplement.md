# Ralph loop changelog — Asset Plugin Extension + Codification Cluster + Hardpoint Inspector (iter 331-339)

**Date:** 2026-05-07
**Arc class:** Multi-arc post-iter-323 LIVE-delivery + codification + UX surfacing
**Iters covered:** iter-331 → iter-339 (9 iters; 5 LIVE + 2 codification + 1 republish/preflight + 1 XAML)
**Status at end-of-arc:** **iter-313 LocateByConvention plugin set extended to N=6 + iter-321 Asset Browser tab serves all 6 categories + iter-337 codified iter-strategy preflight stack + Hardpoint Inspector END-TO-END operator-visible**

## Executive summary

The 9-iter window since iter-330 has 3 distinct arcs:

| Phase | Iters | What shipped |
|-------|-------|--------------|
| Asset class plugin extension | iter 331-333 | Weapon icons + ability icons + Asset Browser tab 4→6 categories + iter-321 prefix-overlap bug fix |
| Codification + tooling cluster | iter 334-337 | Codify `feedback_locate_by_convention_extensible` + Lua Playground preset menu refresh + Combat preflight + republish + Codify `feedback_iter_strategy_preflight_stack` at FIRST 3-instance trigger |
| Hardpoint Inspector | iter 338-339 | VM smaller-scope + XAML wire-up + republish |

**Net deltas across 9 iters**:
- LIVE wire count: **142** (unchanged — iter 282/285/296/299/300 wires already counted in iter-330 era; this window adds plugin infrastructure + UI surfacing)
- Asset class plugins (iter-313 LocateByConvention set): **4 → 6** (+weapons +abilities)
- Asset Browser tab categories (iter-321): **4 → 6**
- Codified `feedback_*.md` memory rules: **8 → 10** (iter-334 LocateByConvention + iter-337 preflight stack)
- Combat tab GroupBoxes: **+1 Hardpoint Inspector**
- Editor binary republished: **2× (iter-336 + iter-339)** — closes 145-iter staleness gap from iter-190
- Codification candidates flagged at 1/3 trigger: **+5** (iter-333 prefix-overlap audit + iter-333 consumer extensibility audit + iter-336 binary-republish staleness + iter-338 codification-value-by-next-iter + iter-338 vm-first-xaml-second iter split)

## Phase 1 — Asset class plugin extension (iter 331-333)

### iter 331 — Weapon hardpoint icon resolver (5th plugin)

iter-331 task #582 framed Audit B last wire as RECOMMENDED, but preflight pivoted to weapon icons (iter-294 Audit E extension closing the iter-313 LocateByConvention plugin set). Shipped `ResolveWeaponIcon(weaponName, size = 32)` + `LocateWeaponIconDds` via `i_button_hp_` SWFOC hardpoint prefix. 11 pin tests including strict 5-way prefix discriminator + reverse 5-way symmetry + end-to-end 5-way co-existence.

**Pivot rationale**: Audit B has 2 not 1 remaining wires; iter-300 explicitly empirical-defers both; faction-roster-by-build-tab has 31 iters of zero operator demand signal. Weapon icons directly serve the user's "nice GUI showing units by their in-game pictures" mandate.

### iter 332 — Ability icon resolver (6th plugin; codification trigger)

Mirror of iter-331 with `i_button_ability_` SWFOC ability prefix. Default size 32 (3rd asset class to share size-32 default). 11 pin tests including 6-way validation. Pattern stable at N=6; codification trigger reached.

### iter 333 — Asset Browser tab 4→6 categories + iter-321 prefix-overlap bug fix

Extended `AssetBrowserTabViewModel.CategoryPrefixes` from 4 to 6 entries. **Surfaced latent iter-321 prefix-overlap bug**: `Directory.EnumerateFiles(root, "i_button_*.dds")` is a SUPERSET of `i_button_hp_*.dds` + `i_button_ability_*.dds` files. Without iter-333's regression guard test, the bug would have shipped silently. Fix: longest-prefix-first ordering of CategoryPrefixes array + `HashSet<string>` claim tracking ensures each DDS file matches exactly ONE category.

**Pattern lessons** (2 NEW codification candidates at 1/3): `feedback_glob_walker_prefix_overlap_audit.md` + `feedback_consumer_extensibility_audit.md` — producer extensibility is clean (literal-prefix matching); consumer extensibility surfaces glob-overlap concerns.

## Phase 2 — Codification + tooling cluster (iter 334-337)

### iter 334 — Codify `feedback_locate_by_convention_extensible.md` (9th codified rule)

6-instance trigger reached at iter-332 (units → portraits → factions → planets → weapons → abilities). Mirrors iter-302 codification cadence per iter-334 lesson "codification iters have highest cost-benefit-ratio". 11-section template with 4 honest-break-out cases + 5 edge-case sub-rules + cost-benefit ratio (~50 LoC source + ~225 LoC tests + ~30 min cycle = 2-4× faster after first 2 instances).

### iter 335 — Lua Playground preset menu refresh (closes 65-iter doc gap)

8 new presets covering iter 282/285/296/299/300 LIVE wires. Preset menu now lists 99 entries covering iter 100-300 LIVE wires (was 91). GroupBox header bumped 270 → 300. Mid-iter pin-synchronization issue caught: 2 different test files (Iter252 + Iter271) pin the same GroupBox header text — `feedback_pin_synchronization_across_test_files.md` flagged at 1/3.

### iter 336 — Combat tab weapon-icon column preflight pivot + editor republish (closes 145-iter staleness gap)

iter-326/327/328 preflight stack applied at iter-strategy layer for first time. Found `SWFOC_GetHardpoints` LIVE wire EXISTS but returns child addresses + HP (not weapon names); composition needs 2-bridge-call chain (~250-300 LoC vs predicted ~150 LoC). **Pivoted to smaller-scope close-out + republish** (165.49 MB → 157.33 MB at May 7 07:14). NEW pattern lesson `feedback_binary_republish_staleness_audit.md` flagged at 1/3 — schedule explicit republish at ~50-iter intervals.

### iter 337 — Codify `feedback_iter_strategy_preflight_stack.md` (FIRST 3-instance trigger)

iter-326/327/328 preflight stack applied at iter-strategy layer reached 3 instances at iter-336 (iter-331 LIVE delivery + iter-332 mirror reuse + iter-336 smaller-scope pivot). **FIRST 3-instance codification** in the SWFOC project; justified because PATTERN is meta-rule already established at iter-302 6-instance threshold. 11-section template with 5-pivot decision tree + cost-benefit ratio (~40 sec preflight + ~30-90 min savings = ~45× ROI; STRONGEST single-rule ROI in codified set).

## Phase 3 — Hardpoint Inspector (iter 338-339)

### iter 338 — VM smaller-scope (FIRST consumer of iter-337 preflight rule)

`HardpointEntry` record `(int Index, long ChildAddr, float Hp)` + static `ParseListFromBridgeReply(string?)` parser in `CombatTabState.cs`. `_bridge` field + `Hardpoints` ObservableCollection + `RefreshHardpointsCommand` + `RefreshHardpoints` CapabilityAwareAction in `CombatTabViewModel.cs`. 8 pin tests (parser-focused). **Mid-iter compile error caught**: `BridgeRoundTripResult` has `Response` + `ErrorMessage` fields, NOT `ResponseOrError` (iter-283 5-second-grep rule reinforced).

### iter 339 — XAML wire-up + editor republish (SECOND consumer of iter-337 rule)

Combat tab Hardpoint Inspector GroupBox inserted after iter-219 Suspend AI section. TextBox + Refresh button + ListBox with `[Index N] 0xHEXADDR hp=H.HHH` ItemTemplate. CapabilityAwareAction badge + tooltip per iter-308/iter-311 codified pattern. 6 XAML pin tests. Editor republished 157.33 → **157.34 MB at May 7 07:49**.

**Operator workflow now LIVE**: Inspector tab → Copy obj_addr (iter-191) → Combat tab → paste in Hardpoint Inspector TextBox → click Refresh → ListBox shows hardpoint vector with index/address/HP per row. Mid-battle damage diagnosis enabled.

## Pattern lessons surfaced

| Codification candidate | First instance | Second instance | Trigger status |
|------------------------|----------------|------------------|----------------|
| `feedback_glob_walker_prefix_overlap_audit.md` | iter-333 | — | 1/3 |
| `feedback_consumer_extensibility_audit.md` | iter-333 | — | 1/3 |
| `feedback_pin_synchronization_across_test_files.md` | iter-335 | — | 1/3 |
| `feedback_binary_republish_staleness_audit.md` | iter-336 | — | 1/3 |
| `feedback_codification_value_proven_by_next_iter.md` | iter-338 | — | 1/3 |
| `feedback_vm_first_xaml_second_iter_split.md` | iter-148/149 | iter-338/339 | **2/3 — codification at 3rd recurrence** |

**5 NEW patterns at 1/3 trigger + 1 pattern at 2/3 trigger** = signal that the iter-strategy layer + consumer-extension layer + tooling-discipline layer are producing pattern lessons at higher rate than catalog-resolution layer.

## Operator-facing impact

### Asset class extension (iter 331-333)

Operator with extracted .meg assets now sees:
- Weapons + abilities surface in Asset Browser tab (was hidden)
- All 6 asset classes from iter-313 LocateByConvention plugin set browsable in one tab
- No false-positive "ghost unit" rows from prefix-overlap bug

### Codification (iter 334 + iter 337)

Future agents starting any iter inherit:
- iter-334 rule: how to extend UnitIconResolver at 7th asset class (~50 LoC marginal cost)
- iter-337 rule: how to preflight any feature work iter (~40 sec; 45× ROI)

### Tooling discipline (iter 335 + iter 336)

Operators benefit from:
- 99-entry Lua Playground preset menu (covers iter 100-300 LIVE wires)
- Editor binary current with iter 191-339 features (was iter-190 stale)

### Combat tab Hardpoint Inspector (iter 338-339)

Operator can now:
- Paste unit obj_addr → click Refresh → see hardpoint vector
- Diagnose mid-battle which hardpoint is taking damage
- Lay foundation for future icon-resolution chain (iter-340+)

## Cumulative tally (post-iter-339)

| Metric | iter-330 era | iter-339 era | Delta |
|--------|--------------|--------------|-------|
| LIVE wires | 142 | 142 | 0 (plugin/UX surfacing focus) |
| Asset class plugins | 4 | 6 | +2 |
| Asset Browser categories | 4 | 6 | +2 |
| Codified `feedback_*.md` rules | 8 | 10 | +2 |
| Codification candidates at 1/3+ | 5 | 11 | +6 |
| Codification candidates at 2/3 | 0 | 1 | +1 |
| Combat tab GroupBoxes | N | N+1 | +1 (Hardpoint Inspector) |
| Editor binary republishes | 1 (iter-190) | 3 (iter-190 + iter-336 + iter-339) | +2 |
| Operator changelog supplements | 1 (iter-330) | 2 (iter-330 + iter-340) | +1 |

## Verification gates at end-of-arc

| Gate | Status |
|------|--------|
| `dotnet test --filter Iter338\|Iter339` | **14/14 PASS in 1.58s** |
| `dotnet test --filter Iter331\|Iter332\|Iter333` | (per-iter close-outs document) all GREEN |
| Editor build | GREEN |
| Editor binary | 157.34 MB at May 7 07:49 (iter-339 republish) |
| Bridge harness | 1100/0 |
| Verifier ledger lint | 0/0 at 318 entries |
| iter-313 LocateByConvention plugin set | N=6 (units + portraits + factions + planets + weapons + abilities) |
| iter-321 Asset Browser tab | 6 categories, longest-prefix-first claim tracking |
| Combat tab Hardpoint Inspector | END-TO-END operator-visible |
| Codified rules | 10 (iter-334 + iter-337 added this window) |

## Next-arc options (queued for iter-341+)

In priority order:

1. **Hardpoint icon-resolution chain mini-arc** (iter-336 preflight identified 2-bridge-call complexity): iter-341 SWFOC_GetType existing wire research + iter-342 Combat tab DataGridTemplateColumn extension
2. **README capstone update** (iter-322 last; ~19 iters since; canonical cadence is ~30 — premature, defer to iter-352+)
3. **Codify `feedback_vm_first_xaml_second_iter_split.md`** at 2/3 trigger: defer until 3rd instance unless context budget allows premature codification
4. **Phase2HookPending re-audit** (iter-323 last; ~17 iters since; canonical cadence is ~16 — slightly past due)
5. **Audit B remaining 2 wires honest-defer doc** (faction-roster-by-build-tab + hero-roster from iter-300; explicit rationale doc for "deferred until empirical evidence")

Recommended: **option 4 (P2HP re-audit)** — slightly past 16-iter cadence; surfaces drift candidates for iter-342+ resolution arc; historically high-value (iter-323 produced 6 pattern lessons + 5 rationale extensions).
