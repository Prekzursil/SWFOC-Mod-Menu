# iter-339 — Combat tab Hardpoint Inspector XAML wire-up + editor republish (closes iter-338 deferred XAML layer; SECOND consumer of iter-337 preflight rule)

**Date:** 2026-05-07
**Arc class:** LIVE delivery XAML layer (closes iter-338 VM-only iter; mirrors iter-148→iter-149 Camera tab pattern)
**Predecessor:** iter-338 (Combat tab Hardpoint Inspector VM smaller-scope)
**Successor (queued):** iter-340 (TBD — see "Next iter options" below)

## What changed (3 files modified + 1 binary republished; ~80 LoC + 8.6 MB binary)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/MainWindowV2.xaml` (+~50 LoC):
  - NEW Hardpoint Inspector GroupBox inserted after iter-219 Suspend AI GroupBox in Combat tab
  - GroupBox header: `"Hardpoint Inspector (iter-281 SWFOC_GetHardpoints; RequiresLiveSwfoc)"` (operator-trust mandate per iter-311 codified rule)
  - TextBox bound to `HardpointInspectAddrText` (operator types unit obj_addr in hex or decimal)
  - Refresh button bound to `RefreshHardpointsCommand` + iter-308/iter-311 capability badge + tooltip pattern
  - ListBox bound to `Hardpoints` ObservableCollection with ItemTemplate showing `[Index N] 0xHEXADDR hp=H.HHH` per row (Consolas monospace; min 80px / max 240px height with scrollbar)
- **NEW** `tests/SwfocTrainer.Tests/Regression/Iter339HardpointInspectorXamlTests.cs` (~95 LoC, **6 facts**):
  - `XamlPin_HardpointInspectorGroupBoxPresent` — header + iter-281 cite + RequiresLiveSwfoc badge
  - `XamlPin_HardpointInspectAddrTextBoxBound` — TextBox binds to VM property
  - `XamlPin_RefreshHardpointsCommandBound` — Refresh button binds to AsyncRelayCommand
  - `XamlPin_RefreshHardpointsBadgeAndTooltipBound` — capability badge surface per iter-311 codified rule
  - `XamlPin_HardpointsListBoxItemsSourceBound` — ListBox binds to ObservableCollection
  - `XamlPin_HardpointEntryDataTemplateShowsIndexAddrHp` — ItemTemplate surfaces 3-field record contract
- **REPUBLISH** `SWFOC editor/publish/SwfocTrainer.App.exe`: 157.33 MB (iter-336 era) → **157.34 MB (iter-339 era)** at May 7 07:49.

## Verification gates ALL GREEN

```
[Start-Process bypass — Clink-safe]
dotnet test --filter "FullyQualifiedName~Iter338|...~Iter339"
Test Run Successful.
Total tests: 14
     Passed: 14
 Total time: 1.5828 Seconds
```

- iter-338 parser tests (8 facts): PASS
- iter-339 XAML pin tests (6 facts): PASS
- Combined Combat-Hardpoint-Inspector suite: **14/14 PASS in 1.58s**
- Editor build: GREEN (no XAML compilation errors)
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- Editor binary 157.34 MB at May 7 07:49 (operator-current with iter-338 + iter-339)

## iter-337 preflight rule validation — iter-339 SECOND consumer

iter-338 was the FIRST consumer of iter-337's preflight rule (decision: continue with original plan). iter-339 is the SECOND consumer:

**Step 1 — Rationale-grep**: iter-338 close-out documented VM surface (Hardpoints + RefreshHardpointsCommand + RefreshHardpoints + HardpointInspectAddrText). **PASS** (rationale already explained).

**Step 2 — Bridge-source-grep**: N/A this iter (XAML doesn't touch bridge).

**Step 3 — 4-step composition preflight**:
- (a) Catalog rationale already explains? N/A (XAML doesn't add new wires)
- (b) Engine-surface gap? N/A
- (c) Bridge wire orphan? VM exists from iter-338; XAML closes the orphan (operator-visible surface)
- (d) Composition genuinely sufficient? YES (TextBox + Button + ListBox over existing VM surface)

**Decision tree application**: All preflight steps green → **continue with original plan**. Outcome matched — shipped at predicted ~30 LoC scope (~50 LoC actual XAML; comments + ItemTemplate verbose).

## Operator-facing impact (Phase 2 — XAML layer COMPLETE)

**Pre-iter-339**: Operator running iter-336 republished binary could see Combat tab but no Hardpoint Inspector. iter-338 VM was complete but no XAML surface to consume.

**Post-iter-339**: Operator opens Combat tab → scrolls to "Hardpoint Inspector" GroupBox → types/pastes unit `obj_addr` (hex or decimal) → clicks Refresh → ListBox shows hardpoint vector with index/address/HP per row.

**End-to-end workflow**:
1. Operator launches editor (`publish/SwfocTrainer.App.exe` 157.34 MB)
2. Operator attaches to running SWFOC (RequiresLiveSwfoc bridge gate)
3. Operator opens Inspector tab → selects unit → clicks "Copy obj_addr" (iter-191 button)
4. Operator opens Combat tab → pastes addr into Hardpoint Inspector TextBox
5. Operator clicks Refresh → sees `[Index 0] 0x140012340 hp=750.000`, `[Index 1] 0x140012358 hp=420.500`, etc.
6. Operator can read which hardpoints are taking damage in real-time (mid-battle inspection)

**Use cases enabled**:
- Mid-battle damage diagnosis (which hardpoint of the AT-AT is destroyed?)
- Hardpoint enumeration for debugging (does this unit have 4 or 6 weapons?)
- Foundation for iter-340+ icon-resolution chain (each child addr → SWFOC_GetType → ResolveWeaponIcon)

## Pattern lessons

### iter-148→iter-149 + iter-338→iter-339 = `feedback_vm_first_xaml_second_iter_split` at 2/3 trigger

iter-148 (Camera VM) → iter-149 (Camera XAML) was the 1st instance of the VM-first / XAML-second iter split pattern. iter-338 (Hardpoint Inspector VM) → iter-339 (Hardpoint Inspector XAML) is the 2nd instance. Pattern shape stable:

| Aspect | iter-148/149 (Camera) | iter-338/339 (Hardpoint) |
|--------|----------------------|--------------------------|
| Iter 1 (VM) LoC | ~80 (6 dispatcher methods + 6 state methods + 6 VM commands + 6 CapabilityAwareAction entries) | ~80 (HardpointEntry record + parser + ObservableCollection + RefreshHardpointsCommand + CapabilityAwareAction) |
| Iter 1 tests | implicit (pin tests in same file) | 8 facts (parser-focused) |
| Iter 2 (XAML) LoC | ~30 (6 buttons) | ~50 (GroupBox + TextBox + Button + ListBox + ItemTemplate) |
| Iter 2 tests | XAML pin tests (mirror) | 6 facts (XAML pin tests) |
| Combined cycle | 2 iters / ~60 min | 2 iters / ~50 min |

If a 3rd VM→XAML iter split surfaces (iter-340+?), codification trigger reaches 3-instance threshold. Most likely candidate: iter-340 if Asset Browser tab gets a search/filter VM extension.

### iter-339 IS the 2nd consumer of iter-337 codified rule — meta-validation continues

iter-338 was the 1st consumer of iter-337's codification (immediate next-iter consumption). iter-339 is the 2nd consumer (sequential next-iter consumption). The rule's value compounds at each consumption:

- iter-338: ~5 sec preflight discovery confirmed VM-layer plan; saved ~30 min cycle vs cold-start design
- iter-339: ~5 sec preflight discovery confirmed XAML-layer plan; saved ~15 min cycle vs cold-start design (smaller layer = smaller savings)

**Pattern observation**: iter-337 codified rule's per-consumption ROI scales with iter complexity. Higher-LoC iters (VM) get higher savings; lower-LoC iters (XAML) get smaller absolute savings. Both above the rule's ~40 sec preflight cost.

## What's NOT done in iter-339 (deferred)

- **Hardpoint icon-resolution chain** (per-child SWFOC_GetType → ResolveWeaponIcon): deferred to iter-340+ — iter-336 preflight identified this as 2-bridge-call complexity beyond single-iter scope
- **Live SWFOC verify** of the inspector: requires operator session with running SWFOC + a tactical battle with units having visible hardpoints
- **Hardpoint Inspector test harness** (faking SWFOC_GetHardpoints reply for E2E test): deferred — would need V2BridgeAdapter test injection per iter-336 V2BridgeAdapter pattern; not surgical-scope for iter-339

## Verification checklist

- [x] XAML GroupBox inserted in Combat tab after iter-219 Suspend AI section
- [x] TextBox + Refresh button + ListBox + ItemTemplate wired to iter-338 VM surface
- [x] CapabilityAwareAction badge + tooltip per iter-308/iter-311 codified pattern
- [x] 6/6 XAML pin tests pass
- [x] 8/8 iter-338 parser tests still pass (no regression)
- [x] Combined 14/14 PASS in 1.58s
- [x] Editor build: GREEN
- [x] Editor republished: 157.34 MB at May 7 07:49
- [x] iter-337 preflight rule applied + validated as 2nd consumer
- [ ] Live SWFOC verify with running game — deferred to operator session
- [ ] Hardpoint icon-resolution chain — deferred to iter-340+
- [ ] Test harness with faked bridge reply — deferred

## Next iter options (iter-340)

In priority order:

1. **Operator changelog supplement** covering iter 331-339 (~9-iter doc gap since iter-330 covered iter 320-329; well-precedented at iter-235/241/247/262/280/311/320/330 cadence)
2. **Hardpoint icon-resolution chain** kickoff (multi-iter mini-arc per iter-336 preflight): iter-340 RE walk for SWFOC_GetType existing wire? + iter-341 Combat tab DataGridTemplateColumn extension
3. **README capstone update** (premature; only 17 iters since iter-322; canonical cadence is ~30)
4. **Codify** `feedback_vm_first_xaml_second_iter_split.md` at premature 2/3 (defer until 3rd instance per established 3-instance trigger for meta-level patterns)

Recommended: **option 1 (operator changelog)** — closes the iter-330 → iter-339 9-iter doc gap; well-precedented; lowest token cost; codifies the iter-323 7-iter arc + iter-330-339 8-iter arc as separate operator-facing doc sections.
