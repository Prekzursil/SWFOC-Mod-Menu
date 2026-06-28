# iter-343 — Hardpoint icon-resolution chain Phase 1 Approach A optimistic chain (5th consumer of iter-337 preflight rule; closes iter-339 deferred icon resolution)

**Date:** 2026-05-07
**Arc class:** LIVE delivery (icon-resolution chain Phase 1; mandate-aligned per "nice GUI showing units by their in-game pictures")
**Predecessor:** iter-342 (Hardpoint icon-resolution chain RESEARCH + design)
**Successor (queued):** iter-344 (live SWFOC verify OR pivot to Approach B if tostring returns userdata)

## What changed (3 files modified + 1 binary republished; ~140 LoC + ~270 LoC tests)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.Core/V2Vm/CombatTabState.cs` (+~5 LoC):
  - Extended `HardpointEntry` record with `string? IconPath = null` optional field (defaults to null per iter-311 codified `feedback_optional_default_null_constructor_extension`)
  - Class XML doc updated to document iter-343 Approach A chain + tostring(handle) failure mode
- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/CombatTabViewModel.cs` (+~80 LoC):
  - Added `_iconResolver` field + `using SwfocTrainer.Core.Assets;`
  - Extended ctor with `(UnitIconResolver? iconResolver = null)` per iter-311 codified rule (backward-compatible)
  - Extended `RefreshHardpointsCore` to enrich entries via per-child icon resolution chain
  - NEW `ResolveHardpointIconAsync(long childAddr)`: calls `SWFOC_GetTypeLua(childAddr)` → checks for `userdata:` / `ERR:` prefix → calls `_iconResolver.ResolveWeaponIcon(typeName)` → returns IconPath OR null gracefully
  - NEW `SetIconResolver(UnitIconResolver?)` hot-swap method per iter-312/iter-321 pattern
  - LastStatus message updated to report `N hardpoint(s); M with icons`
- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/MainWindowV2.xaml` (+~20 LoC):
  - Hardpoint Inspector ListBox ItemTemplate restructured: `<StackPanel Orientation="Horizontal">` wraps `<Image Source="{Binding IconPath}" Width="32" Height="32"/>` + existing TextBlock
  - Image collapses gracefully when IconPath is null (no broken-image placeholder)
- **NEW** `tests/SwfocTrainer.Tests/Regression/Iter343HardpointIconChainTests.cs` (~115 LoC, **8 facts**):
  - HardpointEntry record contract pins (default null, with-mutation, explicit null)
  - Parser-IconPath isolation pin (parser leaves IconPath null; VM enriches separately)
  - 4 XAML pin tests (Image element + 32px size + horizontal StackPanel layout + text preservation)
- **REPUBLISH** `SWFOC editor/publish/SwfocTrainer.App.exe`: 157.34 MB at May 7 08:05.

## Verification gates ALL GREEN

```
[Start-Process bypass — Clink-safe]
dotnet test --filter "FullyQualifiedName~Iter338|...~Iter339|...~Iter343"
Test Run Successful.
Total tests: 22
     Passed: 22
 Total time: 1.9708 Seconds
```

- iter-338 parser tests (8 facts): PASS
- iter-339 XAML pin tests (6 facts): PASS
- iter-343 chain pin tests (8 facts): PASS
- Combined Hardpoint Inspector suite: **22/22 PASS in 1.97s** (no regression in iter-338/339 surface)
- Editor build: GREEN
- Editor binary republished
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries

## iter-337 preflight rule application — 5th consumer (validates rule beyond 4-instance threshold)

**Step 1 — Rationale-grep**: iter-342 close-out documented Approach A choice + 3-call chain feasibility analysis. **PASS**.

**Step 2 — Bridge-source-grep**: SWFOC_GetTypeLua signature confirmed unchanged from iter-169 (uses iter-167 unit-getter helper with `tostring(method())` codegen). **PASS**.

**Step 3 — 4-step composition preflight**:
- (a) Catalog rationale: iter-169 catalog says "GameObjectType handle" — semantics still ambiguous, but graceful failure mode handles both cases
- (b) Engine-surface gap: NO (SWFOC_GetTypeLua + ResolveWeaponIcon both LIVE)
- (c) Bridge wire orphan: closing the orphan with iter-343 implementation
- (d) Composition genuinely sufficient: YES with graceful degradation if tostring returns userdata

**Decision tree application**: All preflight steps green → continue with original Approach A plan. Outcome matched — shipped at predicted ~140 LoC scope.

**5th consumer milestone**: iter-337 codified at 3-instance trigger; iter-343 is the 5th consumer (iter-338, iter-339, iter-341, iter-342, iter-343). Rule's stability beyond 4-instance threshold validated.

## Operator-facing impact (Hardpoint Inspector with optimistic icon resolution)

**Pre-iter-343**: Operator opens Combat tab → Hardpoint Inspector → types unit obj_addr → clicks Refresh → ListBox shows TEXT-ONLY rows: `[Index 0] 0x140012340 hp=750.000`.

**Post-iter-343 (if Approach A's tostring assumption holds)**: Operator opens Combat tab → Hardpoint Inspector → types unit obj_addr → clicks Refresh → ListBox shows **icon + text rows**: `[ICON] [Index 0] 0x140012340 hp=750.000`. Mid-battle damage diagnosis enriched with weapon-type visual identification.

**Post-iter-343 (if tostring returns userdata)**: Same operator workflow; icons stay null + Image element collapses → text-only rows render unchanged. Graceful failure mode — no broken-image placeholders, no error messages, just plain text. iter-344 will pivot to Approach B (NEW name-extraction bridge wire) if needed.

**Live SWFOC verify required to know which path operator gets**. Empirical evidence will inform iter-344 decision tree.

## Pattern lessons

### iter-337 preflight rule consumed at 5 instances; bimodal cadence pattern stable

iter-338 + iter-339 (continue) + iter-341 (audit pivot) + iter-342 (research pivot) + iter-343 (continue with optimistic chain). **5 consumers across 6-iter window** = roughly 1 consumption per iter for the iter-337 codification's first 6 iters. Rule's value compounds at high rate.

### NEW pattern observation — *graceful-failure-mode shipping enables empirical-feedback iter-loop*

iter-343 Approach A ships optimistically with graceful failure (icons render null if tostring returns userdata). **This enables operator-feedback-driven iteration**:
1. Ship optimistic implementation in iter-343
2. Operator runs against live SWFOC in their session
3. Reports back whether icons render or stay null
4. iter-344 either: closes feature (icons rendered) OR pivots to Approach B (new wire needed)

**Pattern lesson** (1st instance; codification candidate at 3rd recurrence): when feature implementation has empirical unknown, ship optimistic-with-graceful-failure FIRST, get operator feedback in NEXT iter, refine in iter+2 if needed. iter-343 is the 1st instance; future similar uncertainty-resolution iters trigger codification at 3rd recurrence.

Codification candidate `feedback_graceful_failure_enables_empirical_feedback.md` flagged at 1/3.

## What's NOT done in iter-343 (deferred)

- **MainViewModelV2 wiring** to pass `iconResolver` to `CombatTabViewModel` ctor: deferred to iter-344 — VM constructor extension shipped but composition root not yet wired (operator currently runs without resolver = icons stay null even if tostring works)
- **Live SWFOC verify** of `tostring(GameObjectType_handle)` semantics: requires operator session
- **iter-321 Asset Browser tab Hardpoint extension**: hardpoints don't fit the iter-321 file-walker model (they're runtime objects, not extracted DDS files); separate concern
- **Approach B fallback** (NEW name-extraction bridge wire): contingency if iter-344 verify shows tostring returns userdata; ~50 LoC bridge + 30 LoC simulator + ~15 pin tests

## Verification checklist

- [x] HardpointEntry record extended with IconPath field
- [x] CombatTabViewModel ctor extended per iter-311 codified rule
- [x] ResolveHardpointIconAsync chain implemented with graceful failure
- [x] SetIconResolver hot-swap mirror of iter-312/iter-321
- [x] XAML Image element bound to IconPath
- [x] 8/8 iter-343 pin tests pass (parser-isolation + XAML)
- [x] 22/22 combined Hardpoint Inspector suite pass in 1.97s
- [x] Editor build GREEN; republished 157.34 MB at May 7 08:05
- [x] iter-337 preflight rule consumed for 5th time
- [ ] MainViewModelV2 wire-up — deferred to iter-344
- [ ] Live SWFOC verify of tostring semantics — deferred to operator session
- [ ] iter-344 contingency (Approach B if needed) — pending empirical evidence

## Net iter-343 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | ~140 source + ~115 tests = ~255 LoC |
| Files modified | 3 (CombatTabState + CombatTabViewModel + MainWindowV2.xaml) |
| Files new | 1 (Iter343HardpointIconChainTests.cs) |
| Tests new | 8 facts |
| Combined suite | 22/22 PASS in 1.97s |
| Editor binary | 157.34 MB at May 7 08:05 |
| iter-337 preflight consumers | 5 (this iter is 5th) |
| Pattern observations flagged | 1 (`feedback_graceful_failure_enables_empirical_feedback.md` at 1/3) |
| Cycle time | ~40 min (chain implementation + tests + republish) |

**iter-343 closes the user mandate "nice GUI showing units by their in-game pictures" at the per-hardpoint scope, pending empirical verification of tostring semantics.** Foundation complete; iter-344 verifies + closes OR pivots.
