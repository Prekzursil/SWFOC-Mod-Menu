# Ralph loop changelog — Hardpoint Icon Chain + Codification Cluster + First Drift Catch (iter 340-346)

**Date:** 2026-05-07
**Arc class:** Multi-arc post-iter-339 audit + research-implementation split + codification + reverse-orphan audit
**Iters covered:** iter-340 → iter-346 (7 iters; 1 docs supplement + 1 audit + 1 research + 2 implementation + 1 codification + 1 audit-with-drift-catch)
**Status at end-of-arc:** **iter-343 Hardpoint icon-resolution chain Phase 1 END-TO-END WIRED + iter-345 11th codified rule (FIRST 8-instance trigger; HIGHEST evidence base) + iter-346 FIRST drift catch in 5-audit sequence resolved**

## Executive summary

The 7-iter window since iter-339 has 3 distinct arcs:

| Phase | Iters | What shipped |
|-------|-------|--------------|
| Audit + Hardpoint Research | iter 340-342 | Operator changelog supplement closing iter-330 gap + Phase2HookPending CLEAN audit (0 drift catches via iter-329 rationale extensions compounding) + Hardpoint icon-resolution research (3 approaches analyzed; Approach A recommended) |
| Hardpoint Implementation + Wiring | iter 343-344 | Approach A optimistic chain (Combat tab → ResolveHardpointIconAsync → SWFOC_GetTypeLua → ResolveWeaponIcon) + MainViewModelV2 composition root wiring (6th consumer of icon resolver hot-swap chain) |
| Codification + Drift Catch | iter 345-346 | Codify `feedback_resolver_injection_at_composition_root.md` at FIRST 8-instance trigger (HIGHEST evidence base) + Reverse-orphan audit catches `SWFOC_GetTypeLua` regex-visibility flip from iter-343 |

**Net deltas across 7 iters**:
- LIVE wire count: **142** (unchanged — focus is composition root + audit + codification, not new wires)
- Codified `feedback_*.md` memory rules: **10 → 11** (iter-345 resolver injection at composition root)
- Asset class plugins (iter-313 LocateByConvention set): **6** (unchanged from iter-339)
- Combat tab GroupBoxes: **+0** (Hardpoint Inspector unchanged from iter-338/339; chain wiring layered on top)
- Editor binary republishes: **3× (iter-343 + iter-344 + iter-346 republish-required for snapshot edit but not committed)** — iter-344's 157.34 MB inherited
- Codification candidates flagged at 1/3 trigger: **+5 new** (iter-341/342/343 each contributed 1 + iter-346 contributed 2)
- iter-337 preflight rule consumers: **+5 new (iter-341/342/343/344 + iter-346 audit decision)** = total 6 across 7-iter window

## Phase 1 — Audit + Hardpoint Research (iter 340-342)

### iter 340 — Operator changelog supplement covering iter 331-339

Pure docs iter (~150 lines new file `ralph_loop_changelog_2026-05-07_supplement.md`). Closes 9-iter doc gap since iter-330. Documents 3 sub-arcs (iter-331-333 plugin extension + iter-334-337 codification cluster + iter-338-339 Hardpoint Inspector). All gates inherit from iter-339 republish (157.34 MB).

### iter 341 — Phase2HookPending re-audit (6th audit; CLEAN; 0 drift candidates)

24 P2HP entries triaged → **0 drift candidates surfaced**. Compares vs iter-323 audit which produced 5 drift candidates (iter-324/325/326/327/328 → 4 LIVE-already + 1 honest-defer arc). The difference: iter-329 docs cleanup arc extended rationale text on 5 entries with drift-prone framing (FreezeCredits + ListHeroes + GetPlanetTechAndBuildings + SpawnUnit + SetDamageMultiplier per-slot). iter-341 audit immediately recognized those entries as already-resolved.

**HEADLINE — iter-341 IS the proof that iter-329 docs cleanup work compounds**: 5 rationale extensions in iter-329 (~30 LoC) → 0 drift candidates in iter-341 → iter-342 can immediately pivot to NEW work without re-investigating the same 5 entries. **2.25× faster + 6× cycle savings** vs iter-323.

**NEW pattern observation** (1/3 trigger): `feedback_audit_compounds_via_rationale_extensions.md` — periodic catalog audits compound when rationale extensions are present.

### iter 342 — Hardpoint icon-resolution chain RESEARCH + design

3 candidate approaches analyzed for Combat tab Hardpoint Inspector icon column:
- **Approach A (optimistic chain)**: SWFOC_GetHardpoints → SWFOC_GetTypeLua per child → ResolveWeaponIcon. ~140 LoC + 8-10 tests. Risk: tostring(handle) might return "userdata: 0x..." not name. **Recommended**.
- **Approach B (explicit name-extraction bridge wire)**: NEW Lua_GetUnitTypeNameLua via DoString chain. ~220 LoC + 12-15 tests. Lower risk; higher cost.
- **Approach C (engine-already-does-this lookup)**: cross-reference hardpoint addresses against existing roster wires. ~80 LoC; risk: semantic mismatch.

**Critical unknown surfaced**: `tostring(GameObjectType_handle)` semantics undocumented for SWFOC engine. iter-169 catalog says "GameObjectType handle" but doesn't specify if `tostring` resolves to a name string or userdata pointer.

**iter-337 preflight rule decision tree application — 4th consumer**: pivot to smaller-scope RESEARCH iter (decision tree row 3 — "preflight surfaces unforeseen complexity"). 4/5 distinct decision-tree shapes now validated.

**NEW pattern observation** (1/3 trigger): `feedback_research_first_implementation_second.md` — when complexity is unknown, ship research-iter FIRST + implementation-iter SECOND.

## Phase 2 — Hardpoint Implementation + Wiring (iter 343-344)

### iter 343 — Approach A optimistic chain implementation (5th consumer of iter-337 rule)

Shipped iter-342's recommended Approach A:
- `HardpointEntry` record extended with `string? IconPath = null` optional field
- `CombatTabViewModel` ctor extended with `(UnitIconResolver? iconResolver = null)` per iter-311 codified rule (`feedback_optional_default_null_constructor_extension`)
- `RefreshHardpointsCore` extended with per-child icon enrichment chain
- NEW `ResolveHardpointIconAsync` method calls SWFOC_GetTypeLua → checks for `userdata:` / `ERR:` prefix → ResolveWeaponIcon (graceful failure)
- NEW `SetIconResolver` hot-swap method per iter-312/iter-321 pattern
- ListBox ItemTemplate restructured: StackPanel Horizontal wraps Image + TextBlock; Image collapses gracefully when IconPath null
- 8 pin tests in `Iter343HardpointIconChainTests.cs`

**Filtered tests**: 22/22 PASS in 1.97s. Editor republished 157.34 MB at May 7 08:05.

**NEW pattern observation** (1/3 trigger): `feedback_graceful_failure_enables_empirical_feedback.md` — when feature has empirical unknown, ship optimistic-with-graceful-failure FIRST + get operator feedback in NEXT iter + refine in iter+2 if needed.

### iter 344 — MainViewModelV2 composition root wiring (6th consumer of iter-337 rule)

iter-343 shipped the icon-resolution chain BUT MainViewModelV2 didn't pass the resolver. Operator clicking Refresh in Hardpoint Inspector got `IconPath = null` for all rows even if tostring(handle) would have returned valid name strings.

iter-344 fixes:
- Line 86: `Combat = new CombatTabViewModel(bridge, unitMutator)` → `Combat = new CombatTabViewModel(bridge, unitMutator, iconResolver)` (passes iter-309 injected `iconResolver`)
- Line 449+: Added `Combat.SetIconResolver(newResolver)` to existing OnSettingsPropertyChanged hot-swap chain (mirrors iter-312 + iter-321 pattern; **6th consumer** in the chain)

Editor republished 157.34 MB at May 7 08:09.

**End-to-end workflow operator now sees**:
1. Operator launches editor → Settings tab → "Unit icons" GroupBox → Browse... → picks `C:/swfoc_extracted_dds/`
2. Operator opens Combat tab → scrolls to Hardpoint Inspector GroupBox
3. Operator types/pastes unit `obj_addr` (hex) → clicks Refresh
4. **If tostring(GameObjectType_handle) returns NAME**: ListBox shows hardpoint icons next to each row → mid-battle weapon-type visual identification
5. **If tostring returns "userdata: 0x..."**: ListBox shows text-only rows (graceful fallback) → iter-345 pivots to Approach B

**Pattern observation** (8/3 trigger reached): `feedback_resolver_injection_at_composition_root.md` — 8 instances of iter-309 resolver-injection pattern across iter-308/309/312/317/318/319/321/344. Codification candidate flagged for iter-345.

## Phase 3 — Codification + Drift Catch (iter 345-346)

### iter 345 — Codify `feedback_resolver_injection_at_composition_root.md` at FIRST 8-instance trigger (11th codified rule; HIGHEST evidence base of any codified rule in the project)

Pure docs iter (~165 LoC memory rule + ~140 lines close-out + 1 MEMORY.md entry).

11-section template body:
- **Rule**: 3-step composition root injection pattern (ctor extension + SetIconResolver hot-swap method + MainViewModelV2 wire-up)
- **Why**: 8-instance table validates pattern across iter-308/309/312/317/318/319/321/344
- **How to apply**: 4 numbered steps with LoC estimates
- **Hot-swap behavior choice**: Pattern A (eager re-resolution; iter-308/317/318/319/321) + Pattern B (dormant; iter-344) decision tree
- **Honest break-out clause**: 4 NOT-applicable cases (singleton, single-consumer, async-init, stateless)
- **Edge cases worth flagging**: 5 sub-rules (timing + threading + test-isolation + iter-321 prefix-overlap + composition root growth)
- **Cost-benefit ratio**: ~5-15 LoC source + ~2-4 tests + ~5-10 min cycle = 2-3× faster after first 2 instances
- **Memory-write triggers**: 8-instance justification (HIGHEST evidence base in project; 2 hot-swap patterns)
- **Prospective uses**: 3 candidate future tab consumers (Inspector + WorldState + Director Mode)
- **Pattern reinforcement**: cross-link to iter-311 + iter-302 + iter-316 + iter-334 + iter-337 codified rules
- **Cross-link to related codified rules**: 7 links

**Codification thresholds now dynamic based on evidence quality**:
- New patterns: ≥6 instances (iter-302 precedent)
- Meta-rules at higher abstraction layers: ≥3 instances (iter-337 precedent)
- Production patterns with high evidence: ≥6 instances but flexible up to 8+ (iter-345)
- Variety of behavior shapes (e.g. 2+ patterns within a single rule) can substitute for higher count

**Codified-rules tally now at 11** across iter 100-345 = 1 codified rule per ~22 iters. iter-345 brings cadence slightly tighter than iter-337's 1 per ~24 iters.

### iter 346 — Reverse-orphan snapshot audit (5th audit; FIRST DRIFT CATCH after 4 consecutive CLEAN PASSes)

Per iter-345 close-out, iter-346 was queued as the substantially-overdue reverse-orphan snapshot audit (74-iter gap from iter-272; canonical ~22 iters). Iter-272 had assessed the framework as "converged" after 4 consecutive CLEAN PASSes.

**Audit fired and CAUGHT 1 drift entry**: `SWFOC_GetTypeLua` flipped from "regex-invisibly used" (iter-191 BuildUnitLuaNoArgCall string-literal form) to "regex-visibly used" (iter-343 `$"return SWFOC_GetTypeLua({childAddr})"` interpolated form in CombatTabViewModel.ResolveHardpointIconAsync).

**Diff:**
- `actuallyUnwired.Count` (53) ≠ `KnownUnwiredEntries.Count` (54)
- No-longer-unwired: 1 entry (`SWFOC_GetTypeLua`)
- **Newly-unwired: 0 entries** (high-value finding — across 73 iters of catalog growth, every catalog addition had a regex-visible call site shipped in the same iter)

**Fix**: Removed `"SWFOC_GetTypeLua",` line + added 5-line iter-343 drop-note (mirrors iter-200/iter-218 format) + updated iter-191 NOTE block to remove "GetTypeLua" from its list. Re-rebuild required (snapshot is COMPILED INTO test binary; `--no-build` runs against stale snapshot). Final: PASSED in <1 ms.

**Cadence summary** (iter-238/255/263/272/346 = 5 audits):

| Audit iter | Gap | Newly-unwired | No-longer-unwired | Result |
|---|---|---|---|---|
| iter-238 | (1st) | 0 | 0 | CLEAN |
| iter-255 | 17 | 0 | 0 | CLEAN |
| iter-263 | 8 | 0 | 0 | CLEAN |
| iter-272 | 9 | 0 | 0 | CLEAN |
| **iter-346** | **74** | **0** | **1** | **DRIFT CAUGHT** |

**HEADLINE**: iter-272's lesson #2 ("framework has converged") was OVERCONFIDENT — the mechanism does still catch drift; the dry spell was driven by ~108 iters of regex-invisible-only call-site additions. iter-343 broke the dry spell by adding the first regex-visible form in 73 iters.

**2 NEW pattern observations** (each at 1/3 trigger):
- `feedback_audit_dry_spell_is_not_convergence.md` — when an automated audit shows N consecutive CLEAN passes, do NOT downgrade to "regression-confirmation only"
- `feedback_no_build_safe_only_for_jit_paths.md` — when editing test-side static data (HashSet/Dictionary/array initializers), always re-run with full build; `--no-build` flag is safe only for JIT-compiled paths

## Pattern lessons surfaced

| Codification candidate | First instance | Second instance | Trigger status |
|------------------------|----------------|------------------|----------------|
| `feedback_audit_compounds_via_rationale_extensions.md` | iter-341 | — | 1/3 |
| `feedback_research_first_implementation_second.md` | iter-336+iter-338/339 | iter-342+iter-343 | **2/3 — codification at 3rd recurrence** |
| `feedback_graceful_failure_enables_empirical_feedback.md` | iter-343 | — | 1/3 |
| `feedback_codification_value_proven_by_next_iter.md` | iter-338 (carried from iter-339) | — | 1/3 |
| `feedback_vm_first_xaml_second_iter_split.md` | iter-148/149 | iter-338/339 | **2/3 (carried from iter-339)** |
| `feedback_audit_dry_spell_is_not_convergence.md` | iter-346 | — | 1/3 |
| `feedback_no_build_safe_only_for_jit_paths.md` | iter-346 | — | 1/3 |

**5 NEW patterns at 1/3 trigger + 2 patterns at 2/3 trigger** (1 carried + 1 new) = signal that the iter-strategy + audit-mechanics + research-implementation split layers are producing pattern lessons at a sustained rate.

## Operator-facing impact

### Audit + Hardpoint Research (iter 340-342)

- iter-340 changelog: operators reading the master changelog can now trace iter-331-339 work
- iter-341 audit clean: zero new audit follow-up arc needed; signals catalog discipline is working
- iter-342 research: 3 implementation approaches documented for future agents to choose

### Hardpoint Implementation + Wiring (iter 343-344)

Operator workflow now ENABLED end-to-end (pending live verification of `tostring(GameObjectType_handle)` semantics):

1. **Settings tab** → IconsRoot → Browse to extracted DDS root
2. **Combat tab** → Hardpoint Inspector GroupBox → paste unit obj_addr → click Refresh
3. **Optimistic icon rendering**: if engine returns name strings, ListBox shows weapon-type icons per hardpoint row
4. **Graceful fallback**: if engine returns userdata pointers, ListBox shows text-only rows (no broken-image placeholders)
5. **Mid-battle diagnosis**: operator can identify which hardpoint type is taking damage at a visual glance

Closes a major step toward the user mandate **"nice GUI showing units by their in-game pictures"** at the per-hardpoint scope.

### Codification + Drift Catch (iter 345-346)

- iter-345 rule: future tab additions consuming shared services (UnitIconResolver, V2BridgeAdapter, V2UnitMutationDispatcher) inherit a 3-step recipe + 2 hot-swap behavior patterns + decision tree
- iter-346 audit fix: test gate stays GREEN; future ralph-loop iters can ship without manual snapshot maintenance

## Cumulative tally (post-iter-346)

| Metric | iter-339 era | iter-346 era | Delta |
|--------|--------------|--------------|-------|
| LIVE wires | 142 | 142 | 0 (composition root + audit + codification focus) |
| Asset class plugins | 6 | 6 | 0 |
| Asset Browser categories | 6 | 6 | 0 |
| Codified `feedback_*.md` rules | 10 | 11 | +1 (iter-345 resolver injection at composition root) |
| Codification candidates at 1/3+ | 11 | 16 | +5 (iter-341/342/343 + iter-346 ×2) |
| Codification candidates at 2/3 | 1 | 2 | +1 (iter-336+iter-338/339 → iter-342+iter-343 research-first pattern; carried iter-148/149 + iter-338/339 vm-first-xaml-second still 2/3) |
| Combat tab GroupBoxes | N+1 (Hardpoint Inspector) | N+1 (chain wiring, no new GroupBox) | 0 |
| Editor binary republishes | 3 (iter-190 + iter-336 + iter-339) | 5 (+ iter-343 + iter-344) | +2 |
| Operator changelog supplements | 2 (iter-330 + iter-340) | 3 (iter-330 + iter-340 + iter-347) | +1 |
| Reverse-orphan audit drift catches | 0 | 1 | +1 (FIRST in iter-238/255/263/272/346 sequence) |
| iter-337 preflight rule consumers | 0 | 6 (iter-341 + iter-342 + iter-343 + iter-344 + iter-345 + iter-346) | +6 (highest-utilization codified rule) |

## Verification gates at end-of-arc

| Gate | Status |
|------|--------|
| `dotnet test --filter Iter338\|Iter339\|Iter343` | **22/22 PASS in 1.97s** (iter-343) |
| `dotnet test --filter CapabilityCatalogReverseOrphanTests` | **PASSED in <1 ms** (iter-346 after fix) |
| Editor build | GREEN (iter-344 republish + iter-346 test rebuild) |
| Editor binary | 157.34 MB at May 7 08:09 (iter-344 republish; iter-345 + iter-346 inherited) |
| Bridge harness | 1100/0 |
| Verifier ledger lint | 0/0 at 318 entries |
| iter-313 LocateByConvention plugin set | N=6 (unchanged) |
| iter-321 Asset Browser tab | 6 categories (unchanged) |
| Combat tab Hardpoint Inspector | END-TO-END operator-visible WITH ICON CHAIN (pending live tostring verification) |
| Codified rules | 11 (iter-345 added this window) |
| Reverse-orphan snapshot count | 53 entries (was 54; iter-346 dropped GetTypeLua) |

## Next-arc options (queued for iter 348+)

In priority order:

1. **Live SWFOC verify of iter-343 Hardpoint Inspector chain** — requires operator session; surfaces empirical evidence for `tostring(GameObjectType_handle)` semantics. If icons render: close mandate. If userdata: pivot to Approach B.
2. **Phase2HookPending re-audit** (iter-341 last; ~7 iters since; canonical cadence is ~17 — premature, defer to iter-358+)
3. **Codify `feedback_research_first_implementation_second.md`** at 2/3 trigger: defer until 3rd instance
4. **Codify `feedback_vm_first_xaml_second_iter_split.md`** at 2/3 trigger: defer until 3rd instance
5. **README capstone update** (iter-322 last; ~25 iters since; canonical cadence is ~30 — slightly past due at iter-352+)
6. **Reverse-orphan snapshot audit** (iter-346 last; canonical ~22 iters; way premature at iter-368+)

Recommended for **iter 348**: option 5 (README capstone update) is closest to canonical cadence; would cover iter-322-347 master loop window with 6 sub-arcs (Phase2HookPending audit + drift-resolution arc + docs cleanup + UI integration polish + asset class plugins + codification cluster + Hardpoint Inspector chain + audit drift catch). High operator value as the "headline reference" doc.

OR shorter-cycle: codify one of the 2/3-trigger patterns when the 3rd instance lands (iter-348-358 likely candidates as patterns recur).
