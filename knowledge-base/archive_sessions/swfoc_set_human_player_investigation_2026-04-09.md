# SWFOC_SetHumanPlayer Investigation — 2026-04-09

## RESOLVED 2026-04-10 — canonical answer is `PlayerListClass::Switch_Sides`

IDA Pro MCP came online and the investigation is complete. The correct API is **not** Option A (standalone global) or Option B (per-object byte flip) — it is Option C that nobody considered: an existing engine class method that encapsulates the entire side-effect chain.

**`PlayerListClass::Switch_Sides` at RVA `0x297E80`** (image base `0x140297E80`):
- Increments the current-slot `int` at `PlayerListClass+0x30`, wraps at vector end
- Skips players whose inner `+0x10A` playable flag is 0
- Sets new slot's `PlayerObject+0x62` (LocalPlayer) to 1
- Iterates entire PlayerArray and clears every other player's `+0x62` to 0
- Logs `"PlayerListClass::Switch_Sides: Switched to player index %d, faction %s"`
- Fires event 55 via `sub_1402A9FF0(*(GameMode + 24), 55, ...)` → camera/HUD/input router refresh
- Caller `sub_14001FB30` (SwitchSidesCommand::execute) then calls `sub_1402B59B0(ActiveGameMode)` for subsystem refresh

**Why direct `+0x30` write or manual `+0x62` flip is WRONG**: both bypass event 55 dispatch and leave camera/HUD/input router in a stale state. The class method is the only correct path.

**Evidence**: MSVC-emitted literal `"PlayerListClass::Switch_Sides: ..."` inside the function body is unambiguous function-identity evidence. `xrefs_to 0x140297E80` returned 5 code callers across 3 distinct functions (debug console, game-mode code at `0x1400C0110`, main loop at `0x140456970`) — core engine API, not debug-only. Runtime witness: the `switch_player` console command (string at `0x1407FFA60`, vtable `SwitchSidesCommand::vftable` at `0x1407FFA38`) invokes this path.

**Ledger entries added 2026-04-10** (via `tools/phase_b4_switch_sides.py`, all VERIFIED with 2-tool consensus `ida_pro + cheat_engine`):
- `rva_player_list_switch_sides` (0x297E80)
- `rva_player_list_get_current_player` (0x294A40) — companion reader, cross-validates +0x30
- `rva_switch_sides_command_vtable` (0x7FFA38) — console command vtable anchor
- `struct_player_list_current_slot_offset` (PlayerListClass+0x30)
- `struct_player_local_flag` — updated with `lua_runtime` evidence (Switch_Sides is the canonical setter of +0x62)

**Next-session wiring (deferred)**: implement `SWFOC_SetHumanPlayer(targetSlot)` in `swfoc_lua_bridge/lua_bridge.cpp` as a loop: read current slot via GetCurrentPlayer, call Switch_Sides until it lands on targetSlot (or bail after N iterations to avoid infinite loop on unplayable targets). Rewrite `FactionSwitchService` in the editor to call the new bridge helper. Regression pair per project conventions. This should happen during the live-game validation pass (handoff Section 6 priority #1).

---

## Original 2026-04-09 investigation (kept for history)

**Phase**: B4 (FactionSwitchService unblock research)
**Status**: `BLOCKED-NEEDS-LIVE-INVESTIGATION` (superseded by 2026-04-10 resolution above)
**Investigator**: autonomous pass, no IDA/Ghidra/BinaryNinja MCP available

## Goal

Find a write path to change which player slot the engine treats as the local
human, so `FactionSwitchService.BuildLuaCommand` can emit a real bridge call
instead of the current `error("BLOCKED-MEMORY: ...")` marker.

Target was one of:
- **Option A**: A standalone `uint32` global holding "current human player slot"
- **Option B**: Per-slot byte flipping of `PlayerClass +0x62 LocalPlayer`
  backed by evidence that doing so is sufficient (i.e. the engine itself
  flips this byte when it reassigns human control).

## Result

Neither option is supported by on-disk evidence. The local_player byte
`PlayerClass +0x62` is well-known as a **read-side indicator** (`FindLocalPlayerSlot`,
god_mode / OHK caves, `Is_Local_Player` Lua binding) but **no on-disk source
writes it**, and no standalone "current human" global is cataloged.

## Searches performed

### 1. Trainer write-side audit

```
Grep: "switch.*player|set.*human|human.*player|local.*player|take.*faction
      |set_player|setlocal|SetHumanPlayer|LocalPlayer"
      in C:\Users\Prekzursil\Downloads\swfoc_memory\trainer\
```

- `SWFOC_GUI_Trainer_v3.lua` and `.CT` define `PO.LocalPlayer = 0x62` and read it
  in `getPlayerInfo`, `findLocalPlayerSlot`, drainCredits filters, and god_mode
  / OHK cave scripts. **None** of the 10 `safeWriteByte / safeWriteFloat /
  safeWriteInt` call sites writes to `info.ptr + PO.LocalPlayer`. The trainer
  has no "Switch Player", "Take Over", or "Become This Faction" button.

- `SWFOC_GUI_Trainer_v4.lua` same story: only `Find_Player("local")` Lua reads
  and `Give_Money` / `Set_Tech_Level` writes.

- `god_mode.lua`, `blueprints.lua`, `fow_toggle.lua`, `triggers.lua`,
  `lua_playground.lua` — all read-only with respect to `+0x62`.

### 2. RVA table audit

`swfoc_lua_bridge/rvas.h` globals block (lines 139-148):

```
PlayerListClass_Global   = 0xA16FD0
PlayerArray_Global       = 0xA16FF0
PlayerCount_Global       = 0xA16FF8
TheCommandBar            = 0xB27F60
TheGameText              = 0xA7BC58
DefaultHeroRespawnTime   = 0xB169F0
```

No `LocalPlayerIndex`, `HumanPlayerSlot`, `CurrentHuman`, or equivalent. The
`PlayerObj` namespace only names the byte offset `LocalPlayer = 0x62` as a
struct field, with no accompanying writer RVA.

### 3. Ledger audit (verified_facts.json)

Relevant hits:
- `rva_lua_is_human` / `rva_lua_is_local_player` — Lua-side getters, both
  read-only.
- `struct_player_ai_ptr` — PlayerClass +0x360 AIPlayerClass pointer; null for
  humans. Interesting read-side signal (an engine routine that **creates** a
  human might null this field, or **assigning** one might swap in an AI
  object), but no ledger entry documents a writer.
- `struct_player_local_flag` — PlayerClass +0x62 uint8, VERIFIED as the
  local_player flag, evidence chain is **read-only** (trainer caves + bridge
  FindLocalPlayerSlot + test_harness SetupPlayer() which writes it in a
  synthetic fake-memory fixture, NOT from the real engine).

No ledger entry documents a real-game code path that *writes* +0x62 or any
"become human" global.

### 4. re-findings audit

`re-findings/playerobject_complete.json` describes PlayerClass layout through
+0x4D0. It documents `+0x62` as the local_player flag but does not identify
which engine function sets it. No field-setter RVA is captured.

### 5. Bridge source audit

`swfoc_lua_bridge/lua_bridge.cpp`:
- `FindLocalPlayerSlot()` (line 463) iterates `PlayerArray` and returns the
  slot whose `+0x62 == 1`. **Read side only.**
- No `SWFOC_SetHumanPlayer` / `SetLocalPlayer` / `BecomePlayer` symbol exists
  anywhere in the bridge source.
- `replay_harness.cpp` has `Lua_ReplaySwitchLocalPlayer` but that operates on
  an in-memory `ReplayState` (see `replay_state.h::ReplayMutSwitchLocalPlayer`),
  not on the live game process.

## Why we refuse to guess

A naive "clear everyone's +0x62, set target's +0x62 = 1" write is tempting
but would almost certainly desync the engine:

1. `PlayerClass +0x360 AIPlayerClass*` is null for humans. If we flip the
   local byte without swapping this pointer, the target slot still has an AI
   controller attached and the former human now has none. Input router,
   goal system, and AI pump state will all disagree with the local flag.
2. Camera owner, selection system, UI HUD bindings, and likely the input
   router (mouse click-to-command routing) each cache "which player am I
   rendering for". The byte at +0x62 is an *indicator*, not the authoritative
   source — there is probably a dedicated setter that touches multiple
   subsystems in one shot.
3. The `CLAUDE.md` known-pitfalls section already warns that
   `PlayerObject.Playable` at +0x37 does not hold the values expected by
   first-principles reasoning. Trusting field semantics on PlayerClass without
   RE confirmation of the setter has bitten this project before.
4. The trainer was built by an experienced modder and specifically did not
   ship a "switch player" button — that is itself a negative signal.

Shipping `SWFOC_SetHumanPlayer` with an invented RVA or an unproven byte-flip
pattern would violate the project rule "no fabricated RVAs" and could
reasonably crash the game or corrupt save state.

## Next-session recipe (IDA-enabled)

Run in an IDA session with the funnel MCP connected. **Do these in order**:

### Step 1 — Find the setter by xref on +0x62

```
# In IDA Python / IDA Pro MCP
# Find all writes to [player + 0x62]. Expected pattern:
#   mov byte ptr [rcx+62h], 1     or    mov byte ptr [rXX+62h], 0
# Xref filter: writes only, byte-size store.
decompile_function(FindLocalPlayerSlot_site)  # starting landmark
find_byte_writes(struct_offset=0x62, field_size=1)
```

Expected result set: 1-3 functions. One of them is the setter. Decompile
each and look for the one that **also**:
- Touches `AIPlayerClass*` at +0x360 (null out or set)
- Writes a global near `PlayerArray_Global` (0xA16FF0 / 0xA16FF8) region
  — there may be a sibling global at 0xA17000+ for "local human slot"
  that we haven't cataloged
- Calls into the camera system, selection manager, or HUD/UI notify

### Step 2 — Find the "current human" global (if one exists)

```
# Grep IDA strings for: "local player", "human player", "SetLocalHuman",
#                      "SetLocalPlayer", "Local_Player"
strings_search("local.*player")
strings_search("human.*player")
# For each hit, get xrefs, decompile the consumer.
# A global load pattern like:
#   mov eax, dword ptr [rip + <offset>]   ; where offset is in .data near A16FF0
# is the candidate.
```

### Step 3 — Cross-validate with the Lua binding

```
# decompile Is_Local_Player (rva_lua_is_local_player) and Is_Human
# (rva_lua_is_human) — these are confirmed-safe read-side references.
# Their implementations reveal whether the "is local" answer is derived
# purely from +0x62 or from a separate global the setter writes.
decompile_function(rva_lua_is_local_player)
decompile_function(rva_lua_is_human)
```

If `Is_Local_Player` reads a global instead of walking `PlayerArray` looking
for +0x62 == 1, that global IS the target and its RVA is the answer.

### Step 4 — Optional runtime confirm via Cheat Engine

Before trusting any RVA from Step 1-3:
1. Attach CE to a live skirmish.
2. Scan for `base + candidate_rva` as uint32, value = current human slot (e.g. 0 for REBEL).
3. Start a fresh skirmish as EMPIRE. Rescan changed-to-1 (or whichever slot).
4. If the scan converges on the candidate RVA, write it and observe: does
   the HUD / camera / unit-selection also switch? If yes → real global,
   ship it. If no → it's an indicator like +0x62, keep hunting.

### Step 5 — Ledger entry requirements

Before adding to `verified_facts.json`, require **two-tool consensus**:
- `ida_pro`: decompile_function evidence showing the setter + xref
- `cheat_engine`: live runtime write + observable HUD/camera switch

Mark as `VERIFIED` with category `global_pointer` (if standalone) or extend
`struct_player_local_flag` with a `write_path` section (if the setter is a
PlayerClass method that touches multiple fields atomically).

## Ledger action for this session

Add a `BLOCKED-NEEDS-LIVE-INVESTIGATION` marker on the research question:

```json
"research_swfoc_set_human_player": {
  "category": "research_blocker",
  "claim": "Write path to reassign the local human player slot at runtime.",
  "confidence": "BLOCKED-NEEDS-LIVE-INVESTIGATION",
  "status": "No on-disk evidence of a writer for PlayerClass +0x62 or of a standalone current-human global. Trainer, bridge, ledger, and re-findings are all read-side only. See knowledge-base/swfoc_set_human_player_investigation_2026-04-09.md for the next-session recipe.",
  "blocked_on": ["ida_pro decompile of +0x62 byte writers", "cheat_engine runtime scan for human-slot global"],
  "investigated": "2026-04-09"
}
```

## Files left untouched (per Option C rules)

- `swfoc_lua_bridge/lua_bridge.cpp` — no new helper
- `swfoc_lua_bridge/rvas.h` — no new constant
- `swfoc_lua_bridge/test_harness.cpp` — no new suite
- `FactionSwitchService.cs` — remains on `error("BLOCKED-MEMORY: ...")` marker
- `feature_readiness_matrix_2026-04-09.md` — FactionSwitch stays BLOCKED-MEMORY
- `verified_facts.json` — only the research_blocker row above (if the next
  editor pass decides to record it; this doc alone is sufficient as an audit
  trail for the B4 session)

## Bottom line

The live game state for "who is the human" is not captured anywhere on disk
as a writable address. The byte at +0x62 is provably the *read-side* marker
but the *write path* requires a live IDA session plus CE runtime validation
before shipping.
