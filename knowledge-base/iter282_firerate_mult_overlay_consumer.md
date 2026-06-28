# iter-282 — SWFOC_GetFireRateMultiplierGlobal overlay consumer

**Date:** 2026-05-06
**Arc class:** Thread B Overlay Phase 2-full → post-arc resolution iter (Tier 2 finale)
**Predecessors:** iter-275..279 (Phase 2-full vendoring + ImGui plumbing + Tier 2 partial), iter-281 (Tier 2 damage-mult resolution)
**Successor (queued):** iter-283 (overlay tier-3 polish OR Phase2HookPending re-audit OR new arc kickoff — TBD per loop discretion)

## What changed (3 files, ~30 net LoC)

- **`swfoc_overlay/hud_state.cpp`** — replaced the iter-281 honest-defer comment placeholder for fire-rate (step #6) with a real `BridgeProbe("return SWFOC_GetFireRateMultiplierGlobal()", resp)` call mirroring iter-281's damage probe. `snap.firerate_mult` now populates from the LIVE getter.
- **`swfoc_overlay/overlay.cpp`** — extracted the iter-281 damage_mult render branch into a `renderMultRow(label, mult)` lambda (capture-less). Now invoked twice — once for damage_mult, once for firerate_mult. Footer iter-tag bumped from `"Phase 2-full @ iter 281 (Tier 2 partial)"` to `"Phase 2-full @ iter 282 (Tier 2 complete)"`.
- **`swfoc_overlay/hud_state.h`** — no change this iter. The `firerate_mult` field was added by iter-281 anticipating iter-282; it stayed at `-1.0f` sentinel until the worker probe shipped today.

**No bridge / catalog / simulator additions** — they ALL already existed.

## The discovery — incomplete-investigation drift caught mid-iter

Iter-282's queued task description claimed:
> "iter-225 only shipped setter; iter-282 adds the getter pair"

This was **wrong**. A 5-second `grep -n 'GetFireRateMultiplierGlobal' lua_bridge.cpp` would have shown:

| Line | Symbol | Status |
|------|--------|--------|
| 6794 | `static int Lua_GetFireRateMultiplierGlobal(lua_State* L)` | DEFINED |
| 7616 | `{"SWFOC_GetFireRateMultiplierGlobal", Lua_GetFireRateMultiplierGlobal}` | REGISTERED |

And in the editor:

| File | Line | Status |
|------|------|--------|
| `CapabilityStatusCatalog.cs` | 160-162 | CATALOGUED `Live` |
| `Simulator/SwfocSimulator.cs` | 126 | HANDLER REGISTERED |

Iter-281's honest-defer was based on incomplete investigation: it read iter-225's setter doc but didn't verify the getter's absence in the bridge code itself. Iter-282 collapses from "~50 LoC bridge + 4-target editor wiring" to "~30 LoC overlay consumer addition" — a **90% scope reduction**.

## Pattern lesson — bidirectional infrastructure-claim drift

Iter-256 codified `feedback_aob_drift_across_binary_versions` for the case **"claims of present infrastructure may be wrong"** (the iter-249 SetUnitCapOverride AOB drifted across binary versions).

Iter-282 surfaces the **mirror** case: **"claims of missing infrastructure may also be wrong."** A queued task may premise on missing infra that actually already exists from a prior arc's pair-completion pass (iter-225's bridge wire pre-existed because some unrecorded prior iter — likely the same conversation that shipped the setter — also pair-shipped the getter, but the queued task description didn't reflect that).

**Codification rule (NEW, iter-282):** before writing "add the X function" code, run a 5-second grep to verify X doesn't already exist. Cheap to verify, expensive to assume. The cost ratio mirrors iter-256's: a 5-second grep prevented ~50 LoC of duplicated work + a guaranteed merge conflict against the existing function definition.

The delta vs iter-256:

| Aspect | iter-256 (AOB drift) | iter-282 (claim drift) |
|---|---|---|
| Direction | "Address resolves to wrong function" | "Function already exists when claimed missing" |
| Cause | Binary-version asymptote across Steam updates | Stale task-queue description vs current-state codebase |
| Mitigation | Semantic verification before designing arc | Mid-iter `grep` before writing addition code |
| Cost-of-skip | ~5 iters of misdirected RE work (iter-105 → iter-128 SetUnitShield) | ~50 LoC of duplicate code + merge conflict |
| Frequency | Once per ~150 iters (long binary cycle) | Hits at conversation handoffs (high frequency) |

Both directions deserve memory-rule codification. The iter-282 lesson should land as `feedback_infra_claim_drift_bidirectional.md` in the next memory-write iter.

## Tier 2 row group — fully resolved

The HUD's Tier 2 multiplier row is now complete:

```
─────────────────────  (separator above Tier 2)
Damage mult: 2.00x      ← amber when scaled, gray when neutral, "probe pending" when sentinel
Fire-rate mult: 1.50x   ← same ladder
─────────────────────  (separator below Tier 2)
F1 toggles | Phase 2-full @ iter 282 (Tier 2 complete)
```

Both rows share the `renderMultRow(label, mult)` lambda — the second-instance abstraction trigger from `feedback_pattern_extraction_threshold` (iter-?? if codified) lands here cleanly.

## Build verification

```
cmd.exe /c "cd /d C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_overlay && .\build.bat"

[1/4] Compiling MinHook (shared with swfoc_lua_bridge)...
[2/4] Compiling overlay sources...
[3/4] Compiling ImGui v1.91.5 (vendored Phase 2-full)...
[4/4] Linking swfoc_overlay.dll...

=== OVERLAY BUILD SUCCESS ===
 swfoc_overlay.dll: 1,038,848 bytes (+512 B vs iter-281)
```

`grep -iE 'error|warning|undefined'` on full build output: **zero matches** — clean compile + link.

Binary delta: +512 B for ~30 LoC additional logic (lambda body + new probe call). Well within the iter-275 design budget.

## Tasks queued

- **iter-283** — TBD per loop discretion. Candidates:
  1. Tier 3 overlay content (e.g., paired counter readouts: kill/death tally, current-event ring, scenario timer)
  2. Phase2HookPending re-audit (6th audit, ~9-iter cadence since iter-274; next canonical due ~iter-283..285)
  3. New A1.x arc kickoff (per the iter-269 ledger-state asymptote signal saying NEW arcs land deferred at 37.5% rate)
  4. Codify `feedback_infra_claim_drift_bidirectional.md` (iter-282 NEW pattern lesson — mirror to iter-256's pattern)
  5. Lua Playground preset menu refresh (last ran iter-264; iter 257-282 wires unsurfaced)

Lean toward **Option 4** (codify the iter-282 lesson) since it's the cheapest persistence + the lesson is fresh. **Option 2** (audit) is also strong as a cadence-driven pick. Loop operator picks.

## Verification checklist

- [x] Bridge wire confirmed pre-existing: `lua_bridge.cpp:6794` (definition) + `:7616` (registration).
- [x] Catalog entry confirmed pre-existing: `CapabilityStatusCatalog.cs:160-162` (Live + iter-225 rationale).
- [x] Simulator handler confirmed pre-existing: `SwfocSimulator.cs:126`.
- [x] Worker probe step #6 wired in `hud_state.cpp` lines 191-204.
- [x] Render branch consumes `firerate_mult` via `renderMultRow` lambda in `overlay.cpp` lines 478-503.
- [x] Footer iter-tag bumped to "Tier 2 complete".
- [x] Build green: 0 errors / 0 warnings, +512 B binary delta.
- [ ] Live game verify (deferred — operator can verify by enabling iter-225 fire-rate setter then watching HUD row update).
- [ ] State docs synced (.remember/now.md, .remember/ralph_loop_state.md, STATUS.md).
- [ ] Task #532 marked completed; iter-283 queued.
