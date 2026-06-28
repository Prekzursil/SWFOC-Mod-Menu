# SWFOC In-Game Overlay — Architecture Design (2026-04-27)

## What the operator wants

> "Implement an overlay into the game itself that communicates with the editor, similar to how WeMod works. It should look more neat than the editor and interact live with the game — drag-and-drop unit spawning at clicked spots, hijacking reinforcements workflow, story-event spawn hooks, planet-faction-flip with both convert-units and kick-out options."

In short: the editor moves out of a separate WPF window and **into the game itself**, with chrome that's intentionally cleaner than the desktop editor and a UX that lets the operator interact with the game world directly (point-and-click rather than slot-and-faction picker dropdowns).

## Why this is a separate effort from the V2 editor

The V2 editor is a **diagnostic and development surface** — every button has a tooltip explaining the bridge call, every tab exposes raw Lua probe access, and there's intentional scaffolding everywhere. That's the right shape for an RE / modding tool, but the wrong shape for in-game use:

| V2 editor (current) | Overlay (proposed) |
|---|---|
| Separate WPF window, alt-tab to use | Always-on, in-game, click-through where appropriate |
| Tab-based, dense | Contextual radial menus + drag-and-drop |
| Slot dropdowns + numeric inputs | Pointer at game world, action infers slot/coords |
| Tooltips explain Lua probe calls | Tooltips explain the game-world effect |
| Dark editor theme (functional) | Frosted glass + subtle motion (operator-pleasing) |

## Layered architecture

```
                   ┌─────────────────────────────────────────┐
                   │   StarWarsG.exe (DirectX 9 game host)   │
                   │                                         │
                   │   ┌─────────────────────────────────┐   │
                   │   │ powrprof.dll (existing bridge)  │   │
                   │   │  + named pipe                   │   │
                   │   │  + MinHook detours              │   │
                   │   │  + Lua interop (SWFOC_*)        │   │
                   │   └─────────────────────────────────┘   │
                   │                ▲                        │
                   │                │ (in-process)           │
                   │   ┌────────────┴───────────────────┐    │
                   │   │ swfoc_overlay.dll (NEW)        │    │
                   │   │  • D3D9::Present detour        │    │
                   │   │  • ImGui or Dear PyGui         │    │
                   │   │  • Hotkey: F1 toggles visible  │    │
                   │   │  • Captures mouse on hover     │    │
                   │   └────────────────────────────────┘    │
                   │                ▲                        │
                   │                │ (shared memory + event)│
                   └────────────────┼────────────────────────┘
                                    │
                                    ▼
                ┌─────────────────────────────────────────┐
                │ SwfocTrainer.App (existing WPF editor)  │
                │  • Stays as the dev / RE surface        │
                │  • Becomes the overlay's "control panel"│
                │  • Wires complex flows (snapshots,      │
                │    saved recipes, Lua playground) the   │
                │    overlay deliberately doesn't expose  │
                └─────────────────────────────────────────┘
```

Three pieces:

1. **`powrprof.dll`** — keeps doing what it does today (named-pipe bridge + Lua interop). No changes required.
2. **`swfoc_overlay.dll`** (new) — the in-game overlay. Same loading mechanism as `powrprof.dll` (place next to `StarWarsG.exe`, OS DLL search order picks it up). Detours `IDirect3DDevice9::Present` to inject ImGui rendering each frame.
3. **`SwfocTrainer.App`** — the existing WPF editor. Doesn't change semantically; becomes a power-user / RE surface. The operator does 90% of routine play through the overlay; opens the editor when they need replay, snapshots, raw Lua, etc.

## Communication channels

The overlay needs to talk to *both* the editor (for state sharing — e.g. "operator just toggled god mode in the overlay; reflect that in the editor's checkbox") and the bridge DLL (for the actual game effect).

```
Overlay ↔ Bridge:    same in-process Lua API the bridge already exposes.
                     Overlay calls bridge functions directly through Lua —
                     no IPC needed. Latency: nanoseconds.

Overlay ↔ Editor:    second named pipe (\\.\pipe\swfoc_overlay).
                     Overlay publishes UI state events; editor publishes
                     command queue. Both ends use a JSON-line protocol so
                     the existing V2BridgeAdapter pattern (which is line-
                     oriented ASCII) extends naturally.
```

The simulator already has a working two-actor pipe protocol. Reusing the same shape means the overlay can be tested against the simulator too (a *third* layer the simulator validates).

## Feature 1 — drag-and-drop tactical spawning

**Operator gesture**: hold F2, mouse cursor turns into a unit ghost showing the currently selected spawn type. Click anywhere on the tactical map → unit spawns at the clicked world position.

**Implementation chain**:

1. Overlay captures mouse-down while F2 is held.
2. Convert screen → world coords via the engine's existing **camera projection matrix** (already exposed on `D3D9Device`).
3. Emit `SWFOC_SpawnUnit('SelectedType', LocalPlayerSlot, worldX, worldY, worldZ, 1)` directly to the in-process Lua state.
4. The 6-arg form is **exactly what `BridgeSpawningDispatcher` already emits** — so the bridge contract is unchanged. The overlay just provides nicer ergonomics for picking the args.

**RE work needed** — already done:
- `SWFOC_SpawnUnit` is verified (see `verified_facts.json`, ledger entry `lua_binding_spawn_unit`).
- World coords are 3 floats; the engine accepts (X, Y, Z=0 for ground units).
- LocalPlayerSlot is queryable via `SWFOC_GetLocalPlayer`.

**RE work needed** — pending:
- Camera projection matrix readout. The engine stores it somewhere reachable from the D3D9 device pointer; needs an IDA pin to find the offset. Trainer-feature-candidates list has this as a candidate already.

## Feature 2 — galactic-map story-hook spawning

**Operator gesture**: right-click a planet on the galactic map. Radial menu appears with options including "Spawn unit here…". Pick a unit type → unit spawns *as if* it had been delivered by a story event.

**Why "story-event" is the right hook**: when you `SWFOC_SpawnUnit` directly on a galactic-mode planet, the engine doesn't always integrate the unit cleanly into the planet's defending / attacking force lists. But when a *story event* fires that spawns a unit (the way Han Solo arriving at Yavin works in canon), the engine routes it through the proper galactic state machine. We piggyback on that machinery.

**Implementation chain**:

1. Overlay enumerates the `Story_*` Lua functions registered in the runtime.
2. Picks one that takes a planet + unit-type pair as args (or, more likely, dynamically constructs a wrapper Lua function that calls the right internal helper).
3. Emits the engine-native call (similar shape to what `StoryEventService` already does).

**RE work needed**:
- Find the engine's "spawn-via-story-event" entry point. Candidates: `Spawn_Unit_Reinforcement` if it exists, or the function the campaign uses for fleet arrivals. The callgraph index in `tools/callgraph_query.py` is the right tool to find it.

## Feature 3 — planet faction flip with convert-OR-kick

**Status quo**: when a planet's owner changes (whether the operator forced it via `SWFOC_ChangePlanetOwner` or the AI took it), the engine's post-conquest cleanup **kicks out** all units that don't belong to the new owner. They're not destroyed — they're placed into a queue that returns them to their owner's nearest friendly planet.

**What the operator wants**: a choice at the moment of flip:

| Option | Result |
|---|---|
| **Default** (engine behaviour) | Kicked-out units returned to their owner's nearest base. |
| **Convert** | Units re-team to the new owner. Their `OwnerSlot` flips. |
| **Pure kick** | Units destroyed, no return queue (ruthless). |

**Implementation chain**:

1. Detour the post-conquest cleanup function (the one that walks the planet's unit list and either returns or destroys them).
2. Inject a check: read the operator's current preference from a flag set by the overlay before the click.
3. Branch on the flag — call `Switch_Sides(unit, newOwner)` for Convert, skip the return queue for Pure-Kick, fall through to the default path otherwise.

**RE work needed**:
- Find the post-conquest cleanup function. Likely a per-frame walk in galactic mode that reconciles unit ownership against planet ownership. The callgraph index can find it via `cluster` queries on `ChangePlanetOwner`.
- `Switch_Sides` exists and is verified (used by the v3 faction switch). We already call it for slot-level swaps; using it per-unit is the same pattern.

## Aesthetic direction

The user said "more neat than the editor". The editor's V2 dark theme is functional but austere. For the overlay:

- **Frosted glass** background panels with subtle blur — readable over the busy game world without obscuring it.
- **Neon SW palette** — Empire chrome (steel grey + crimson accent), Rebel terminal (amber on dark), Underworld grit (sand + rust). Operator's current faction tints the chrome.
- **Subtle motion** — panels slide in from the edge, never pop. Hover reveals contextual help.
- **No tabs** — the overlay is contextual. What you see depends on what's under the cursor + what mode (tactical / galactic) you're in.
- **Hotkeys are first-class** — every action has a keyboard binding shown next to its label. F-keys for modes; number row for unit-type quick-pick; QWE radial menu activation; spacebar for "do the obvious thing here" (spawn / repair / select).

## Phasing

Realistic build phases — not all-or-nothing:

| Phase | Scope | Pre-req |
|---|---|---|
| **0** (now) | Architecture doc + RE backlog (this doc + ledger candidates). | — |
| **1** | `swfoc_overlay.dll` + ImGui + F1 toggle + visible "Hello world" panel rendered over the game. | D3D9 hook proven to load. |
| **2** | Read-only HUD: live local-player credits, alive-unit count, current planet (galactic) / map (tactical). Pulls from existing bridge. | Phase 1. Camera/coord IPC not needed for HUD. |
| **3** | Unit-type quick-pick + drag-to-spawn (tactical). Uses the verified 6-arg `SWFOC_SpawnUnit` plus camera-coord readout. | Camera projection matrix RE-pin. |
| **4** | Galactic story-hook spawning (radial menu on planet). | Story-event spawn hook RE-pin. |
| **5** | Planet flip with convert/kick option. | Post-conquest cleanup RE-pin + per-unit `Switch_Sides`. |
| **6** | Editor↔overlay state sync: shared toggles (god mode, free build, freeze credits) reflect in both surfaces in real time. | Phase 1 + the second named pipe. |
| **7** | Aesthetic polish: faction-tinted chrome, animations, hotkey hints. | All previous phases. |

## What this design does NOT change

- The simulator and its 124 tests remain the canonical regression guard for editor + bridge.
- The bridge DLL and Lua-side service surface stays the same — overlay calls the same `SWFOC_*` functions the editor does.
- The V2 editor stays useful as the dev / RE surface.
- The verified ledger continues to gate which RVAs the overlay can hook (no overlay feature ships against an unverified RVA).

## Risks

- **D3D9 hook fragility** — anti-cheat on multiplayer servers will flag this. Decision: overlay is single-player / skirmish only, like the rest of the trainer. Document loud and clear.
- **Performance** — ImGui per frame adds ~0.5 ms typical. Fine at 60 fps; we already monitor for the freeze trap (see iter ValueFreezeService rewrite). Budget 1 ms / frame as the soft cap.
- **State drift between overlay and editor** — handled by the second pipe; both surfaces poll the bridge for ground truth on toggles, not each other.
- **Operator surprises** — drag-to-spawn in tactical is *very* powerful. Default to "shift+click confirms" or a 0.5 s hold to commit, so accidental clicks don't dump 10 AT-ATs.

## Implementation kick-off plan

When ready to start Phase 1:

1. **Skeleton DLL project** at `swfoc_lua_bridge/../swfoc_overlay/` (sibling to existing bridge).
2. **MinHook detour** of `IDirect3DDevice9::Present`. Easiest reference: existing `swfoc_lua_bridge` already uses MinHook — copy the loader pattern.
3. **ImGui** vendored as a submodule (header-only, no dependency hell).
4. **F1 hotkey** toggles overlay visibility — that's the Phase 1 done line.
5. **Smoke test**: launch SWFOC, F1, verify a panel renders over the game without crashing.

Anything past Phase 1 needs design review before commit; the foundation has to be solid first.
