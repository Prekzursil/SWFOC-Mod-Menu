# Selection Pointer Discovery -- 2026-04-11

## Summary

Found the canonical read path for the "currently selected game object(s)" via
IDA Pro MCP static decompilation of the Lua `Select_Object` wrapper and its
down-stream callees. Every RVA below was verified live against the loaded
`StarWarsG.exe` IDB during the 2026-04-11 session (IDA Pro MCP uptime
`14085 s`). The selection list is per-player and lives inside the active
`GameModeClass` at a fixed offset; no runtime pointer scanning is required.

## Pointer chain (authoritative)

```text
1. mgr_root   = *(uintptr_t*)(g_base + 0xB15418 + 0x18)
                i.e. the qword at g_base + 0xB15430.
                Alias: qword_140B15418 in IDA, "_GameModeRoot_Global" here.

2. slot_id    = sub_140294A70(g_base + 0xA16FD0)
                PlayerListClass_Global + helper -> int32 of current human
                player's slot id (reads *(pl + 0x30), then
                *(vec_begin[idx] + 0x4C)).

3. sel_list   = *(uintptr_t*)(mgr_root + 0x1C0) + 0x48 * slot_id
                i.e. sub_1402AD080(mgr_root, slot_id) — two insns total.

4. head       = *(uintptr_t*)(sel_list + 0x10)
   sentinel   = sel_list + 0x08
   while (head != sentinel) {
       intrusive = *(uintptr_t*)(head + 0x18)
       obj       = intrusive - 0x18     // (skip RefCount header)
       if (obj) emit(obj)
       head      = *(uintptr_t*)(head + 0x08)
   }
```

`mgr_root + 0x1C0` is an array of 72-byte (`0x48`) DynamicVector headers,
one per player slot. Each header is a doubly-linked list of weak refs to
`GameObjectClass`. The `-0x18` adjustment is because the list nodes point
into the object at the `RefCountClass` sub-part rather than at the base.

## IDA Pro evidence

### `sub_14003AFE0` -- canonical "any selected?" reader

```c
char sub_14003AFE0() {
    int slot = sub_140294A70(&PlayerListClass_Global);
    __int64 vec = sub_1402AD080(*(qword_140B15418 + 24), slot);
    if (!vec) return 0;
    __int64 v2 = *(_QWORD *)(vec + 16);
    __int64 sentinel = vec + 8;
    if (v2 == sentinel) return 0;
    while (1) {
        __int64 v4 = *(_QWORD *)(v2 + 24);
        __int64 obj = v4 - 24;
        if (!v4) obj = 0;
        if (!sub_14039AD40(obj)) break;   // "not dead yet" validator
        v2 = *(_QWORD *)(v2 + 8);
        if (v2 == sentinel) return 0;
    }
    return 1;
}
```

This is the minimal reference for our helper -- it exactly mirrors the
chain above. The helper uses the raw pointer directly and skips
`sub_14039AD40` (it is a multi-vtable validator that is unsafe to replay
without the full engine-state context -- the C# side re-validates via
`SWFOC_InspectUnit`).

### `sub_1402AD080` -- per-player selection-list accessor

```c
__int64 sub_1402AD080(__int64 a1, int a2) {
    return *(_QWORD *)(a1 + 448) + 72LL * a2;
}
```

Two-instruction function. `a1 + 448` = `mgr_root + 0x1C0`, entry width = 72 =
`0x48`. This is the direct witness for the formula.

### `sub_140603F60` -- Lua `PlayerWrapper::Lua_Select_Object`

```c
v11 = *(player_ptr + 76);
sub_1402BD2F0(*(qword_140B15418 + 24), *(v12 + 80), v11);
v13 = sub_1402AD080(*(qword_140B15418 + 24), v11);
// iterate v13 as a doubly-linked list, copy each obj into a DynamicVector
```

This is the write-side path (the Lua wrapper that ADDS to the selection).
It proves that `qword_140B15418 + 0x18` is the live manager and that
`sub_1402AD080` is the correct accessor.

### `sub_140294A70` -- current human player slot

```c
__int64 sub_140294A70(__int64 pl) {
    if ((_QWORD)(*(pl + 8) - *(pl + 0)) >> 3 == 0) return -1;
    void* vec_begin = *(_QWORD *)pl;
    int   cur_idx   = *(int *)(pl + 48);        // +0x30
    void* player    = *(_QWORD *)(vec_begin + 8 * cur_idx);
    if (!player) return -1;
    return *(unsigned int *)(player + 76);       // +0x4C
}
```

Already verified previously -- `PlayerListClass_Global + 0x30` is the
current-slot integer and `Diag_SelfTest` in `lua_bridge.cpp` uses it.

## RVAs added

| Name                        | RVA        | Type            | Evidence |
|-----------------------------|------------|-----------------|----------|
| `GameModeRoot_Global`       | `0xB15418` | data pointer    | decompile `sub_140603F60` + `sub_1402BD360` |
| `GetCurrentPlayerSlot`      | `0x294A70` | `sub_140294A70` | decompile verified above |
| `GetPerPlayerSelectionList` | `0x2AD080` | `sub_1402AD080` | decompile verified above |

All three RVAs were confirmed to be function / data starts via
`lookup_funcs`. No sub-function-start anomalies. `GameModeRoot_Global` and
`PlayerListClass_Global` overlap in domain with the earlier
`camera_selection_system.json` findings.

## Alignment with prior work

`re-findings/camera_selection_system.json` (2026-04-07 agent 3I) documented:

- `GameModeManagerClass_Global = 0xB153E0` -- a different, higher-level
  global. Our chain does NOT use it. `0xB15418` is a sibling pointer the
  selection system uses directly.
- `GameModeClass + 0x1C0-0x1D8` as a "DynamicVector<DynamicVector<GameObject*>>"
  suspected layout. Our evidence refines this: the outer container is a
  flat C array at `+0x1C0`, the inner is a 72-byte DynamicVectorClass, and
  the stride is exactly 72 (not a variable-length vector-of-vectors).

The 2026-04-07 agent marked this as "DISCOVERED -- needs runtime
verification". The 2026-04-11 IDA decompile upgrades it to
`VERIFIED-IDA-STATIC` for both the selection-list formula and the mgr-root
pointer-chain hop.

## Usage in the bridge

`Lua_GetSelectedUnit` and `Lua_GetSelectedUnits` in `lua_bridge.cpp` walk the
chain above and either return the first valid entry (single-unit mode) or a
comma-separated list (multi-unit mode). Both use `IsBadReadPtr` guards at
every hop so a desync between the game-state thread and the pipe drain
thread degrades to returning `0` / empty rather than crashing the game.

## Limitations

1. We iterate the raw linked list without calling `sub_14039AD40`
   (the "is the unit still alive" validator). Objects that are mid-death
   may still be in the list -- the caller is responsible for validating via
   `SWFOC_InspectUnit`.
2. The formula was verified against static decompile only. Runtime
   round-trip confirmation (select a unit in-game, read back the pointer,
   compare against `Find_Selected` or a CE scan) is the next-session TODO.
3. Split-player / AI slots use the same list structure; `GetCurrentPlayerSlot`
   already filters to the HUMAN slot so we only ever read the local
   player's selection.
