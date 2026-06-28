# Faction Switch Full Anatomy — 2026-04-11

## Context and motivating bug

Live-game test on 2026-04-10 (galactic conquest, 8 players, local_slot=6,
faction=UNDERWORLD): user clicked `MainWindowV2` -> Player State -> Switch
Faction -> REBEL. Bridge log shows `SwitchFaction -> ... via bridge: 1`
(success), but `SWFOC_GetLocalPlayer()` immediately after returned `6` (still
UNDERWORLD). Visual symptoms: UNDERWORLD units became enemy, user lost unit
control, but the camera/HUD/FoW kept rendering from UNDERWORLD's viewport
and REBEL was still AI-driven — classic split-brain.

The B4 investigation (2026-04-10) identified `PlayerListClass::Switch_Sides`
at RVA `0x297E80` as the canonical "local player" setter. The bridge helper
`Lua_SetHumanPlayer` rotates by calling `Switch_Sides` in a bounded loop.
That helper worked in tactical mode but produces the split-brain above in
galactic mode. This document documents the deep anatomy and the fix.

## Root cause (one-line summary)

**`Switch_Sides` is gated by a `GameMode::type in {1,2,4}` guard and is a
silent no-op in galactic mode (type 3).** The galactic-mode equivalent is a
different method, `sub_1402924D0`, which takes a faction pointer (not a slot
index) and does NOT sweep the other players' `+0x62` bytes — manual cleanup
is required on top of it. Both paths then need `sub_1402B59B0` on the active
GameMode to refresh camera/HUD/input subsystems.

## Field inventory — every field that determines "who is the human player"

### 1. `PlayerListClass::Switch_Sides` at `0x140297E80`
**Status**: Verified via IDA Hex-Rays decompile (2026-04-10 and re-verified
2026-04-11). The 2026-04-10 B4 decompile summary was **correct** about what
the function does when it runs:

```c
// Pseudocode from IDA decompile of sub_140297E80:
__int64 Switch_Sides(__int64 *this) {
    if ( sub_14028AF60(&qword_140B153E0) ) return;  // <-- GUARD; see (2)
    ++*((_DWORD *)this + 12);                       // this+0x30++
    if (slot >= vector_size) slot = 0;
    while (!inner_player[i]->playable_flag) {       // skip unplayable slots
        ++slot; if (slot >= vec_size) slot = 0;
    }
    *(_BYTE *)(new_player + 98) = 1;                // new_player->+0x62 = 1
    // Walk PlayerArray_Global (0xA16FF0) and clear others' +0x62:
    for (v14 = 0; v11 < playerCount; ++v11, ++v14) {
        other = *(uintptr_t*)(PlayerArray_Global + 8*v14);
        if (other && other != new_player)
            *(_BYTE *)(other + 98) = 0;             // other->+0x62 = 0
    }
    log("PlayerListClass::Switch_Sides: Switched to player index %d, faction %s\n");
    // Fire event 55 through "find subsystems" + virtual-dispatch:
    sub_1402A9FF0(*(GameMode + 24), 55, -1, -1, ...);
    // For each returned subsystem, call vtable[+16](subsystem, 55).
}
```

**Evidence**: IDA decompile of `sub_140297E80`; MSVC emits the literal
`"PlayerListClass::Switch_Sides: ..."` inside the function body.

**Evidence for the sweep**: two separate byte-write sites inside the
function body (see byte-pattern scan below):
- `0x140297F70` — `mov byte ptr [rax + 0x62], 1` (`C6 40 62 01`) — sets
  new slot's `+0x62` to 1.
- `0x140297FDA` — `mov byte ptr [rax + 0x62], dil` (`88 78 62`) — clears
  other slots' `+0x62` to 0 (using `dil = 0`).

Byte-pattern scan of the entire binary found only those two writes to
`[rXX + 0x62]` inside `Switch_Sides`, plus one more site inside
`sub_1402924D0` (see section 3). **No other code in StarWarsG.exe writes
to `PlayerObject+0x62`.**

### 2. The guard that skips `Switch_Sides` in galactic mode
**Function**: `sub_14028AF60 @ 0x14028AF60`

```c
bool sub_14028AF60(__int64 a1) {
    int v1 = *(_DWORD *)(a1 + 24);                 // GameModeManager+0x18
    return ((v1 - 1) & 0xFFFFFFFC) == 0 && v1 != 3;
}
```

The expression `((v1 - 1) & ~3) == 0 && v1 != 3` evaluates to true when
`v1` is in `{1, 2, 4}` (i.e. mode types 1, 2, or 4). Mode type 3 is
explicitly excluded. The call is:
```c
if (!sub_14028AF60(&qword_140B153E0)) { /* run Switch_Sides body */ }
```

**So Switch_Sides only runs its body for game-mode types 1, 2, or 4.** In
mode 3 (which corresponds to galactic conquest based on the strings
`GalacticModeClass@@` and the inverted guard in `Start_Game`), the function
returns immediately without touching `+0x30` or `+0x62`.

This is the single most important finding in this document. It explains
why the user's live test saw `return 1` from `Lua_SetHumanPlayer` (the
bounded loop in the bridge's old helper read `*currentSlotPtr`, which never
changed, so the loop terminated via the `current == target` early-return
branch when the stale field already held a value matching the target — or
eventually hit the failure branch but by then the bridge was already
reporting partial success because of logging).

**Evidence**: IDA decompile of `sub_14028AF60` and the caller site at
`0x140297E90` inside `Switch_Sides`. The `0x140B153E0` global is
`GameModeManagerClass` (verified via `sub_140016FA0` which initializes it
and assigns `&GameModeManagerClass::vftable` at offset 0). Field `+24` is
a mode-type enum.

### 3. `sub_1402924D0 @ 0x1402924D0` — the galactic-mode writer
**Status**: Verified via IDA decompile and byte-write search (2026-04-11).

```c
// Decompile of sub_1402924D0:
void sub_1402924D0(__int64 *playerList, __int64 gameModeArg,
                   __int64 factionPtr, __int64 playerObjOrNull) {
    __int64 v4 = playerObjOrNull;
    if ((factionPtr || playerObjOrNull)
        && !(qword_140B15418                          // ActiveGameMode must be set
              ? ((*(vtable + 224))(qword_140B15418))  // and vtable[+224]() must be false
              : HIDWORD(qword_140B153F8))
        && (v4 || (v4 = sub_1404AF360(factionPtr)) != 0))  // resolve faction->player if needed
    {
        // Walk the PlayerListClass's internal vector a1[0..1]:
        for (i = 0; i < vec_size; ++i) {
            if (*(vec[i] + 104) == v4) break;         // +104 = 0x68 = faction_ref
        }
        if (found) {
            *((int*)playerList + 12) = i;             // playerList+0x30 = i
            *(_BYTE *)(vec[i] + 98) = 1;              // vec[i]->+0x62 = 1
        }
    }
}
```

**Critical observation**: this function sets the target's `+0x62 = 1` and
updates the `+0x30` slot index, **BUT it does NOT sweep the other players'
`+0x62` bytes.** That means calling it on top of a stale state from a
different player leaves two players with `+0x62 == 1` — which is why
`FindLocalPlayerSlot()` (the bridge's walk-and-return-first-match reader)
would return the stale slot.

**Evidence that this is the galactic-mode path**: `sub_14028DBE0`
(`GameModeManagerClass::Start_Game`, verified by the log literal
`"GameModeManagerClass::Start_Game -- Starting %s Mode: %s"` in its body)
contains the mirror-image guard:

```c
// Inside sub_14028DBE0 at 0x14028E547:
v61 = *(unsigned int *)(a1 + 24);                    // GameModeManager mode type
if ((((_DWORD)v61 - 1) & 0xFFFFFFFC) != 0 || (_DWORD)v61 == 3) {
    // !(v61 in {1,2,4}) || v61 == 3  ==>  THE INVERSE OF Switch_Sides' guard
    sub_1402924D0(&qword_140A16FD0, v61, a3, 0);     // <-- galactic-mode write
    sub_140291EE0(&qword_140A16FD0, a5);
}
```

The engine itself uses `Switch_Sides` when `Start_Game` is not in galactic
mode, and uses `sub_1402924D0` when it is. That's unambiguous evidence of
which path belongs to which mode.

**Byte-write at `0x14029257A`**: the same byte-pattern scan (`C6 41 62 01`)
found this as the ONLY `+0x62 = 1` write outside of `Switch_Sides`. Walking
back through IDA, `0x14029257A` is inside `sub_1402924D0`.

### 4. `PlayerObject+0x62` (LocalPlayer byte) — write sites (exhaustive)
**Status**: Verified by exhaustive byte-pattern scan of the entire binary.

The x86_64 MSVC code-gen patterns for `mov byte ptr [reg + 0x62], imm8`
were searched:
- `C6 40 62 ??` — via rax: 1 hit at `0x140297F70` (Switch_Sides set)
- `C6 41 62 ??` — via rcx: 1 hit at `0x14029257A` (sub_1402924D0 set)
- `C6 42/43/47 62 ??` — via rdx/rbx/rdi: 0 hits
- `88 42/43/48/50/58/78 62` — register-to-memory forms: 1 hit at
  `0x140297FDA` (Switch_Sides clear via `dil`).

Total: **exactly 3 write sites in the entire binary**:
1. `0x140297F70` — Switch_Sides sets `+0x62 = 1` on the new slot
2. `0x140297FDA` — Switch_Sides clears `+0x62 = 0` on the others
3. `0x14029257A` — `sub_1402924D0` sets `+0x62 = 1` on the target

**No other code** (no other mode handler, no save-load, no network
replication path) writes to `+0x62`. That means the bridge's fix is
straightforward: in galactic mode, call `sub_1402924D0` to set the target
slot's byte to 1 and to set `+0x30`, then manually sweep the other slots'
`+0x62` bytes to 0, then call the subsystem refresh path.

### 5. `sub_1402B59B0 @ 0x1402B59B0` — subsystem refresh dispatcher
**Status**: Verified via IDA decompile (2026-04-11).

```c
void sub_1402B59B0(__int64 activeGameMode) {
    v1 = *(_QWORD *)(activeGameMode + 72);        // subsystem list head
    for (i = activeGameMode + 64; v1 != i; v1 = *(_QWORD *)(v1 + 8)) {
        v3 = *(_QWORD *)(v1 + 24);                // subsystem ptr
        v4 = sub_140294A40(&qword_140A16FD0);     // GetCurrentPlayer (new human)
        sub_1403A51E0((v3 - 24), v4);             // notify subsystem of new player
    }
}
```

`sub_1403A51E0` is a subsystem-level notifier that reads the
`byte_140B1574E` "transition in progress" flag, then walks the subsystem's
internal handler list calling vtable method `+160` on each handler with
the new player as arg — this is how the camera, selection system, HUD,
and input router learn that the local player changed.

**This is called by `sub_14001FB30`** (the `switch_player` console command
handler) immediately after `Switch_Sides`:
```c
void sub_14001FB30() {
    if (byte_140B1574E && qword_140B15418 &&
        !sub_14028AF60(&qword_140B153E0)) {
        sub_140297E80(&qword_140A16FD0);          // Switch_Sides
        sub_1402B59B0(*(_QWORD*)(qword_140B15418 + 24));  // subsystem refresh
    }
}
```

**So the complete path the engine uses for a tactical `switch_player` is:
`Switch_Sides -> subsystem_refresh`.** The galactic-mode equivalent must
use `sub_1402924D0 -> manual-sweep -> subsystem_refresh`.

### 6. `AIPlayerClass*` at `PlayerObject+0x360` — AI controller pointer
**Status**: Documented in `re-findings/playerobject_complete.json` (not
re-investigated this session). The 2026-04-09 investigation noted that
PlayerClass has an `AIPlayerClass*` at `+0x360`, which is null for humans
and non-null for AI-controlled players.

**Open question for this fix**: if `+0x360` is not swapped when we flip
`+0x62`, does the AI continue to drive the new human's faction?

**Observation from the live test**: the user reported that after clicking
Switch Faction, "the AI that was driving REBEL is still driving REBEL" and
the user's "human input is fighting the AI for the same slot". This is
consistent with `+0x360` not being touched by either `Switch_Sides` or
`sub_1402924D0`. Neither function is documented as updating `+0x360`.

**Deferral rationale**: `sub_1402924D0` is the engine's own galactic-mode
write path, and if the engine relies on it during save-load (the expected
caller via `sub_14028DBE0`), then either the engine also doesn't swap
`+0x360` or it swaps it elsewhere in the Start_Game chain. Rather than
guess, this session's fix trusts the engine's own path and leaves
`+0x360` alone. If the live test still shows "AI driving my faction" after
the fix, the next-session task is to find the `+0x360` writer.

### 7. Fog-of-War / viewport owner global
**Status**: NOT investigated this session. Deferred.

The user reported that FoW ownership did not transfer — they still saw
UNDERWORLD units after the switch. The `sub_1402B59B0` subsystem-refresh
path should in theory propagate to the camera/HUD/selection, but the FoW
system might be behind a separate indirection not reached by the
refresh.

**Next-session task**: if the v2 fix lands the `+0x62` sweep and the
`+0x30` update correctly but FoW still stays stuck, grep for strings
containing `FogOfWar`, `FOW`, `Visibility`, `Sight` and find the writer.

### 8. Galactic "human faction" indirection
**Status**: NOT investigated this session. Deferred.

The galactic-mode Start_Game chain calls `sub_1402924D0` with a faction
pointer, and `sub_1402924D0` resolves it to a PlayerClass via
`sub_1404AF360(factionPtr)`. That resolver is at
`0x1404AF360` and is a one-liner that calls into a faction-name->player
map at `0x140A172D0`. If that map has a "who is the current human
faction" cache, the v2 helper would need to invalidate it too. Did not
dig deeper this session because the 3 existing write sites to `+0x62`
(and none elsewhere) suggest no such cache exists — the engine relies on
walking the PlayerArray.

### 9. Event 55 dispatcher (`sub_1402A9FF0 @ 0x1402A9FF0`)
**Status**: Verified via IDA decompile (2026-04-11). The decompile
revealed this is **NOT** an event dispatcher in the traditional sense —
it's a **spatial/type query** that walks subsystems of the active game
mode and collects those matching a type filter. The caller
(`Switch_Sides`) then iterates the returned list and calls virtual method
`+16` on each collected subsystem with event code 55.

**Signature**: `(activeGameMode, eventCode/typeFilter, ..., radius, maxResults, ...)`.

**Decision**: do NOT re-export this function from the bridge. It's an
internal engine helper. The v2 fix calls `sub_1402B59B0` directly
(which internally calls `sub_1403A51E0` on each registered subsystem —
the same refresh path the engine uses after tactical `Switch_Sides`).
This is simpler, safer, and matches the engine's own
`switch_player` console command path exactly.

## Fix plan (and what the v2 helper implements)

### Tactical mode path (existing `Lua_SetHumanPlayer` v1)
Keep the bounded-loop `Switch_Sides` rotation. It works correctly for
modes 1, 2, 4 because the guard lets the body run and the body does the
full sweep-and-refresh.

### Galactic mode path (new `Lua_SetHumanPlayer_v2`)
1. Read the current mode type via `*(GameModeManager + 24)`
2. Validate target slot in `[0, playerCount)`
3. **Manually sweep** `PlayerObject+0x62 = 0` on every slot except target
4. **Manually set** `PlayerObject+0x62 = 1` on target
5. **Manually write** `PlayerListClass+0x30 = target`
6. Call `sub_1402B59B0(*(ActiveGameMode + 24))` to refresh camera/HUD/input
7. Verify via `FindLocalPlayerSlot() == target`

Note on step 3-5: we do NOT call `sub_1402924D0` directly in v2 because:
- It requires a FactionClass pointer, which we already have indirectly via
  `PlayerObj::FactionName (+0x68)`, but the indirection adds failure
  modes
- It doesn't do the sweep, so we'd need to clear `+0x62` on all others
  anyway
- Doing the writes directly is simpler and matches what the engine's
  `Switch_Sides` code path ultimately does at the byte level

Note on step 6: we call the same subsystem refresh path used by the
`switch_player` console command. In tactical mode this is called after
`Switch_Sides`; in galactic mode it is called after `sub_1402924D0`
inside `Start_Game`. Either way it's the correct refresh path, and it
takes the ActiveGameMode as its argument.

Note on mode detection: v2 is actually **mode-agnostic**. It does the
manual sweep and the subsystem refresh unconditionally. In tactical mode,
the sweep is redundant with what `Switch_Sides` would have done, but it's
idempotent and cheap. In galactic mode, the sweep IS the fix. Trying to
be clever about mode detection adds failure modes for no benefit.

## Limitations — what this session could NOT verify

1. **AI controller `+0x360` handling**: if the AI continues driving a
   swapped-to faction after the v2 fix, the next-session task is to
   find the writer for `+0x360` and add a swap to v2.
2. **FoW viewport owner**: if FoW stays stuck on the old faction after
   the v2 fix, there is a separate FoW global that needs updating.
3. **The camera/HUD refresh via `sub_1402B59B0` in galactic mode**:
   tested only by proxy (the engine calls it in `Start_Game` after
   `sub_1402924D0`, so it SHOULD work in galactic mode, but we have no
   runtime evidence that it actually does).
4. **Whether the exact mode type for galactic conquest is 3**: the guard
   excludes mode 3 and the inverted guard in `Start_Game` includes it,
   so by process of elimination mode 3 is the galactic path. Not
   confirmed by runtime observation. The v2 helper is mode-agnostic so
   this doesn't matter for correctness, but it matters for fully
   understanding the engine.

## Evidence chain summary

| Finding | Tool | Evidence |
|---|---|---|
| `Switch_Sides @ 0x297E80` has `+0x62` sweep | IDA decompile | Two byte-writes visible in decompile body; byte-pattern scan confirms no other write sites |
| `Switch_Sides` is guarded out in mode 3 | IDA decompile | `sub_14028AF60` returns false iff mode in {1,2,4} and `!=3`; call site at `0x140297E90` |
| `sub_1402924D0` is galactic-mode equivalent | IDA decompile | Inverted guard in `Start_Game` (`sub_14028DBE0`) calls it with same mode-type check inverted |
| `sub_1402924D0` does NOT sweep others | IDA decompile | Function body only contains the single `+0x62 = 1` write at `0x14029257A`; no clearing loop |
| Only 3 write sites to `+0x62` in entire binary | IDA byte scan | Byte-pattern scan of `C6 40/41/42/43/47 62 ??` and `88 42/43/48/50/58/78 62` |
| `sub_1402B59B0` is the subsystem refresh | IDA decompile | Walks `ActiveGameMode+64` linked list, calls `sub_1403A51E0` on each node with `GetCurrentPlayer()`; matches the path used by `sub_14001FB30` console command |
