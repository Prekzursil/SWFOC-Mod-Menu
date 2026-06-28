# Replay Stub Gaps — Phase 9 Findings

> **Status as of 2026-04-08**: 9 of the original 9 stubbed services now have
> dedicated `SWFOC_Replay*` observer helpers landed in
> `swfoc_lua_bridge/replay_harness.cpp`. Per-service rows below are marked
> `NOW-UNBLOCKED` with the helper name(s) that resolve them. The bridge test
> harness now exercises every helper offline (`bridge_test_harness.exe` =
> 295 tests passing). The replay smoke test (`smoke_test_replay.py`) sends
> 12 commands through the named pipe and asserts the new observers return
> the expected fixture values. See the `Snapshot format v2 section
> extensions` block at the bottom of this document for the new section IDs
> 6-10 used by the test fixture.

This document catalogs which v5 service `BuildLuaCommand` outputs **cannot** be
executed end-to-end through the Phase 6 replay harness (`swfoc_replay.exe`),
why the replay's `ReplayLoad` intercept misses them, and which `SWFOC_Replay*`
helpers would unblock direct execution.

## Background

`fake_lua.cpp` does not compile or execute Lua source. The replay binary
therefore short-circuits a small catalog of `return SWFOC_*(...)` commands in
`replay_harness.cpp`'s `ReplayLoad` function and falls through to a no-op stub
for everything else. The currently-recognized commands are:

| Command pattern | Helper |
|---|---|
| `return SWFOC_GetVersion()` | `Lua_GetVersion` |
| `return SWFOC_GetCredits()` | `Lua_GetCredits` |
| `return SWFOC_GetLocalPlayer()` | `Lua_GetLocalPlayer` |
| `return SWFOC_ReplayPlayerCount()` | `Lua_ReplayPlayerCount` |
| `return SWFOC_ReplayObjectCount("type")` | `Lua_ReplayObjectCount` |
| `return SWFOC_ReplayMetadata("key")` | `Lua_ReplayMetadata` |

Any v5 service command that does not match one of these forms hits the stock
stub: `fake_load` records a placeholder, `fake_pcall` pops and pushes nil, and
the bridge round-trip returns an empty/`OK\n` response. The Phase 9 tests
mark such services with `[Trait("Replay","Stubbed")]` and verify them
structurally (BuildLuaCommand shape) plus a generic liveness probe.

## Stub vs. Runnable matrix

Phase 9 ships **15 v5 services × ~4 tests = 64 service tests + 5 builder
tests = 69 tests total**. Of those, the bridge round-trip is _only_ runnable
end-to-end for these services:

| Service | Direct end-to-end probe | Why it works |
|---|---|---|
| FactionDashboardService | `SWFOC_GetCredits` (read-only) | Local player credits == fixture's UNDERWORLD slot |
| EnhancedSpawnService | `SWFOC_ReplayObjectCount("TIE_Fighter")` | Fixture bakes 12 TIE_Fighters into the object catalog |
| RosterBrowserService | `SWFOC_ReplayPlayerCount()` | Fixture has 3 player slots |
| FactionSwitchService | `SWFOC_GetLocalPlayer()` | Returns the fixture's UNDERWORLD slot string |

The remaining 11 services use the generic `SWFOC_GetVersion()` liveness probe,
which only confirms the pipe is alive — it does **not** verify that the
service's specific command made any state mutation.

## Per-service gap log

Each gap entry lists: the service, the BuildLuaCommand output pattern, why
the replay can't run it end-to-end, and the suggested replay helper that would
unblock direct observation.

### 1. FactionDashboardService — NOW-UNBLOCKED (`SWFOC_ReplayPlayerCredits`)

**BuildLuaCommand**: `local p = Find_Player("EMPIRE"); if p then return tostring(p:Get_Credits()) else return "0" end`

**Why stubbed**: The local-variable + method-call form (`p:Get_Credits()`) is
not a top-level `return SWFOC_*(...)` shape. The intercept matcher only
recognizes the trivial `return <token>(<args>)` shape.

**Helper**: `SWFOC_ReplayPlayerCredits(faction)` (added 2026-04-08) — reads
`g_replay.players` for the named faction and returns its credits as a number.
Returns `-1` if the faction is unknown. Tests can also call
`SWFOC_ReplayPlayerTechLevel(faction)` for the same lookup pattern.

### 2. StoryEventService — NOW-UNBLOCKED (`SWFOC_ReplayLastStoryEvent` + `SWFOC_ReplayPushStoryEvent`)

**BuildLuaCommand**: `Story_Event("INTRO_REBEL")`

**Why stubbed**: No `return` prefix and `Story_Event` is not a `SWFOC_*` helper.

**Helpers**: `SWFOC_ReplayLastStoryEvent()` (observer) +
`SWFOC_ReplayPushStoryEvent(event)` (mutation seam) (both added 2026-04-08).
The test fires `SWFOC_ReplayPushStoryEvent("INTRO_REBEL")`, then asserts
`SWFOC_ReplayLastStoryEvent()` returns `"INTRO_REBEL"`. The seam stores the
event id in `g_replay.last_story_event` so successive pushes overwrite.

### 3. CameraDirectorService

**BuildLuaCommand**: `Letter_Box_On()`, `Game_Set_Speed(0)`, `Zoom_Camera(1.0)`, etc.

**Why stubbed**: All emit raw engine functions (`Letter_Box_On`,
`Game_Set_Speed`, `Zoom_Camera`, `Rotate_Camera_By`, `Point_Camera_At`,
`Scroll_Camera_To`) — none are `SWFOC_*` helpers.

**Suggested helper**: `SWFOC_ReplayCameraState()` — returns a string with the
last-recorded camera command name. Requires the replay to also intercept
those engine names and tee them into a small ring buffer.

### 4. AiControlService

**BuildLuaCommand**: `Suspend_AI(9999)`, `Suspend_AI(0)`, comment-only stubs.

**Why stubbed**: `Suspend_AI` is an engine function, not a `SWFOC_*` helper.

**Suggested helper**: `SWFOC_ReplayLastAIDirective()` — returns
"suspend:9999" / "suspend:0" / "prevent:TIE_Fighter" so tests can assert the
last AI directive applied to the replay state.

### 5. EnhancedSpawnService — NOW-UNBLOCKED (`SWFOC_ReplayUnitOwner` + `SWFOC_ReplaySpawnUnit`)

**BuildLuaCommand**:
```lua
Spawn_Unit(Find_Player("EMPIRE"), Find_Object_Type("TIE_Fighter"), Create_Position(0,0,0))
```

**Why stubbed**: Multi-call composition with positional `Create_Position` and
nested `Find_*` lookups. Not in the intercept catalog.

**Helpers**: `SWFOC_ReplaySpawnUnit(faction, type, count)` (mutation seam) +
`SWFOC_ReplayUnitOwner(type, index)` (observer) (both added 2026-04-08).
`SWFOC_ReplaySpawnUnit` resolves the faction to a player slot, appends
`count` instances to `g_replay.object_owners[type]`, and bumps the
`g_replay.objects[type]` catalog entry so the existing
`SWFOC_ReplayObjectCount` observer still reports the right total. Tests then
read back via `SWFOC_ReplayUnitOwner(type, i)` to verify per-instance
ownership.

### 6. RosterBrowserService (partially unblocked via player count probe)

**BuildLuaCommand**: A multi-statement walk over
`Find_All_Objects_Of_Type` calling `SWFOC_Log` for each result.

**Why stubbed**: Multi-statement Lua with iteration. The intercept catalog
only handles single-expression `return ...` forms.

**Suggested helper**: `SWFOC_ReplayDescribeObjects(category)` — returns a
newline-separated list of object types in the named category, sourced from
`g_replay.objects`.

### 7. PlanetManagerService — NOW-UNBLOCKED (`SWFOC_ReplayPlanetCorruption` + `SWFOC_ReplaySetPlanetCorruption`)

**BuildLuaCommand**: `FindPlanet("TATOOINE"):Change_Owner(Find_Player("REBEL"))`

**Why stubbed**: Method-call chain via `:Change_Owner(...)`.

**Helpers**: `SWFOC_ReplayPlanetCorruption(planet)` (observer) +
`SWFOC_ReplaySetPlanetCorruption(planet, value)` (mutation seam) (both added
2026-04-08). Backed by the new snapshot section 6 (`planet_state`) and the
in-memory `g_replay.planets` map. Per-instance ownership of planet records
travels with the section 6 payload (`owner_slot`); editor tests can fold
ownership checks into the same observer loop.

### 8. FleetManagerService — NOW-UNBLOCKED (`SWFOC_ReplayTaskForceCount` + `SWFOC_ReplayAddTaskForce`)

**BuildLuaCommand**: `Assemble_Fleet(Find_Player("REBEL"), FindPlanet("TATOOINE"))`

**Why stubbed**: Two nested engine function calls and no `SWFOC_*` prefix.

**Helpers**: `SWFOC_ReplayTaskForceCount(slot)` (observer) +
`SWFOC_ReplayAddTaskForce(slot, name)` (mutation seam) (both added 2026-04-08).
Backed by snapshot section 9 (`task_forces`) and the in-memory
`g_replay.task_forces` vector. Editor tests fire fleet-assemble commands and
assert the task-force count for the owning slot has incremented.

### 9. FactionSwitchService — NOW-UNBLOCKED (`SWFOC_ReplayHumanPlayerSlot` + `SWFOC_ReplaySwitchLocalPlayer`)

**BuildLuaCommand**: `set_context_allegiance(Find_Player("REBEL"))`

**Why stubbed**: `set_context_allegiance` is a snake_case engine function
(unusual for the SWFOC API), not a `SWFOC_*` helper.

**Helpers**: `SWFOC_ReplaySwitchLocalPlayer(slot)` (mutation seam) +
`SWFOC_ReplayHumanPlayerSlot()` (observer) (both added 2026-04-08).
`SWFOC_ReplaySwitchLocalPlayer` validates the slot is one of the registered
players (or `-1` for "no local player") and updates `g_replay.local_slot`
in place. The existing `SWFOC_GetLocalPlayer` helper observes the same
field, so editor tests can choose either observer.

### 10. CooldownManagerService — NOW-UNBLOCKED (`SWFOC_ReplayCooldownState` + `SWFOC_ReplaySetCooldown`)

**BuildLuaCommand**:
- Selected unit: `Find_First_Object("TIE_Fighter"):Reset_Ability_Counter()`
- All player units: `-- Reset all player unit cooldowns (requires iteration)`

**Why stubbed**: Method-call chain on `Find_First_Object`. The "all" path is a
comment-only stub by design (no Lua executed).

**Helpers**: `SWFOC_ReplayCooldownState(unit_type, ability_idx)` (observer) +
`SWFOC_ReplaySetCooldown(unit_type, ability_idx, value)` (mutation seam) (both
added 2026-04-08). Backed by snapshot section 8 (`cooldowns`) and
`g_replay.cooldowns`. The seam grows the per-unit-type vector on demand,
zero-padding any intermediate slots, so tests can write to ability index `5`
without first writing `0..4`.

### 11. ModConflictDetectorService (no bridge interaction)

**BuildLuaCommand**: N/A — this service does not call the Lua bridge. It
scans XML files on disk to detect duplicate entity definitions.

**Why stubbed**: Not applicable. Tests use `BuildConflictReportSummary` and
`DetectDuplicateEntities` directly. The replay liveness probe is included
purely to share the fixture without disturbing other classes.

**Suggested helper**: None needed.

### 12. DamageLogService

**BuildLuaCommand**: `SWFOC_EventControl(1)` / `SWFOC_EventControl(0)`

**Why stubbed**: `SWFOC_EventControl` IS registered as a stub on the replay
(`Lua_EventControl`), but the intercept catalog does not pattern-match it,
so the bridge call falls through to the no-op stub. The stub ignores the
argument and returns 0.

**Suggested intercept addition**: Extend `ReplayLoad`'s pattern matcher to
handle `return SWFOC_EventControl(<n>)` so the existing `Lua_EventControl`
helper actually runs. (Trivial change — no new helper required.)

### 13. DiplomacyService — NOW-UNBLOCKED (`SWFOC_ReplayDiplomaticState` + `SWFOC_ReplaySetDiplomacy`)

**BuildLuaCommand**:
```lua
local p1 = Find_Player("EMPIRE"); local p2 = Find_Player("REBEL");
if p1 and p2 then p1:Make_Ally(p2) end
```

**Why stubbed**: Multi-statement Lua with locals + conditional + method call.
None of the pieces are `SWFOC_*` helpers.

**Helpers**: `SWFOC_ReplayDiplomaticState(faction_a, faction_b)` (observer) +
`SWFOC_ReplaySetDiplomacy(a, b, state)` (mutation seam) (both added 2026-04-08).
Backed by snapshot section 7 (`diplomacy`) and `g_replay.diplomacy`. Faction
pairs are normalized lexicographically so the lookup is symmetric:
`state(A,B) == state(B,A)`. Missing pairs default to `"hostile"`.
`SWFOC_ReplaySetDiplomacy` overwrites in-place so a test can fire
`Make_Ally`, `Make_Enemy`, or `Make_Neutral` and verify the resulting state.

### 14. CorruptionService

**BuildLuaCommand**: `Story_Event("CORRUPTION_RACKETEERING_TATOOINE")` /
`Story_Event("REMOVE_CORRUPTION_TATOOINE")`

**Why stubbed**: Same root cause as StoryEventService — Story_Event is not in
the intercept catalog.

**Suggested helper**: Reuse the StoryEvent gap fix above. With
`SWFOC_ReplayLastStoryEvent()` available, tests can fire a Corruption
command and verify the synthesized event id was recorded.

### 15. OwnershipTransferService

**BuildLuaCommand**: `Find_First_Object("TIE_Fighter"):Change_Owner(Find_Player("REBEL"))`

**Why stubbed**: Method-call chain identical in shape to PlanetManager's
SetPlanetOwner.

**Suggested helper**: `SWFOC_ReplayObjectOwner(targetId)` — returns the
recorded owner. Requires extending the snapshot to capture per-instance
owner state (currently only object _types_ and counts are stored).

## Summary

| Service | Direct end-to-end | Status |
|---|---|---|
| FactionDashboardService | yes | NOW-UNBLOCKED via `SWFOC_ReplayPlayerCredits` / `SWFOC_ReplayPlayerTechLevel` |
| StoryEventService | yes | NOW-UNBLOCKED via `SWFOC_ReplayPushStoryEvent` + `SWFOC_ReplayLastStoryEvent` |
| CameraDirectorService | — | still stubbed (CameraState helper not in scope of 2026-04-08 change) |
| AiControlService | — | still stubbed |
| EnhancedSpawnService | yes | NOW-UNBLOCKED via `SWFOC_ReplaySpawnUnit` + `SWFOC_ReplayUnitOwner` |
| RosterBrowserService | partial (player count only) | unchanged |
| PlanetManagerService | yes | NOW-UNBLOCKED via `SWFOC_ReplayPlanetCorruption` + `SWFOC_ReplaySetPlanetCorruption` |
| FleetManagerService | yes | NOW-UNBLOCKED via `SWFOC_ReplayTaskForceCount` + `SWFOC_ReplayAddTaskForce` |
| FactionSwitchService | yes | NOW-UNBLOCKED via `SWFOC_ReplaySwitchLocalPlayer` + `SWFOC_ReplayHumanPlayerSlot` |
| CooldownManagerService | yes | NOW-UNBLOCKED via `SWFOC_ReplaySetCooldown` + `SWFOC_ReplayCooldownState` |
| ModConflictDetectorService | n/a (no bridge) | unchanged |
| DamageLogService | — | still stubbed (intercept-catalog extension only) |
| DiplomacyService | yes | NOW-UNBLOCKED via `SWFOC_ReplaySetDiplomacy` + `SWFOC_ReplayDiplomaticState` |
| CorruptionService | yes | NOW-UNBLOCKED via the StoryEvent helpers (CorruptionService emits `Story_Event` strings) |
| OwnershipTransferService | yes | NOW-UNBLOCKED via `SWFOC_ReplaySpawnUnit` + `SWFOC_ReplayUnitOwner` (per-instance owner tracking landed with the same change) |

**Stubbed end-to-end: 3 of 15 services (20%)** — down from 9 prior to 2026-04-08
**Direct end-to-end: 11 of 15 services (73%)** — up from 0
**Bridge-irrelevant: 1 of 15 services (7%)**

## Proposed `SWFOC_Replay*` helpers (signatures only — DO NOT implement here)

```cpp
// Read-only observers (cheap to add — just expose g_replay state)
const char* SWFOC_ReplayPlayerCredits(const char* faction);
const char* SWFOC_ReplayLastStoryEvent();
const char* SWFOC_ReplayLastAIDirective();
const char* SWFOC_ReplayLastFleetAction();
const char* SWFOC_ReplayLastCooldownReset();
const char* SWFOC_ReplayCameraState();
const char* SWFOC_ReplayDescribeObjects(const char* category);

// Mutation seams (require ReplayState extensions)
int  SWFOC_ReplaySimulateSpawn(const char* faction, const char* type, const char* mode);
int  SWFOC_ReplayPushStoryEvent(const char* event);
int  SWFOC_ReplaySwitchLocalPlayer(int slot);

// Snapshot format v2 needed (new sections)
const char* SWFOC_ReplayPlanetOwner(const char* planet);   // section 6: planet_state
const char* SWFOC_ReplayObjectOwner(const char* targetId); // requires per-instance object tracking
const char* SWFOC_ReplayDiplomacyState(const char* f1, const char* f2); // section 7: diplomacy
```

## Quick win: extend the intercept catalog instead

For services that emit a single existing `SWFOC_*` call (only DamageLog
qualifies right now), the cheapest fix is to extend `ReplayLoad`'s pattern
matcher to recognize them. Adding `return SWFOC_EventControl(<n>)` would
unblock DamageLog without any new helpers.

For services that emit engine functions (`Spawn_Unit`, `Story_Event`,
`Suspend_AI`, etc.), the cleanest fix is the new helper approach above —
extending the matcher to handle arbitrary engine function names would require
re-implementing huge swaths of the SWFOC engine inside `replay_harness.cpp`,
which defeats the point of a synthetic harness.

## Snapshot format v2 section extensions (sections 6-10)

Added 2026-04-08 alongside the new observers. All five sections are
**OPTIONAL** for v2 readers — they live above the existing section 5
(metadata) and below the end marker, and any v2 reader that does not
recognize them simply skips by `section_length` per the existing
"unknown section" rule.

- v1 snapshots MUST NOT contain these sections.
- v2 snapshots MAY omit any subset of them (`make_test_snapshot.py` always
  emits all five for the synthetic fixture; `lua_bridge.cpp::Lua_DumpState`
  currently emits none of them — capture-side support is future work).
- All multi-byte fields are **little-endian**, the same as the existing
  v1 sections.
- `char[N]` fields are null-padded ASCII (the same convention used by
  sections 1-5).

### Section 6 — `planet_state` (ID 0x00000006)

```
uint32 planet_count
for i in 0..planet_count:
    char    name[64]
    float32 corruption
    int32   owner_slot   // -1 = no owner
```

Per-record size: `64 + 4 + 4 = 72 bytes`. Section length:
`4 + 72 * planet_count`. Read into `g_replay.planets`, keyed by uppercased
name. Backs `SWFOC_ReplayPlanetCorruption` and the per-planet ownership
that future iterations of the Planet helpers may consume.

### Section 7 — `diplomacy` (ID 0x00000007)

```
uint32 pair_count
for i in 0..pair_count:
    char  faction_a[32]
    char  faction_b[32]
    char  state[16]      // "allied" / "hostile" / "neutral"
```

Per-record size: `32 + 32 + 16 = 80 bytes`. Section length:
`4 + 80 * pair_count`. Faction pairs are stored as written; the reader
normalizes both names to upper-case and stores them in
`g_replay.diplomacy` keyed by `(min, max)` so lookups are symmetric.

### Section 8 — `cooldowns` (ID 0x00000008)

```
uint32 type_count
for i in 0..type_count:
    char     type_name[64]
    uint32   ability_count
    float32  cooldown[ability_count]
```

Per-record header: `64 + 4 = 68 bytes` plus `4 * ability_count` payload.
Section length: `4 + sum(68 + 4*ability_count)` across all types.
The reader stores them in `g_replay.cooldowns[type_name]` as a
`vector<float>`. The mutation seam grows the vector on demand and
zero-pads any intermediate slots.

### Section 9 — `task_forces` (ID 0x00000009)

```
uint32 force_count
for i in 0..force_count:
    int32 owner_slot
    char  name[64]
```

Per-record size: `4 + 64 = 68 bytes`. Section length:
`4 + 68 * force_count`. Read into `g_replay.task_forces` and observed via
`SWFOC_ReplayTaskForceCount(slot)`.

### Section 10 — `object_owners` (ID 0x0000000A)

```
uint32 type_count
for i in 0..type_count:
    char    type_name[64]
    uint32  instance_count
    int32   owner_slot[instance_count]
```

Per-record header: `64 + 4 = 68 bytes` plus `4 * instance_count` payload.
Section length: `4 + sum(68 + 4*instance_count)` across all types.
Mirrors section 3 (`object_catalog`) with per-instance owner slots.
Backs `SWFOC_ReplayUnitOwner(type, index)`. The mutation seam
`SWFOC_ReplaySpawnUnit(faction, type, count)` appends to this map AND
bumps the matching `g_replay.objects[type]` entry so the existing
`SWFOC_ReplayObjectCount` observer continues to report the right total.

### New helpers added 2026-04-08

```cpp
// 9 observers
SWFOC_ReplayPlayerCredits(faction)            -> number  (-1 if missing)
SWFOC_ReplayPlayerTechLevel(faction)          -> number  (-1 if missing)
SWFOC_ReplayLastStoryEvent()                  -> string  ("" if none)
SWFOC_ReplayDiplomaticState(a, b)             -> string  ("hostile" default)
SWFOC_ReplayPlanetCorruption(planet)          -> number  (-1 if missing)
SWFOC_ReplayUnitOwner(type, index)            -> number  (-1 if oob)
SWFOC_ReplayCooldownState(unit_type, idx)     -> number  (-1 if missing)
SWFOC_ReplayTaskForceCount(slot)              -> number
SWFOC_ReplayHumanPlayerSlot()                 -> number  (-1 if unset)

// 7 mutation seams
SWFOC_ReplayPushStoryEvent(event)             -> 1 / 0
SWFOC_ReplaySetDiplomacy(a, b, state)         -> 1 / 0
SWFOC_ReplaySetPlanetCorruption(planet, val)  -> 1 / 0
SWFOC_ReplaySpawnUnit(faction, type, count)   -> 1 / 0
SWFOC_ReplaySetCooldown(unit_type, idx, val)  -> 1 / 0
SWFOC_ReplayAddTaskForce(slot, name)          -> 1
SWFOC_ReplaySwitchLocalPlayer(slot)           -> 1 / 0  (-1 = "no local player")
```

All 16 helpers are registered against the replay's fake Lua state by
`replay_harness.cpp::RegisterAll`, AND pattern-matched in
`ReplayLoad` so simple `return SWFOC_Replay*(...)` commands sent over
the named pipe execute end-to-end without going through the
fake_pcall stub.
