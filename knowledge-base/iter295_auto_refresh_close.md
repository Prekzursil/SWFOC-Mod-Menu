# iter-295 — Galactic + HeroLab auto-refresh on construction

**Date:** 2026-05-07
**Arc class:** Mandate-expansion polish iter (cheapest highest-impact)
**Predecessor:** iter-294 (mandate-expansion audit)
**Successor (queued):** iter-296 (real `SWFOC_GetPlanets` impl)

## What changed (2 files, ~16 LoC)

- **`GalacticTabViewModel.cs:135`** — `_ = RefreshPlanetsCore();` fire-and-forget at end of ctor (with 8-line justification comment explaining the mandate + graceful-failure semantics).

- **`HeroLabTabViewModel.cs:79`** — same pattern: `_ = RefreshHeroesCore();` fire-and-forget.

## Verification

```
dotnet build src/SwfocTrainer.App/SwfocTrainer.App.csproj
Build succeeded. 0 Warning(s), 0 Error(s)
```

## Behavior change (operator-visible)

**Before**: opening the Galactic or HeroLab tab showed an empty grid. Operator had to click "Refresh planets" / "Refresh heroes" manually before any data appeared.

**After**: tabs auto-populate when the WPF tab control instantiates the ViewModel (i.e., on first activate / on app start, depending on lazy-loading config). When bridge is connected + game is running, the tab shows live data immediately. When bridge is disconnected, the refresh fails gracefully (the existing empty-state behavior is preserved).

## Why fire-and-forget vs `OnActivated` event hook

The cleanest solution would be a `TabSelected` event from `MainWindowV2.xaml`'s `TabControl`, but:
- Requires modifying the 378K-line `MainWindowV2.xaml`.
- Requires defining an `IActivatable` interface across all 22 ViewModels.
- ~100+ LoC for marginal correctness gain.

Fire-and-forget at ctor is **2 LoC per tab**, idempotent (subsequent manual refresh works), and graceful-failure-safe (bridge errors silently fail the initial fire-and-forget; the user gets the existing manual button as fallback).

iter-296+ can graduate to `OnActivated` hooks if the loop pattern recurs across more tabs.

## What's NOT done in iter-295 (per scope)

- **Empty-state fallback messaging** — deferred. The existing tab UX shows blank grids on empty response. Adding "(bridge disconnected — no data)" text needs ~20 LoC of XAML + ViewModel binding work that's not strictly necessary; the tabs already log errors to the Output panel.
- **`SWFOC_GetPlanets` real impl** — that's the actual data-source fix. Currently the wire returns `count=0` (Phase 1 stub per Audit B). iter-296 implements the real galactic API call.

## Honest scope acknowledgment

iter-295's auto-refresh helps **today** for HeroLab (`SWFOC_ListHeroes` is a real wire, returns live data when bridge connected) and helps **eventually** for Galactic (currently still gets `count=0` until iter-296 ships the real `SWFOC_GetPlanets`).

So the iter-295 fix is necessary-but-not-sufficient: the auto-refresh wire-up is in place; iter-296 makes the data flow.

## Tasks queued

- **iter-296** (next): real `SWFOC_GetPlanets` implementation in `lua_bridge.cpp`. Replace the `count=0` stub with actual galactic-mode planet enumeration. Per agent #2 audit: ~30-50 LoC + may need new "global-list-return" dispatcher helper class. Pin RVA for planet-list pointer first.
- iter-297: L3 stub-XML injection for savegame repair v2.
- iter-298: Save integrity SHA256 guards.
- iter-299: `SWFOC_GetFactionRoster` + `SWFOC_GetCurrentMod` bridge wires.
- iter-300: `SWFOC_ListMods` + Settings mod-picker UI.
- iter-301: Asset/icon extraction kickoff (stretch).

## NEW pattern lesson — "ctor-time fire-and-forget" for tab init

When a WPF tab needs auto-data-load on first activate but adding `IActivatable` infrastructure is out of scope:
- Fire-and-forget at ctor end: `_ = AsyncMethodCore();`
- Idempotent (manual refresh button still works).
- Graceful-failure-safe (errors propagate to existing logging path).
- ~2 LoC per tab.
- Trade-off: fires even if user never visits the tab (wasted work if tab unused). Acceptable for cheap operations.

This is a recurrence of the **iterative-deferral pattern** (iter-287 → iter-292): ship the cheap fix that's 80% of the value, defer the polished `OnActivated` infrastructure to a later iter.

## Verification checklist

- [x] `GalacticTabViewModel` ctor fires `RefreshPlanetsCore()`.
- [x] `HeroLabTabViewModel` ctor fires `RefreshHeroesCore()`.
- [x] Editor builds clean (0 warnings / 0 errors).
- [x] Pattern matches iter-217 PlayerState faction-merge precedent (auto-refresh on init).
- [ ] State docs synced.
- [ ] Task #546 marked completed; iter-296 queued.
