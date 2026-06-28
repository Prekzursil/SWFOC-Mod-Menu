# iter-300 — `SWFOC_ListMods` bridge wire (300th-iter milestone)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion + 300th-iter milestone
**Predecessor:** iter-299 (GetFactionRoster + GetCurrentMod)
**Successor (queued):** iter-301 (Settings tab UI mod-picker consumer)

## What changed (3 files extended; ~190 LoC total)

- **`swfoc_lua_bridge/lua_bridge.cpp`** (+~110 LoC) — New `Lua_ListMods()` enumerates all `./Mods/*` candidate folders containing `Modinfo.xml`. Returns newline-CSV `<mod_name>;<absolute_path>` rows, or `(no_mods)` sentinel, or `ERR: ...`. Mirrors iter-299 `Lua_GetCurrentMod` filesystem walk shape but emits ALL matches instead of picking the most-recently-accessed. Result buffer capped at 16KB (silently truncates beyond ~100 mods, logged for operator awareness). Registered in canonical `RegisterAll(L)` table immediately after `SWFOC_GetCurrentMod`.

- **`SWFOC editor/.../CapabilityStatusCatalog.cs`** (+~10 LoC) — `SWFOC_ListMods` flipped Live on first introduction (no Phase2HookPending intermediate) with iter-300 rationale.

- **`SWFOC editor/tests/.../Simulator/{SwfocSimulator,FakeGameState}.cs`** (+~30 LoC) — Simulator handler `HandleListMods` reads from new `FakeGameState.AvailableMods` field (`List<(Name, Path)>`). Returns matching newline-CSV or `(no_mods)` sentinel.

- **`SWFOC editor/tests/.../Catalog/Iter300ListModsTests.cs`** (NEW, ~95 LoC) — 4 pin tests covering catalog Live status, empty-list sentinel, multi-mod enumeration, and **cross-iteration ListMods ↔ GetCurrentMod consistency check** (iter-299 active mod must appear in iter-300 enumeration).

## Why ListMods now (300th-iter milestone scope discipline)

iter-294 Audit B identified 6 missing enumeration wires. iter-296 closed `GetPlanets` (#1), iter-299 closed `GetFactionRoster` + `GetCurrentMod` (#2-3). iter-300 closes `ListMods` (#4) — **4 of 6 enumeration wires now LIVE**. Remaining 2 (faction-roster-by-build-tab + hero-roster) deferred until empirical evidence shows operators need them; the 4 shipped wires cover the dynamic-loading mandate's primary asks (mod identification + faction listing + planet enumeration).

**Settings tab UI consumer DEFERRED** to iter-301 by design. The bridge wire is the high-value piece; the UI extension is a 80-120 LoC editor change that benefits from a focused dedicated iter (DataGrid binding, Refresh command, "Open Mods folder" button via `Process.Start("explorer.exe", path)`, cross-ref with iter-299 GetCurrentMod for "currently loaded" badge). Keeping iter-300 surgical-scope.

## Verification gates ALL GREEN

```
swfoc_lua_bridge/build.bat:
  === Results: 1100 passed, 0 failed === ALL BUILDS AND TESTS PASSED ===

dotnet test --filter 'FullyQualifiedName~Iter300':
  Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 21 ms

Verifier ledger lint inherits 0/0 at 318 entries unchanged.
```

4 tests cover:
- Catalog LIVE status pin + iter-300 rationale
- Empty AvailableMods → `(no_mods)` sentinel
- 3-mod enumeration → 3 newline rows in correct order
- **Cross-iter consistency**: iter-299 GetCurrentMod's active mod must appear in iter-300 ListMods enumeration

## Cross-iteration consistency test — *the kind of test that surfaces real bugs*

Test #4 verifies an invariant that crosses bridge wires: **the mod that GetCurrentMod identifies as "loaded" must be one of the mods ListMods enumerates**. If a future change breaks the cross-reference (e.g., GetCurrentMod returns a name that ListMods doesn't include), the test fails immediately. This is the kind of test that catches regressions ListMods-alone or GetCurrentMod-alone tests would miss.

Pattern lesson: **when adding a new wire that semantically relates to an existing wire, write at least ONE test that exercises both together**. The cost is ~10 LoC; the benefit is regression coverage for the relationship between the wires, not just each wire in isolation.

## Pattern lessons

### *Engine-already-does-this* — 6th instance, codification trigger reached

iter-100 / iter-107 / iter-179 / iter-296 / iter-299 / **iter-300**. 6 instances now. iter-300 reuses iter-299's filesystem-walk pattern (mirroring `GetCurrentMod`'s shape but enumerating instead of picking). The pattern abstraction holds:

- **Default first-attempt for any new bridge wire**: leverage the cheapest existing mechanism (engine Lua API via DoString OR filesystem probe via Win32 API)
- **Cost**: ~30-100 LoC vs ~200-500 LoC for an RVA-pin path
- **Mod-compat**: free (DoString approach) or trivially scoped (filesystem approach respects whatever folder layout the operator has)
- **Honest break-out clause**: when no cheap mechanism exists (e.g., engine-internal state with no Lua exposure), then RVA pin is justified — but only after confirming no alternative

**iter-301 will codify** as `feedback_engine_already_does_this.md` memory rule. 6 instances across ~200 iters is enough recurrence to abstract.

### *Sidecar-additive* — 4th instance (iter-292/297/298/300)

iter-292 (save-side copy) + iter-297 (mod-side addition) + iter-298 (verification overlay) + **iter-300 (read-only enumeration)**. 4 instances. ListMods is read-only, but it fits the broader principle: **never mutate operator data; always emit additive results** (the bridge call returns new data, never modifies the disk). Ready for codification at iter-302+ when the next mutating-vs-non-mutating distinction needs to be made.

### *5-second-grep* — 6th application, 0 mid-iter API drifts caught

iter-296 had 1 wire-format drift; iter-298 had 1 encoding-default trap; iter-299 had 4 API name drifts. **Iter-300: 0 mid-iter API drifts caught** because every API used (`Lookup`, `Note`, `NamedPipeLuaBridgeClient`, `V2BridgeAdapter.SendRawAsync`) was learned from iter-299's pin tests just hours earlier. **The principle pays compound interest**: each grep teaches you APIs that the next iter reuses for free.

## 300-iter milestone tally (master loop iter 100-300, bridge surface)

- **143 LIVE bridge wires** (was 142 at iter-299; iter-300 +1 ListMods)
- **12 dispatcher helpers** (8-helper matrix complete iter-178 + 4 multi-arg extensions iter-182/184/186)
- **24 Phase2HookPending entries** (5 audit passes; 60% drift rate within ledger-surface candidates)
- **318 verified ledger entries** (up from 313 at session start)
- **109+ native UX buttons** across 21+ V2 tabs (iter-117 through iter-219 surfacing arc, capped)
- **Bridge harness 1100/0** continuously since iter-225 (75 iters of zero-regression)
- **Editor 0 warnings / 0 errors** continuously since iter-261
- **6 missing enumeration wires from iter-294 Audit B**: 4 closed (iter-296/299/300), 2 remain
- **3 sidecar-additive savegame tools** shipped (iter-292 strip-fix + iter-297 stub injection + iter-298 integrity guards)
- **Thread B Overlay Phase 2-full COMPLETE** (iter-275-285; ImGui v1.91.5 vendored + 4-row Tier 1+2+3 HUD + 11 LIVE backing wires)
- **Thread C savegame editor FUNCTIONALLY COMPLETE via CLI** (iter-286-292; 9 tools at `tools/savegame_parser/`)

## What's NOT done in iter-300 (deferred)

- **Settings tab UI mod-picker** — iter-301. ~80-120 LoC C# + XAML for DataGrid + Refresh + Open-folder buttons + currently-loaded badge.
- **Modinfo.xml version parsing** — iter-302+. Requires C++ XML parser dependency or pivot to filesystem-side parsing in C#.
- **Multi-mod stack support** in GetCurrentMod — when operator launches with `MODPATH=Mods\A;Mods\B`. Iter-303+ if needed.
- **Live SWFOC verify** — deferred to operator's next session (build clean + tests pass).

## Tasks queued

- **iter-301** (next): Settings tab UI mod-picker. Consumes both iter-299 GetCurrentMod and iter-300 ListMods. Operator-facing surfacing iter — "Available Mods" GroupBox + DataGrid + Refresh button + "Open Mods folder" + currently-loaded badge.
- **iter-302** (queued): Codify `feedback_engine_already_does_this.md` memory rule (6th instance trigger reached at iter-300).
- iter-303+: Asset/icon extraction kickoff (.meg parser + DDS decoder per user mandate).

## Verification checklist

- [x] `Lua_ListMods` ships with 16KB-capped buffer + per-folder `Modinfo.xml` validation.
- [x] Registered in `RegisterAll(L)` canonical table after `SWFOC_GetCurrentMod`.
- [x] Catalog Live with iter-300 rationale + sibling-to-iter-299 cross-reference.
- [x] Simulator handler reads from `FakeGameState.AvailableMods` list.
- [x] 4/4 pin tests pass (catalog + sentinel + multi-mod + cross-iter consistency).
- [x] Bridge harness 1100/0; editor build clean; ledger lint 0/0.
- [x] **300th-iter milestone tally documented**.
- [x] No mid-iter API drifts (iter-299 grep paid forward 0-drift dividend).
- [ ] Settings tab UI consumer — deferred iter-301.
- [ ] Live SWFOC verify — deferred to operator session.
- [ ] State docs synced.
- [ ] Task #551 marked completed; iter-301 queued.
