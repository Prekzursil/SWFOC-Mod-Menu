# iter-299 — `SWFOC_GetFactionRoster` + `SWFOC_GetCurrentMod` bridge wires

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (per iter-294 Audit B remaining enumeration wires)
**Predecessor:** iter-298 (SHA256 integrity guards)
**Successor (queued):** iter-300 (`SWFOC_ListMods` + Settings UI mod-picker)

## What changed (3 files extended; ~250 LoC total)

- **`swfoc_lua_bridge/lua_bridge.cpp`** (+~165 LoC) — Two new `Lua_*` functions:
  - `Lua_GetFactionRoster(faction_name)` — DoString-driven filter via `Find_All_Objects_Of_Type` across `{GroundCompany, Hero, SpaceUnit, Infantry, Vehicle}` categories, gated on `Get_Owner():Get_Faction_Name() == requested_faction`. Returns newline-CSV `<unit_type>;<category>` or `(empty)` or `ERR: ...`. Per-unit pcall guards tolerate one bad entry.
  - `Lua_GetCurrentMod()` — Filesystem probe of `./Mods/*/Modinfo.xml` returning the most-recently-accessed candidate. Output: `<mod_name>;<version>\n<absolute_path>` or `vanilla` or `ERR: ...`. Version field is `unknown` (Modinfo.xml parse deferred to iter-300+).
  - Both registered in canonical `RegisterAll(L)` table immediately after `SWFOC_GetPlanets`.
- **`SWFOC editor/.../CapabilityStatusCatalog.cs`** (+~12 LoC) — Two new entries flipped LIVE on first introduction (no Phase2HookPending intermediate; engine-side primitives means "no hook to pend").
- **`SWFOC editor/tests/.../Simulator/SwfocSimulator.cs`** (+~50 LoC) — Two simulator handlers + 3 new `ActiveMod*` fields on `FakeGameState`. Faction is derived from `OwnerSlot → Players[slot].Faction`; category is heuristic from `IsHero` / `IsGround` flags.
- **`SWFOC editor/tests/.../Catalog/Iter299FactionRosterAndCurrentModTests.cs`** (NEW, ~120 LoC) — 6 pin tests exercising both wires through the real pipe + V2BridgeAdapter pattern.

## Why these 2 wires next

iter-294 Audit B identified 6 missing enumeration wires that block the dynamic-loading mandate. iter-296 closed `SWFOC_GetPlanets` (highest priority); iter-299 closes `GetFactionRoster` (operator can list units a faction owns) + `GetCurrentMod` (operator can identify what mod is loaded). Remaining 3: `ListMods`, faction-roster-by-build-tab, hero-roster — queued for iter-300+.

## Pattern — *engine-already-does-this* (5th instance)

iter-100 (SetUnitSpeed via DoString) → iter-107 (Camera via DoString) → iter-179 (TaskForce write-side) → iter-296 (GetPlanets) → **iter-299 GetFactionRoster**. All 5 leverage existing engine-side Lua APIs instead of pinning new RVAs. Marginal cost: ~30-50 LoC vs ~200-500 LoC for an RVA-pin path. Mod-compat free because the engine's Lua API works on whatever mod is loaded.

`GetCurrentMod` breaks the pattern slightly — it's a filesystem probe, not a DoString. Reason: the engine doesn't expose "what mod did I launch with?" via Lua. The next-best primitive (filesystem walk of `./Mods/*/Modinfo.xml` ordered by access time) is host-side, not engine-side. Both wires honest about their mechanism in the catalog rationale.

## Mid-iter API discoveries (5-second-grep before designing)

iter-282 codified the bidirectional infra-claim drift rule. Iter-299 hit 4 instances of consumer-side API surface I had to learn empirically:

1. **`CapabilityStatusEntry.Note`** not `Rationale` — record positional parameter is `Note`. Rewrote 4 assertions.
2. **`CapabilityStatusCatalog.Lookup(name)`** not `Get(name)` — static accessor pattern.
3. **`NamedPipeLuaBridgeClient`** lives in `SwfocTrainer.Core.Services` namespace — not in App.V2.Infrastructure where the adapter sits.
4. **No public `SwfocSimulator.Dispatch()`** — simulator is invoked via `sim.Start()` + pipe-server-only surface; tests connect via `NamedPipeLuaBridgeClient` + `V2BridgeAdapter.SendRawAsync()`.

Each was a 5-second grep that saved ~10 minutes of guessing. **The pattern works**: when adding a new test against existing infra, grep one similar working test first to learn the actual API shape. Recurrence of the iter-282 / iter-296b principle (consumer-side semantic check first).

## Mid-iter bug catch — XML doc comments parsing `<unit_type>` as XML tags

Initial XML doc comments had:
```csharp
/// NEWLINE-separated rows of "<unit_type>;<category>"
```

CS1570 warnings: "End tag 'summary' does not match the start tag 'category'." C# XML doc parser eats anything that looks like `<word>` as an XML element. Fixed by switching to `<c>...</c>` code spans:
```csharp
/// NEWLINE-separated rows of <c>"unit_type;category"</c>
```

Same trap iter-242 hit (and likely many other iters). Worth flagging: **angle brackets in XML doc comments need `<c>` wrapping or HTML entity escaping**, never raw `<...>`. Not codifying as memory rule (well-known C# gotcha) but adding to the C# pin patterns I keep mental track of.

## Verification gates ALL GREEN

```
swfoc_lua_bridge/build.bat:
  === Results: 1100 passed, 0 failed === ALL BUILDS AND TESTS PASSED ===

dotnet build src/SwfocTrainer.App/SwfocTrainer.App.csproj:
  Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test --filter 'FullyQualifiedName~Iter299':
  Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 61 ms

Verifier ledger lint inherits 0/0 at 318 entries unchanged.
```

6 tests passing covers:
- Catalog LIVE status pin (both wires)
- Catalog rationale references "iter 299" + key engine API names
- GetFactionRoster filtering by faction (Rebel-only roster excludes Empire units)
- GetFactionRoster empty-faction sentinel `(empty)`
- GetCurrentMod vanilla sentinel
- GetCurrentMod mod-name + version + path round-trip

## Operator-visible change

**Before iter-299**: editor had no way to ask the bridge "what units does faction X have?" or "what mod is currently loaded?" Operators had to manually inspect the game's Mods folder + remember which units belong to which faction.

**After iter-299**: bridge exposes both as queryable Lua wires. Editor consumers (Spawning tab, Settings tab, Diagnostics tab) can call them via the standard `V2BridgeAdapter.SendRawAsync()` pattern. iter-300 will surface them as native UI buttons.

## What's NOT done in iter-299 (deferred)

- **Modinfo.xml parsing** for real version field — defer to iter-300 (XML parsing in C++ adds dependencies; operators can read the path field today and inspect Modinfo.xml manually).
- **Native UI buttons** for Spawning tab "List Rebel roster" / Settings tab "Active mod: AOTR" — deferred to iter-300+ surfacing arc.
- **Multi-mod support** in GetCurrentMod — currently returns the most-recently-accessed mod. If operator launches with `MODPATH=Mods\A;Mods\B`, only one is returned. Mod stack iter-300 problem.
- **Live SWFOC verify** — requires running game. Build clean + tests pass; live verification deferred to operator's next session.

## Pattern lessons

### Recurrence — *engine-already-does-this* (5th instance, hardening)

5 instances across iter-100/107/179/296/299. **This is now load-bearing pattern across 200+ iter window.** Codification candidate `feedback_engine_already_does_this.md`:
- Rule: when adding bridge wire, check if engine has an existing Lua API first (~30 sec grep against `docs/lua-api.md` + `Find_All_Objects_Of_Type` callsites)
- Why: 6× cheaper than RVA pin path
- Bonus: mod-compat free
- When to break the rule: when no engine API exists (iter-299 GetCurrentMod is a filesystem probe; engine has no "what mod did I launch with" Lua API)

Will codify in iter-300+ when one more instance solidifies the abstraction.

### *5-second-grep before designing* (iter-282 principle, 5th application)

Iter-296 → iter-298 → iter-299 each had 1-4 mid-iter API discoveries that a 5-second grep would've prevented. Cumulative: ~20 minutes of fix-cycles across 3 iters. Still cheaper than not having the rule (would've been ~60+ minutes), but worth getting pre-emptive.

## Tasks queued

- **iter-300** (next): `SWFOC_ListMods` (enumerate ./Mods/* without picking active) + Settings UI mod-picker. Operator can switch mods without leaving the editor. Per Audit B: 4th of 6 missing enumeration wires; ~80-120 LoC bridge + ~50-80 LoC Settings tab UI.
- iter-301+: Asset/icon extraction kickoff (.meg parser + DDS decoder for unit thumbnails per user mandate).

## Verification checklist

- [x] `Lua_GetFactionRoster` ships with DoString-driven faction filter + 5-category fallback chain.
- [x] `Lua_GetCurrentMod` ships with filesystem probe of `./Mods/*/Modinfo.xml`.
- [x] Both registered in `RegisterAll(L)` canonical table.
- [x] CapabilityStatusCatalog has both as Live with iter-299 rationale.
- [x] Simulator handlers + FakeGameState ActiveMod* fields wired.
- [x] 6/6 pin tests pass.
- [x] Bridge harness 1100/0; editor build 0/0; ledger lint 0/0.
- [x] Mid-iter API drifts caught + fixed (Lookup/Note/namespace/Dispatch).
- [x] XML doc comment `<unit_type>` parsing trap caught + fixed (switched to `<c>`).
- [ ] Live SWFOC verify — deferred to operator session.
- [ ] State docs synced.
- [ ] Task #550 marked completed; iter-300 queued.
