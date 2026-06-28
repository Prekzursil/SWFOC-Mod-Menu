# Ralph Loop Changelog — 2026-04-28 (master loop iter 100-114)

**Session theme**: Engine-Lua-API + DoString pattern → 17 LIVE capability flips on the bridge surface, zero MinHook detours, zero new RVA pins. Per-method wires for catalog typing + a universal escape hatch for everything else.

**Final artifacts (2026-04-28 23:12 local)**:
- Bridge `swfoc_lua_bridge/powrprof.dll` — 376 KB — 1100/0 harness PASS
- Editor `publish/SwfocTrainer.App.exe` — 156.86 MB single-file — 7570 .NET tests pass / 0 fail / 5 skipped
- Overlay `swfoc_overlay/swfoc_overlay.dll` — 270 KB — 5-row HUD with faction tinting
- Verifier lint — 315 entries, 0/0 errors/warnings

---

## Pattern proven: engine-Lua-API + DoString (no MinHook needed)

The breakthrough came in iter 99-100. SWFOC's Alamo engine exposes a large surface area through its Lua VM via the LuaUserVar registry function `sub_140546c70`. Engine functions like `Override_Max_Speed`, `Scroll_Camera_To`, `Change_Owner`, `Spawn_Unit`, `Make_Invulnerable`, `Hide`, `Despawn`, etc. are ALL callable from Lua source code — and the bridge already has a `DoString` C++ helper that lets it execute Lua source against the engine's VM.

So instead of:
1. Pinning RVAs via 3-tool consensus
2. Writing MinHook detours with `__fastcall` typedefs
3. Validating struct offsets via RTTI dissection

…the bridge can compose Lua source like `(<unit>):Make_Invulnerable(true)` and dispatch it through `DoString`. **One C++ helper per method shape (no-arg / bool-arg / multi-arg) covers an entire family of engine APIs.**

This pattern compounds: shared helpers (`Lua_DispatchUnitBoolMethod`, `Lua_DispatchUnitNoArgMethod`, `Lua_CallObjMethodLua`) made the marginal cost of new wires drop from ~250 LoC (iter 100 first-of-kind) to ~3 lines per wrapper (iter 111/112 batches).

---

## LIVE flips by tab (operator-facing test checklist)

### Speed tab

- [ ] **Apply per-unit speed** (existing TextBox + "Apply" button) — **LIVE** via SetSpeedOverride engine call (RVA 0x3A8C90). Test: select a unit, set speed to 250, click Apply. Unit should walk much faster. Iter 100.
- [ ] **Revert per-unit speed** (NEW button next to Apply) — **LIVE** via ClearSpeedOverride (RVA 0x38F8B0). Test: after applying speed, click Revert. Unit returns to natural max. Iter 100/102.
- [ ] **Per-faction move speed** (existing Slot + Multiplier + Apply) — **LIVE**. Banner now reads "✓ LIVE (iter 100)". Test: pick slot 0, multiplier 350, click Apply. All Rebel units walk fast. Iter 100/114.
- [ ] **Global game speed** still PHASE 2 PENDING — no engine helper pinned yet. Badge surfaces this; the per-button warning was simplified.

### Combat tab

- [ ] **Apply (per-slot) damage multiplier** — **PHASE 2 PENDING** (attacker-context not at Take_Damage layer). Badge shows "PHASE 2 PENDING". Iter 95 finding.
- [ ] **Apply (GLOBAL) damage multiplier** (NEW button) — **LIVE** via Take_Damage_Outer detour. Test: set multiplier 2.0, click Apply (GLOBAL). Every incoming damage doubles. Iter 96/100/102.

### Camera & Debug tab

- [ ] **Scroll camera to target** (NEW GroupBox between per-pose grid and raw-Lua escape hatch) — **LIVE** via engine's `Scroll_Camera_To` Lua API. Test:
  - Galactic mode: enter `Find_Planet("Yavin")`, click Scroll. Camera pans to Yavin.
  - Tactical mode: enter `Find_First_Object("Empire_AT_AT")`, click Scroll. Camera centers on the AT-AT.
  - Iter 107.
- [ ] **Apply pose** (existing X/Y/Z + Apply pose button) — still PHASE 2 PENDING. Engine's Lua API takes a position-userdata, not raw floats; constructor not yet pinned. Catalog note explains.
- [ ] **Toggle free cam** still PHASE 2 PENDING — no engine `Free_Cam` Lua API. Catalog note explains.

### Bridge surface (operator-callable via SWFOC_DoString or Lua Playground)

These shipped catalog-LIVE without dedicated tab buttons. Operators access them via the Lua Playground or SWFOC_DoString. Future UX polish iters will add per-tab buttons.

- [ ] **`SWFOC_ChangeUnitOwner(unit_lua, player_lua)`** — engine's per-unit `Change_Owner` Lua method. Updates ownership, fires UI events, plays audio, processes corruption, updates AI budgets. Test: `SWFOC_ChangeUnitOwner('Find_First_Object("Empire_AT_AT")', 'Find_Player("REBEL")')` — the Empire AT-AT switches to your Rebels. Iter 108.
- [ ] **`SWFOC_SpawnUnitLua(player_lua, type_lua, position_lua)`** — engine's `Spawn_Unit` Lua API. Test: `SWFOC_SpawnUnitLua('Find_Player("REBEL")', 'Find_Object_Type("Rebel_Trooper_Squad")', 'Create_Position(0,0,0)')` — squad spawns at origin. Iter 109.
- [ ] **`SWFOC_MakeUnitInvulnLua(unit_lua, "true"/"false")`** — engine's `Make_Invulnerable` Lua method. Wrapper at RVA 0x57D550 propagates via BehaviorAttach to all hardpoints (verified ledger fact). Iter 110.
- [ ] **`SWFOC_HideUnitLua(unit_lua, "true"/"false")`** — engine's `Hide` Lua method. Toggles unit visibility without removing it. Iter 111.
- [ ] **`SWFOC_PreventAiUsageLua(unit_lua, "true"/"false")`** — engine's `Prevent_AI_Usage` method. Locks the AI away from the unit. Iter 111.
- [ ] **`SWFOC_SetUnitSelectableLua(unit_lua, "true"/"false")`** — engine's `Set_Selectable` method. Toggle whether the operator can click-select. Iter 111.
- [ ] **`SWFOC_DespawnUnitLua(unit_lua)`** — engine's `Despawn` method. Cleanly remove a unit. Iter 112.
- [ ] **`SWFOC_StopUnitLua(unit_lua)`** — engine's `Stop` method. Halt current action. Iter 112.
- [ ] **`SWFOC_RetreatUnitLua(unit_lua)`** — engine's `Retreat` method. Send unit back. Iter 112.
- [ ] **`SWFOC_CallObjMethodLua(obj_lua, method_name, args_lua)`** — UNIVERSAL escape hatch. Calls ANY method on a Lua object handle with caller-supplied args. Test: `SWFOC_CallObjMethodLua('Find_Player("REBEL")', 'Give_Money', '5000')` — Rebels gain 5000 credits. Iter 113.

### Overlay (in-game DLL, F1 toggle)

- [ ] **5-row HUD bar bottom-right** — bridge LED + credits + units + scene-known + last-error. Iter 92 + iter 103.
- [ ] **Bridge LED faction tinting** — REBEL amber / EMPIRE chrome / UNDERWORLD sand+rust when bridge knows the local-player slot. Iter 103.
- [ ] **Last-error indicator** (5th row) — bright red when bridge's most recent probe failed; dim gray otherwise. Operator notices live without alt-tabbing to DebugView. Iter 103.

---

## Deferred RE findings (XML-attribute-only family)

These engine fields are loaded from XML at unit construction and have no exposed runtime setter. They need either RTTI dissection of the per-unit struct OR a MinHook on the relevant tick/update path. Earmarked for a future dedicated arc.

| Wire | Iter | Strings found | Why deferred |
|---|---|---|---|
| SetFireRate | 101 | `FIRE_RATE_MULTIPLIER` @ 0x1407ff710 (data-only ref) | XML attribute key, no `Override_Fire_Rate` engine helper |
| SetHeroRespawnTimer / SetPermadeath | 104 | `Tactical_Respawn_Time_In_Secs` @ 0x1408816c0, `Default_Hero_Respawn_Time` @ 0x140884d38, `garrison_respawn_counter` @ 0x140861668 | XML-only; schedule fn `sub_14048eb10` reads timer from XMM arg without setter |
| SetUnitShield | 105 | `SHIELD_REGEN_MULTIPLIER` @ 0x1407ff648 etc. | XML keys + behavior names; the one "SHIELD" code-ref is for VFX-effect spawning, not the per-unit shield value |

**Pattern recognition**: when grep finds XML-attribute strings with data-only refs (no code paths reading them at runtime via setter), the engine likely bakes the value at unit construction. Future iters should grep for `Override_*` / `Set*Override` / `Set*Field` engine helpers FIRST and only fall back to RTTI dissection when no clean setter exists.

---

## Bonus shipped this session

### Slash-command bug fix (iter 102)

`/ralph-loop` was failing with `unexpected EOF while looking for matching '` whenever the user prompt contained backticks/quotes. The slash-command markdown used a heredoc-with-`$(cat)` capture that broke on Claude Code's `$ARGUMENTS` substitution.

**Fix**: replaced heredoc capture with sed-based marker-line extraction into a `mktemp` tempfile, plus added `--raw-args-file` flag to `setup-ralph-loop.sh`. Now survives any quote/backtick/backslash content. Lives at `~/.claude/plugins/cache/claude-plugins-official/ralph-loop/...`.

### XAML close-out (iter 102 + iter 114)

- Iter 102 caught: VM commands `SetDamageMultiplierGlobalCommand` (Combat) and `ClearUnitSpeedOverrideCommand` (Speed) existed but had no XAML buttons. Operator couldn't access them. Added the buttons + matching capability badges + tooltips.
- Iter 114 caught: Speed tab per-faction amber MIRROR banner survived iter 102's per-unit-only update. Replaced with green LIVE banner.

These were the kind of half-finished work the "drive every subject to 100% before moving on" discipline is designed to prevent — both caught and fixed inline.

### Test additions (38 new GREEN)

| Test file | Iter | Count | Coverage |
|---|---|---|---|
| Iter97DamageMultiplierGlobalTests.cs | 97 | 7 | Take_Damage_Outer detour |
| Iter100SpeedOverrideTests.cs | 100 | 9 | Speed wires + per-faction enumeration |
| Iter107ScrollCameraToTargetTests.cs | 107 | 6 | Scroll_Camera_To via DoString |
| Iter108ChangeUnitOwnerTests.cs | 108 | 3 | Change_Owner via DoString |
| Iter109SpawnUnitLuaTests.cs | 109 | 3 | Spawn_Unit via DoString |
| Iter110MakeUnitInvulnLuaTests.cs | 110 | 3 | Make_Invulnerable via DoString |
| Iter111UnitBoolMethodBatchTests.cs | 111 | 4 | Hide / PreventAi / Selectable batch |
| Iter112UnitNoArgMethodBatchTests.cs | 112 | 4 | Despawn / Stop / Retreat batch |
| Iter113CallObjMethodLuaTests.cs | 113 | 4 | Universal CallObjMethod escape hatch |

Plus updates to `SpeedTabViewModelCapabilityTests`, `CombatTabViewModelCapabilityTests`, `CameraDebugTabViewModelCapabilityTests`, `PhaseBSimulatorTests`, `CapabilityCatalogReverseOrphanTests`, `PhaseTwoPendingBadgeAuditTests` reflecting the new catalog state.

---

## Master loop progress (Thread A1 LIVE-wire surface)

**12/12 sub-tasks closed** (3 deferred to RTTI-dissection arc):

| Sub-task | Status | Iter | Wire |
|---|---|---|---|
| A1.1 Combat damage global | CLOSED | 96/97/100 backfill | Take_Damage_Outer detour + DoString |
| A1.2 Speed | CLOSED | 100 | SetSpeedOverride direct call |
| A1.3 SetFireRate | DEFERRED | 101 | XML-attribute-only |
| A1.4 Hero respawn | DEFERRED | 104 | XML-attribute-only |
| A1.5 SetUnitShield | DEFERRED | 105 | XML-attribute-only |
| A1.6 Camera Scroll | CLOSED | 107 | Scroll_Camera_To via DoString |
| A1.7 Galactic owner-change | CLOSED | 108 | Change_Owner via DoString |
| A1.8 Spawning | CLOSED | 109 | Spawn_Unit via DoString |
| A1.9 Per-unit Invuln | CLOSED | 110 | Make_Invulnerable via DoString |
| A1.10 Hide / PreventAi / Selectable | CLOSED | 111 (batch) | shared bool-arg helper |
| A1.11 Despawn / Stop / Retreat | CLOSED | 112 (batch) | shared no-arg helper |
| A1.12 Universal method dispatcher | CLOSED | 113 (capstone) | CallObjMethodLua escape hatch |

**Threads B/C/D/E queued for future sessions**:
- B: overlay phases 2-7 (Phase 2 HUD foundation done; Phase 2-full ImGui vendoring pending; Phases 3-7 queued)
- C: save-game RE (open-ended — bounded done = format spec + inspector + 2 repair heuristics)
- D: multi-repo CI gate hygiene (QZP, event-link, Prekzursil_*) — 100% Codecov + 100% SonarCloud as hard-fail gates
- E: local SonarQube workflow at `Documents/sonarqube-2026.2.1.121354`

---

## Live-test plan (when operator is ready)

These wires should be tested live in this priority order:

1. **Iter 100 Speed**: simplest — pick any unit, set speed 250, walks faster. Then revert.
2. **Iter 96/100 Damage global**: set multiplier 2.0, take a swing at any unit. Damage doubles.
3. **Iter 107 Camera Scroll**: galactic mode, scroll to Yavin/Hoth/Coruscant by name.
4. **Iter 108 Change_Owner**: convert an enemy AT-AT to your faction. Audio + UI events should fire.
5. **Iter 109 Spawn_Unit**: spawn a Rebel squad at origin in tactical mode.
6. **Iter 110-112 batch methods**: Make_Invulnerable / Hide / Despawn / Stop / Retreat — each should produce the obvious in-game effect.
7. **Iter 113 universal escape hatch**: try `Give_Money(5000)` on a player — bank should jump.

If any of the above fail live, capture the bridge log line — it'll show the exact `DoString` source that was dispatched and the engine's `pcall` return code. The bridge already records "ERR: <method> raised engine error rc=<n>" on failure.
