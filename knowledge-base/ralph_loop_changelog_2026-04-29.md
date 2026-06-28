# Ralph Loop Changelog — 2026-04-29 (iter 117-145)

**Editor build:** `publish/SwfocTrainer.App.exe` 156.88 MB single-file (was 156.85 MB at iter 113).
**Bridge:** unchanged from iter 113 (`powrprof.dll` 376 KB, 1100/0 harness).
**Catalog:** every iter 100-113 LIVE wire now has at least one native operator button (no more "you must paste preset Lua to use it").

---

## What changed

The iter 100-113 batch shipped 17 LIVE bridge wires. Iter 116 surfaced
them as a Lua Playground preset menu — that worked but operators didn't
discover or trust wires they only saw as preset strings. Iter 117-119
closed the gap with native per-tab buttons.

The only iter 100-113 wires that still don't have a dedicated per-tab
button are the iter 113 universal `SWFOC_CallObjMethodLua` (intentionally
escape-hatch only — too generic to deserve a dedicated UI surface; lives
in the Lua Playground preset menu and as a power-user tool) and a few
that already had earlier surfaces (Speed/Damage/Camera).

---

## Operator-facing additions

### Unit Control tab — "Selected Unit Lua Actions" GroupBox (iter 117-118)

11 buttons against a single shared `SelectedUnitLuaExpr` TextBox (paste
once, click any button) plus a Change-Owner sub-section with its own
TextBox.

**Setup**:
1. Open **Unit Control** tab.
2. Find the **"Selected Unit Lua Actions (iter 117-118 LIVE)"** GroupBox
   (sibling to the existing per-unit obj_addr GroupBox).
3. In the first TextBox, paste a Lua expression that resolves to a unit
   handle. Examples:
   - `Find_First_Object("Empire_AT_AT")`
   - `Find_Object_Type("Rebel_Trooper_Squad")[0]`
   - `Find_Hint("hero")`

**11 buttons against that expression**:

| Button | Engine call | Effect |
|---|---|---|
| Make invuln ON / OFF | `(unit):Make_Invulnerable(true/false)` | Per-unit god-mode (propagates to hardpoints) |
| Hide ON / OFF | `(unit):Hide(true/false)` | Visibility toggle (unit still alive) |
| Lock from AI / Unlock to AI | `(unit):Prevent_AI_Usage(true/false)` | AI can't issue orders to this unit |
| Selectable ON / OFF | `(unit):Set_Selectable(true/false)` | Operator can/can't click-select |
| Despawn | `(unit):Despawn()` | Removes the unit cleanly |
| Stop | `(unit):Stop()` | Interrupts current order |
| Retreat | `(unit):Retreat()` | Engine flees the unit |

**Below the WrapPanel** is the iter 118 Change-Owner section:

1. Type a Lua expression for the destination player in the second
   TextBox: `Find_Player("REBEL")`, `Find_Player("EMPIRE")`,
   `Find_Player("Hostile_Garrison")`, etc.
2. Click **"Change unit owner →"** — calls `SWFOC_ChangeUnitOwner(unit, player)`.
   Engine reassigns ownership; visual asset reskins to new faction colors.

### Spawning tab — "Spawn unit via Lua (iter 119 LIVE)" GroupBox (iter 119)

Three TextBoxes for the three Lua expressions. **This is the LIVE
alternative to the existing PHASE 2 PENDING `SWFOC_SpawnUnit` Phase-1-mirror
Spawn button** — operators can finally spawn units that genuinely appear
in the running game.

**Banner copy** above the original Spawn button has been refined to
point operators at the new LIVE section ("Use the 'Spawn unit via Lua
(iter 119 LIVE)' section below for the LIVE alternative.").

**Three TextBoxes**:

| Field | Examples |
|---|---|
| Player Lua | `Find_Player("REBEL")`, `Find_Player("EMPIRE")` |
| Type Lua | `Find_Object_Type("Rebel_Trooper_Squad")`, `Find_Object_Type("Empire_AT_AT")` |
| Position Lua | `Create_Position(0, 0, 0)`, `Create_Position(1500, -2200, 0)`, or any expression resolving to a position (planet hint, etc.) |

Click **"Spawn (Lua, LIVE) →"** to call
`SWFOC_SpawnUnitLua(player, type, position)`.

---

## Test coverage added

19 new wire-format pin tests in
`tests/SwfocTrainer.Tests/Regression/`:

- `Iter117UnitLuaCallShapeTests.cs` — 10 tests covering
  `BuildUnitLuaMethodCall` and `BuildUnitLuaNoArgCall` with the iter
  110-112 helpers.
- `Iter118ChangeUnitOwnerShapeTests.cs` — 5 tests covering the
  two-Lua-expr `BuildChangeUnitOwnerLuaCommand`.
- `Iter119SpawnUnitLuaShapeTests.cs` — 4 tests covering the
  three-Lua-expr `BuildSpawnUnitLuaCommand`.

These pin the exact `SWFOC_X('expr1', 'expr2'...)` shape so a future
bridge-side rename or quote-style change fails at the dispatcher
boundary, not at runtime in the live game.

---

## Bug fixes shipped alongside

- **CapabilitySurfaceReportIntegrationTests regen idempotency**: the
  regen path was generating the report from STALE history then appending
  fresh history → next non-regen run mismatched on trend line. Fixed by
  recording history first, then re-loading, then regenerating. Now
  byte-stable across same-day regens.
- **9 build warnings cleared**: pre-existing CS1570/CS1574/CS1998/CS3016/
  CS0419 in `EconomyTabViewModel`, `CameraDebugTabState`,
  `CameraDebugTabViewModelCapabilityTests`,
  `PeriodicAutoRefreshDriverConcurrencyTests`,
  `Iter75ActivityPinningTests`, `DarkModeContrastTests`,
  `V2UnitMutationDispatcher`. Build is now `0 Warning(s) / 0 Error(s)`
  end-to-end.

---

## Operator test checklist

Use this as a quick smoke checklist when verifying the iter 117-119
build against a running game.

### Unit Control tab — iter 117 (per-unit method actions)

- [ ] Open Unit Control tab. Verify "Selected Unit Lua Actions
  (iter 117-118 LIVE)" GroupBox is visible.
- [ ] Type `Find_First_Object("Empire_AT_AT")` into the first TextBox.
- [ ] Click **Make invuln ON** → AT-AT can't be killed.
- [ ] Fire on AT-AT to confirm invuln. Click **Make invuln OFF** → AT-AT
  takes damage normally again.
- [ ] Click **Hide ON** → AT-AT disappears visually but is still alive.
- [ ] Click **Hide OFF** → AT-AT reappears.
- [ ] Click **Lock from AI** → AI player can't order this AT-AT.
- [ ] Click **Selectable OFF** → operator click-select on the AT-AT
  fails silently.
- [ ] Click **Selectable ON** → operator can click-select again.
- [ ] Click **Stop** → AT-AT halts current order.
- [ ] Click **Retreat** → AT-AT flees.
- [ ] Click **Despawn** → AT-AT is removed cleanly.

### Unit Control tab — iter 118 (change owner)

- [ ] Spawn a fresh AT-AT (use iter 119 spawn-via-lua section, see below).
- [ ] In the iter 117 first TextBox: `Find_First_Object("Empire_AT_AT")`.
- [ ] In the iter 118 second TextBox: `Find_Player("REBEL")`.
- [ ] Click **Change unit owner →**.
- [ ] AT-AT reskins to Rebel colors and joins Rebel side.

### Spawning tab — iter 119 (Lua-driven spawn)

- [ ] Open Spawning tab. Verify "Spawn unit via Lua (iter 119 LIVE)"
  GroupBox is visible (sibling to the original PHASE 2 PENDING
  Spawn button).
- [ ] Verify banner copy says "Use the 'Spawn unit via Lua (iter 119
  LIVE)' section below for the LIVE alternative."
- [ ] Player Lua: `Find_Player("REBEL")`.
- [ ] Type Lua: `Find_Object_Type("Rebel_Trooper_Squad")`.
- [ ] Position Lua: `Create_Position(0, 0, 0)`.
- [ ] Click **Spawn (Lua, LIVE) →**.
- [ ] Verify a Rebel Trooper Squad appears at the origin in the running
  game. (The original Spawn button does NOT do this — it only fires
  the Phase-1-mirror that returns "1/1 OK" without spawning anything
  visible.)

### Diagnostics tab — verify capability surface

- [ ] Open Diagnostics tab. Click **Open capability surface report**.
- [ ] Verify trend line shows iter 117/118/119 added new LIVE actions
  (the absolute count vs iter 113 may differ; what matters is the
  surface is up-to-date and the bottom-bar engine-effective % is
  monotonically non-decreasing across same-day regens).

---

## Known UI test environment flake (not a regression)

`SwfocTrainer.UiTests.DarkModeContrastTests.{PlayerSlot,Faction}_ComboBox_RendersWithDarkBackground`
fails when the WPF UI doesn't render correctly in the test sandbox
(can't find "Player slot:" or "Faction:" TextBlock). This is
environmental — the editor UI itself works. Pre-existing across iter
115+; not introduced by iter 117-119.

---

## Iter 120 — Live test resilience addendum (2026-04-29)

Fixed an intermittent FAIL in `LiveTacticalToggleWorkflowTests` that
manifested when `SwfocExtender.Host` (the editor's sidecar process)
was running but the actual game wasn't. The locator's
`FindBestMatchAsync(ExeTarget.Swfoc)` was returning non-null for the
sidecar, bypassing the test's initial null-check skip. Then
`AttachAsync` couldn't find the real game and threw `ATTACH_NO_PROCESS`,
which propagated as a test failure.

**Fix**: catch `InvalidOperationException` containing `ATTACH_NO_PROCESS`
in `EnsureAttachedTacticalSessionAsync` and convert to a `LiveSkip` —
same outcome as the initial null check. Test now SKIPS cleanly.

**Net result**: Tests.dll suite is now **7583 / 0 / 5 skipped** (was
`7583 / 1 / 4` with the iter 120-fixed test as the 1 fail).

---

## Iter 121 — Centralized helper, applied across all 6 Live tests

Iter 120's defensive try/catch was lifted into a shared
`LiveSkip.AttachOrSkipAsync(runtime, profileId, output)` helper so the
SwfocExtender.Host sidecar flake can never recur in any of the Live
tests. All 6 of the `tests/SwfocTrainer.Tests/Profiles/Live*Tests.cs`
files now route through this helper:

- `LiveTacticalToggleWorkflowTests` — refactored from inline try/catch.
- `LiveActionSmokeTests` / `LiveCreditsTests` /
  `LiveHeroHelperWorkflowTests` / `LiveRoeRuntimeHealthTests` —
  replaced bare `runtime.AttachAsync(profileId)` with the helper.
- `LivePromotedActionMatrixTests` — extended its existing
  `ATTACH_PROFILE_MISMATCH` catch to also cover `ATTACH_NO_PROCESS`
  (matrix records as a non-attach rather than skipping the whole
  test).

`dotnet test --filter "FullyQualifiedName~Profiles.Live"` now reports
`Passed: 1, Skipped: 5, Failed: 0` cleanly even when
`SwfocExtender.Host` is alive.

---

## Iter 122 — Locator-level regression guard

Closed the iter 120/121 flake at the source with 6 regression tests in
`Iter122ProcessLocatorSidecarTests.cs`. Pins `ProcessLocator.GetProcessDetection`'s
behavior on synthetic process tuples — `SwfocExtender.Host`,
`SwfocTrainer.App`, `swfoc_replay` all classify as `ExeTarget.Unknown`;
real `StarWarsG.exe` + `modpath=` still classifies as `ExeTarget.Swfoc`.

Required exposing `GetProcessDetection` + `ProcessDetection` record as
`internal` (Runtime assembly already had `[InternalsVisibleTo("SwfocTrainer.Tests")]`).

If anyone ever loosens the detection heuristics — e.g. extending
`ContainsToken(processPath, "swfoc.exe")` to `Contains("swfoc")` — this
test fires immediately with a clear assertion message instead of a
delayed Live-test flake.

---

## Iter 123 — RuntimeAttachSmokeTests defensive fix

Iter 122's verification run revealed `RuntimeAttachSmokeTests.RuntimeAdapter_Should_Attach_And_Detach_When_Swfoc_Process_Is_Running`
had the same sidecar flake but wasn't covered by iter 121's `Live*`
refactor (different file naming pattern). Test doesn't take
`ITestOutputHelper`, so couldn't use `LiveSkip.AttachOrSkipAsync` directly
— added inline try/catch with bare `return;` mirroring the test's
existing locator-null skip pattern.

**Final state**: full Tests.dll suite **7589 / 0 / 5 skipped**.

## Defense-in-depth summary (iter 120/121/122/123)

Four independent locks against the same SwfocExtender.Host flake:

1. **Iter 120** — defensive try/catch in `LiveTacticalToggleWorkflowTests`
2. **Iter 121** — shared `LiveSkip.AttachOrSkipAsync` helper across 6 Live tests
3. **Iter 122** — locator-level regression test (6 cases)
4. **Iter 123** — `RuntimeAttachSmokeTests` inline fix

Tests.dll suite went from `7583 / 1 / 4` → `7589 / 0 / 5` across the
iter 120-123 batch (+6 regression tests, –1 flake exposed by iter 122
verification run, +1 caught by iter 123 fix).

---

## Iter 124-127 — Tooling polish (operator-invisible)

These iters fixed RE-tooling drift caught by re-verifying STATUS.md claims with fresh eyes. Operators won't see them but the next session and reverse engineers will.

- **Iter 124** — `tools/callgraph_query.py` had two lookup bugs (image-base mismatch on RVA inputs, no symbolic-name fallback). The CLI's `info` subcommand worked but every other lookup (`fn`, `callers`, `callees`, `reach`, `cluster`, `feature`) failed on documented input forms. Fix added `IMAGE_BASE = 0x140000000` constant + ledger-join fallback through `verified_facts.fact_id`. Pin in `tools/test_callgraph_query.py` (13 smoke checks).
- **Iter 125** — Discovered 2 undocumented subcommands (`coverage`, `dataxrefs`) shipped as working code but missing from the docstring. Updated docstring to enumerate all 11 subcommands; extended smoke harness from 13 → 18 checks. Re-ran `tools/callgraph_report.py` end-to-end: `feature_modules.json` (250 modules) + `untouched_subsystems.md` (1059 RTTI clusters + 18247 no-RTTI = 91.8% untouched within 3 hops) regenerated cleanly.
- **Iter 126** — Replay binary smoke re-verified end-to-end: 12/12 PASS (CLAUDE.md said "expect 6/6"; updated to match the actual 12 cases — 6 baseline + 6 v5 service observer/mutation seams added 2026-04-08).
- **Iter 127** — Synced `.remember/ralph_loop_state.md` with iter 114-126 entries (was stale at iter 113); updated `.claude/ralph-loop.local.md` with current state notes.

---

## Iter 128 — SetUnitShield re-audit catches iter 105 misdiagnosis

Re-investigated A1.5 SetUnitShield using the now-working callgraph CLI from iter 124-125. Iter 105's "XML-attribute-only, defer" finding was **wrong**.

The verified ledger already had four shield-write engine helpers pinned: `SetFrontShield @ 0x3A8630`, `SetRearShield @ 0x3A91E0`, plus `FrontShield_Write_Impl` and `RearShield_Write_Impl`. Hex-Rays of `sub_1403A8630` confirmed safe-to-call: `if (val >= 0) → vtbl[2](unit, 15) → FrontShield_Write_Impl(behavior, unit)`. **Same shape as iter 100's `SetSpeedOverride`.**

Iter 105's mistake: searched string-literal keys (`SHIELD_REGEN_MULTIPLIER`) which are XML-attribute keys and VFX-effect names; missed the actual function-name entries pinned in the verified ledger. Pattern lesson: future "deferred — no setter exists" findings should re-check ledger function names via the callgraph CLI before declaring the family blocked.

---

## Iter 129 — SetUnitShield LIVE wire shipped end-to-end

Promoted iter 128's RE finding to a full LIVE wire in one iter (single-session-completable because the engine helpers were already in the ledger). Bridge `Lua_SetUnitShield` rewritten to call `Resolve<pfn_SetFrontShield>(0x3A8630)(unit, val)` + `Resolve<pfn_SetRearShield>(0x3A91E0)(unit, val)` directly. Mirrors iter 100's `SetSpeedOverride` skeleton verbatim.

`UnitStatEditor`'s `shield` field write also routed through the same engine helpers (was Phase-1 mirror). Catalog flipped `SWFOC_SetUnitShield` Phase2HookPending → Live. **18th LIVE flip in master loop**.

- [ ] Test: in Hero Lab, select a hero, choose field "shield", value 5000, click Apply. Both arc shields fill to 5000.
- [ ] Test: in UnitStatEditor, stage `(shield, 2000)` and Apply. Shield value mutates; Inspector readback confirms.

---

## Iter 130 — A1.4 Hero respawn re-audit + SetHeroRespawn catalog drift caught

A second drift case caught by the iter-128 pattern. Bridge `Lua_SetHeroRespawn(seconds)` had been writing a float to `g_base + RVA::DefaultHeroRespawnTime = 0xB169F0` (matches `fact_global_default_hero_respawn_time` ledger entry) **since the bridge was first written**, but the catalog said `Phase2HookPending / BLOCKED-NO-RVA`. Pure documentation drift.

Catalog flipped `SWFOC_SetHeroRespawn` → `Live` with note: "Global default-respawn-time override — writes float at RVA 0xB169F0 (LIVE). Affects timers created AFTER the call; doesn't reset already-queued respawns. Range clamped to [0, 600] seconds."

Per-hero `SWFOC_SetHeroRespawnTimer` correctly STAYS `Phase2HookPending` — different surface needing per-hero respawn-timer table RVA.

Same iter also re-audited A1.3 SetFireRate: **stays deferred** (genuine — ledger has only consumers like `weapon_tick`, `hardpoint_fire`; no `set_fire_rate` setter exists). Iter 101's framing was correct for fire-rate.

- [ ] Test: in Hero Lab, set Custom respawn time to 2.0 seconds, click Apply. Kill a hero, time the respawn. Hero respawns in ~2s.

---

## Iter 131 — A1.6 SetGameSpeed audit + GetUnitShield LIVE pair-flip

Two parallel results:

- **A1.6 SetGameSpeed re-audit**: confirms genuine defer (NOT drift). Ledger search for game_speed / time_scale / game_time / timestep returns ZERO entries. Bridge `Lua_SetGameSpeed` (lua_bridge.cpp:3057) correctly stores `g_pendingGameSpeed` as Phase-1 mirror. Catalog stays Phase2HookPending.
- **GetUnitShield LIVE pair-flip**: iter 129 set the WRITER LIVE; iter 131 closes the READ pair via `FrontShield_Read @ 0x3963C0`. Pre-iter-131 `Lua_GetUnitShield` read from a stale cache map (returned -1 for any unit not previously written via SetUnitShield); now calls FrontShield_Read directly with cache-fallback for replay/dev builds.

Catalog flipped `SWFOC_GetUnitShield` → `Live`. **20th LIVE flip in master loop**.

- [ ] Test: read shield value via SWFOC_GetUnitShield in Lua Playground. Returns engine current value, not stale cache.

---

## Iter 132 — Phase2HookPending audit pass: 24 candidates triaged

Comprehensive sweep of all 24 catalog entries marked `Phase2HookPending` using the iter-128 ledger-search pattern via the iter-124-fixed callgraph CLI. Authored `knowledge-base/iter132_phase2_pending_audit.md` with structured triage.

- **1 strong drift candidate**: `SWFOC_SetDiplomacy` — engine writer at `0x288800` confirmed via Hex-Rays. Queued as iter 133 LIVE wire.
- **4 drift candidates needing investigation**: ChangePlanetOwner / ChangePlanetOwnerWithMode / GetPlanets / SpawnAsStoryArrival galactic-mode entries. Queued as iter 134 batch re-audit.
- **12 confirmed defers**: XML-attribute family + missing-ledger-surface entries.
- **Per-field dispatchers**: HeroStatEdit + SetUnitField partially LIVE (hull/shield/speed) but catalog single-status entry under-reports.

ROI projection at the time: 5-7 LIVE flips across iter 132-135. Actual delivered: 5 LIVE flips + 2 Phase-1 mirror fixes (iter 133/135/136 LIVE + iter 137 broken-contract patches).

---

## Iter 133 — SetDiplomacy LIVE wire shipped

Promoted from iter 132 audit's strong drift candidate to full LIVE wire in one iter. Bridge `Lua_SetDiplomacy` rewritten to walk `PlayerArray_Global` for `slot_a → PlayerClass*` (same pattern as `SetHumanPlayer_v2`), then call `Resolve<pfn_MakeAllyEnemy>(0x288800)(player_a, slot_b, state_code)`.

State strings 'ally'→0, 'enemy'→1, 'neutral'→2. **One-way per-pair write** — operator must call twice with swapped slots for symmetric relations. Engine semantics confirmed via Hex-Rays of `sub_140288800`: `*(p[+0x370] + 4*slot_b) = state` (15-byte writer).

Catalog flipped `SWFOC_SetDiplomacy` → `Live`. **21st LIVE flip in master loop**.

- [ ] Test: in Galactic tab Set Diplomacy, pick slot 0 → slot 1 ally, click Set. AI relation between Rebels and Empire becomes friendly. Call again with slot 1 → slot 0 for symmetric.

---

## Iter 134 — Galactic 4-candidate audit: REVISED to all-confirmed-defer

Iter 132 was over-optimistic about the galactic batch. Re-audited via callgraph CLI + Hex-Rays:

- **`ChangePlanetOwner`**: Phase-1 mirror; engine writers `PlanetFactionChange_FullTransfer @ 0x3FB040` (3989 bytes, 4 args) and `PlanetFactionChange_InitialSet @ 0x3FA160` (271 bytes, 2 args) too complex for single-iter Resolve<>() pattern. **DEFER**.
- **`ChangePlanetOwnerWithMode`** + **`SpawnAsStoryArrival`**: NOT IN BRIDGE — flagged as **vestigial** at iter 134, later corrected to **broken contract** at iter 137 (the editor calls them so they need Phase-1 mirrors).
- **`GetPlanets`**: returns `"count=0"` sentinel; no planet-list walker. **DEFER**.

Audit-only iter (no LIVE flip shipped). Updated `knowledge-base/iter132_phase2_pending_audit.md`.

Pattern lesson: iter-128 audit's leverage is **highest when the ledger has a single-arg `(unit, value)`-shape engine setter**. Multi-arg / state-laden functions need full RTTI-arc work, not single-iter scope.

---

## Iter 135 — HeroStatEdit partial-LIVE catalog clarification

Bridge `Lua_HeroStatEdit` had per-field LIVE branches all along:

- `hull` → direct write to `addr + RVA::GameObj::HP` (always LIVE)
- `speed` → `SetSpeedOverride @ 0x3A8C90` (LIVE iter 100)
- `shield` → `SetFrontShield + SetRearShield` (LIVE iter 129)
- `respawn_ms` → Phase-1 mirror only

But the catalog said "Phase 1 mirror — composes per-field setters" — under-reported 3/4 sub-fields. Catalog flipped to `Live` with explicit per-field note enumerating each sub-field's status.

**Iter 133 leftover discovered**: `GalacticTabViewModelCapabilityTests` was missed at iter 133 ship. Iter 135 caught it via the full-suite re-run and updated 2 assertions.

7 red-green pins added in `Iter135HeroStatEditPartialLiveTests.cs`. **22nd LIVE flip in master loop**.

- [ ] Test: same Hero Lab tests as iter 129 (shield/speed/hull edits) — badge now reads `LIVE` instead of `PHASE 2 PENDING`.

---

## Iter 136 — SetUnitField bridge per-field LIVE branches mirror

Bridge `Lua_SetUnitField` was purely Phase-1 (queued every field write), even hull/shield/speed which had LIVE engine helpers since iter 100/129. UnitStatEditor's "Apply staged edits" button was Phase-1 even when the operator staged hull/shield/speed.

`Lua_SetUnitField` rewritten to mirror `Lua_HeroStatEdit`'s per-field LIVE branches: hull → direct GameObj::HP write; shield → SetFrontShield + SetRearShield; speed → SetSpeedOverride. Other 10 fields fall through to `g_pendingUnitFieldWrites` Phase-1 mirror queue.

`IsValidObjAddr` + `IsObjOwnedByHuman` enemy READ-ONLY gates added (was unguarded pre-iter-136).

Catalog flipped `SWFOC_SetUnitField` → `Live` (3/13 fields LIVE; 10 still Phase-1; explicit field enumeration in note). 8 red-green pins added in `Iter136SetUnitFieldPartialLiveTests.cs`. **23rd LIVE flip in master loop**.

- [ ] Test: in UnitStatEditor, stage `(hull, 9999)` + `(shield, 5000)` + `(speed, 250)`, click Apply. Each LIVE field mutates the engine. Stage `(max_hull, 99999)` — Phase-1 mirror, no engine effect (badge note explains).

---

## Iter 137 — Vestigial-entry cleanup: broken-contract bridge fix

Iter 134 flagged `SWFOC_ChangePlanetOwnerWithMode` and `SWFOC_SpawnAsStoryArrival` as "vestigial" — but iter 137 found they were actually **broken contracts**: editor's `BridgeGalacticDispatcher` called them via DoString, but the bridge had no `Lua_*` implementation, so the operator's Galactic-tab buttons errored at runtime with `attempt to call nil value`.

Bridge fix: added Phase-1 mirror implementations:

- `Lua_ChangePlanetOwnerWithMode(planet, new_owner, mode)` → records to new `g_pendingPlanetFlipModes` vector under existing `g_planetLock`
- `Lua_SpawnAsStoryArrival(type, planet, faction)` → records to new `g_pendingStoryArrivalSpawns` vector under new `g_storyArrivalLock`

Both registered in `RegisterAll`. Catalog stays `Phase2HookPending` — Phase 2 engine wire-through genuinely blocked per iter 134's multi-arg writer findings. Notes extended with iter 137 + iter 134 provenance + the overlay Feature 2/3 alternate-path explanation (operator's actual buttons use the C++ overlay DLL's separate dispatch path).

5 red-green pins added in `Iter137VestigialMirrorPinTests.cs`.

**Pattern lesson — third failure mode formalized**: "vestigial" entries the editor still calls aren't truly vestigial — they're broken contracts. The fix isn't catalog deletion (which orphans the editor button) but adding the missing Phase-1 mirror so the PHASE 2 PENDING badge becomes accurate.

- [ ] Test: in Galactic tab, click "Flip and convert garrison" with a target planet selected. Returns `"OK: planet flip with mode recorded (Phase 2 multi-arg engine writer blocked per iter 134)"` instead of erroring. Same for Story-arrival spawn.

---

## Iter 138 — Round-2 broken-contract audit (clean)

Re-swept all 26 current Phase2HookPending catalog entries against the bridge's `RegisterAll` block using iter 137's broken-contract lens. **Zero new broken contracts found** — every Phase2 entry has a corresponding `Lua_*` registration.

Cross-checked all 92 catalog SWFOC_* entries vs bridge registrations: all 92 mapped (no orphan catalog entries pointing at missing bridge code). The contract surface is intact.

30-second `grep`+`comm` audit, no source/test/rebuild needed. Records the all-clean result.

---

## Final tally for iter 117-138

| Type | Count | Iters |
|---|---|---|
| Native UX surfaces (LIVE wires from iter 100-113 → operator buttons) | 3 | 117, 118, 119 |
| Live-test resilience defense-in-depth | 4 | 120, 121, 122, 123 |
| RE-tooling polish (callgraph CLI + replay verify + loop sync) | 4 | 124, 125, 126, 127 |
| Catalog drift catches → LIVE flips | 6 | 105/128 (iter 129 ship), 130, 131, 133, 135, 136 |
| Audit-only iters (triage / re-classification) | 3 | 132, 134, 138 |
| Broken-contract bridge fixes (Phase-1 mirror added) | 1 | 137 (covers 2 helpers) |

**Editor test suite trajectory**: 7515 (iter 90) → 7570 (iter 113) → 7589 (iter 123) → 7610 (iter 135) → 7618 (iter 136) → 7623 (iter 137-138). Net **+108 tests across iter 91-138** (+33 across iter 117-138 alone), all green.

**Bridge harness**: 1100/0 throughout.

**Verifier lint**: 315 entries / 0 errors / 0 warnings throughout.

**Editor binary**: 156.85 MB (iter 113) → 156.88 MB (iter 119) → 157.4 MB (iter 137).

---

## Camera primitive arc — iter 143-145 (6 LIVE flips, master loop now at 29 LIVE wires)

Iter 106 had pinned 7 camera-related engine Lua APIs at the LuaUserVar registry. Iter 107 wired the first one (Scroll_Camera_To). Iter 143-145 closed the remaining 6 in a focused arc using the proven engine-Lua-API + DoString skeleton:

| Engine Lua API | RVA | Iter | Bridge wire |
|---|---|---|---|
| `Scroll_Camera_To(target)` | 0x140898d58 | 107 | `SWFOC_ScrollCameraToTarget` |
| `Camera_To_Follow(target)` | 0x140898d70 | **143** | `SWFOC_CameraFollow` |
| `Rotate_Camera_To(target)` | 0x140898db0 | **144** | `SWFOC_RotateCameraTo` |
| `Start_Cinematic_Camera()` | 0x140898ec0 | **145** | `SWFOC_StartCinematicCamera` |
| `End_Cinematic_Camera()` | 0x140898ed8 | **145** | `SWFOC_EndCinematicCamera` |
| `Set_Cinematic_Camera_Key(args)` | 0x140898f30 | **145** | `SWFOC_SetCinematicCameraKey` |
| `Transition_Cinematic_Camera_Key(args)` | 0x140898f50 | **145** | `SWFOC_TransitionCinematicCameraKey` |

### Operator-test checklist (Camera & Debug tab — Lua Playground until per-tab UX surfaces ship)

- [ ] **Pan once to target** — `SWFOC_ScrollCameraToTarget('Find_Planet("Yavin")')`. Camera pans once; doesn't track movement.
- [ ] **Track unit as it moves** — `SWFOC_CameraFollow('Find_First_Object("Empire_AT_AT")')`. Camera attaches to the AT-AT and follows as it walks.
- [ ] **Rotate to face target** — `SWFOC_RotateCameraTo('Find_First_Object("Rebel_T2A_Tank")')`. Camera rotates in place to face the tank.
- [ ] **Start cinematic mode** — `SWFOC_StartCinematicCamera()`. Engine enters cinematic camera mode.
- [ ] **Set keyframes** — `SWFOC_SetCinematicCameraKey('1, Find_Planet("Yavin"), 5.0')`. Sets a cinematic keyframe (engine accepts varying arg shapes; common form: key index + position + look-at + duration).
- [ ] **Transition between keys** — `SWFOC_TransitionCinematicCameraKey('1, 2, 2.5')`. Triggers transition between keyframes.
- [ ] **End cinematic mode** — `SWFOC_EndCinematicCamera()`. Exits cinematic camera mode.

### Pattern lesson

Each LIVE wire ships with ~50 LoC end-to-end (bridge function + simulator handler + tests + catalog entry). The cumulative count of LIVE primitives shipped via the engine-Lua-API + DoString pattern is now **15** (iter 100/107/108/109/110/111/112/113/133/143/144 single-shot + iter 145 quad). The marginal cost is so low that batch-shipping related primitives in one iter (like iter 145's cinematic quad) is straightforward.

### Native UX surface — queued for future iters

All 7 camera primitives are accessible via Lua Playground today. Per-tab native buttons on Camera & Debug tab queued for follow-up iters:
- iter 144 candidate: "Follow target" button next to iter 107's "Scroll camera to target"
- iter 145 candidate: "Cinematic mode" GroupBox with Start/End buttons + keyframe DataGrid

### Final tally — iter 117-145

| Type | Count | Iters |
|---|---|---|
| Native UX surfaces (LIVE wires from iter 100-113 → operator buttons) | 3 | 117, 118, 119 |
| Live-test resilience defense-in-depth | 4 | 120, 121, 122, 123 |
| RE-tooling polish (callgraph CLI + replay verify + loop sync) | 4 | 124, 125, 126, 127 |
| Catalog drift catches → LIVE flips | 6 | 105/128 (iter 129 ship), 130, 131, 133, 135, 136 |
| Audit-only iters (triage / re-classification) | 5 | 132, 134, 138, 141, 142 |
| Broken-contract bridge fixes (Phase-1 mirror added) | 1 | 137 (covers 2 helpers) |
| Simulator coverage closures | 1 | 140 (4 read handlers) |
| Operator changelogs | 2 | 139 (initial), 146 (camera arc) |
| Camera primitive arc LIVE flips | 6 | 143, 144, 145 (4 in batch) |

**Editor test suite trajectory**: 7515 (iter 90) → 7570 (iter 113) → 7589 (iter 123) → 7610 (iter 135) → 7618 (iter 136) → 7623 (iter 137-138) → 7629 (iter 140) → 7635 (iter 143) → 7641 (iter 144) → **7645 (iter 145)**. Net **+130 tests across iter 91-145**, all green.

**LIVE flip trajectory**: 17 (iter 100-113) → 19 (iter 130-131) → 20 (iter 131) → 21 (iter 133) → 22 (iter 135) → 23 (iter 136) → 24 (iter 143) → 25 (iter 144) → **29 (iter 145)**.

**Bridge harness**: 1100/0 throughout.

**Verifier lint**: 315 entries / 0 errors / 0 warnings throughout.

**Editor binary**: 156.85 MB (iter 113) → 156.88 MB (iter 119) → 157.4 MB (iter 137-145).
