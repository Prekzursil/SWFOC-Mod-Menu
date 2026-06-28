# Blocked items — 2026-04-08

This document records ledger entries and follow-up work that could not be
fully resolved in the autonomous session of 2026-04-08, with concrete
instructions on what's needed to unblock them.

## Why these are blocked

The hard rule for the verified-facts ledger requires every `VERIFIED` entry
to have at least 2 entries in `tools_consensus`. The 7 remaining warnings
below have only `ida_pro` and have no second tool source available in this
session because:

- **Ghidra MCP is not connected** (server not running on `127.0.0.1:8089`).
- **Binary Ninja MCP is not connected** (headless server not running on `:9009`).
- **Cheat Engine** is not a valid second source for these specific entries
  because the SWFOC trainer (`trainer/SWFOC_GUI_Trainer_v3.lua`) does not
  exercise them — see per-entry notes below.
- **`lua_runtime`** is not a valid second source because the bridge does
  not currently call any of these RVAs at runtime, so no test_harness or
  smoke test exercises them.

These entries are therefore **single-tool by necessity** until the next
session brings up a second static analyser. They remain `VERIFIED` because
the IDA evidence (Hex-Rays decompile) is independently strong.

## Closing the warnings

Each entry below can be promoted to 2-tool consensus by either:

1. Starting Ghidra with the SWFOC project loaded and running the same
   `lookup_funcs` batch the Phase 3 work used (see
   `tools/phase3_build_engine_batch.py`), or
2. Starting Binary Ninja headless and querying the same RVAs via the BNIL
   MCP, or
3. Bringing the bridge into a live SWFOC instance and adding a runtime
   probe that calls into the function (this works for `rva_fow_*` and
   `rva_set_position` but not for `rva_operator_new` which has no Lua
   binding).

## Per-entry status

| ID | RVA | Why blocked | Unblock recipe |
|---|---|---|---|
| `rva_fow_disable_rendering_impl` | `0x6A53B0` | Trainer never disables rendering — only reveals fog. No runtime witness. | Ghidra `decompile 0x6A53B0`; confirm string `FOWManager::DisableRendering` or equivalent and add `ghidra` to `tools_consensus`. |
| `rva_fow_reveal_command_class_ctor` | `0x6A51B0` | Internal ctor for the FOW reveal command object; not directly invoked by user-visible Lua. | Ghidra `decompile 0x6A51B0`; confirm vtable + ctor pattern and add `ghidra`. |
| `rva_fow_sub_14035d4f0` | `0x35D4F0` | Internal helper of the FOW system; not exposed to Lua. | Ghidra `decompile 0x35D4F0`; record what the function does and add `ghidra`. |
| `rva_fow_temporary_reveal_impl` | `0x6A5CF0` | Trainer never uses temporary reveal (only persistent reveal). | Ghidra `decompile 0x6A5CF0`; confirm reveal-with-timeout semantics and add `ghidra`. |
| `rva_operator_new` | `0x769C58` | C++ allocator. Bridge does not call directly. Cannot add `cheat_engine` or `lua_runtime`. | Ghidra `decompile 0x769C58`; confirm MSVC `operator new` pattern (`__alloc_resource(this, sz)` or similar) and add `ghidra`. |
| `rva_set_position` | `0x3ABB80` | Used by `Teleport`; trainer's blueprints loader uses `Spawn_Unit` (not Teleport). No runtime witness. | Either (a) Ghidra `decompile 0x3ABB80` and add `ghidra`, or (b) add a `SWFOC_TeleportUnit` helper to the bridge that calls this RVA, then add `lua_runtime`. |
| `rva_teleport_lua_wrapper` | `0x5819E0` | Phase 3 corrected this from `Make_Invulnerable_Lua` to `GameObjectWrapper::Teleport` via IDA string evidence. Trainer doesn't teleport. | Ghidra `decompile 0x5819E0`; confirm the corrected identity and add `ghidra`. |

## Other items deferred from this session

### Snapshot format v2 (Part 5.2)

**Status:** PENDING. The synthetic snapshot generator
`swfoc_lua_bridge/make_test_snapshot.py` still emits v1 (`SWFOCSNAPv1`).
The Phase 6 agent's final notes call out the missing `local_slot: uint32`
field in section 1, which the Phase 9 replay harness has to work around.

**Unblock recipe:**

1. Bump magic to `SWFOCSNAPv2` in both
   `swfoc_lua_bridge/make_test_snapshot.py` and
   `swfoc_lua_bridge/replay_harness.cpp`.
2. Add `local_slot: uint32` after `player_count` in section 1.
3. Update the snapshot reader in `replay_harness.cpp` to detect either
   magic and parse accordingly. Default `local_slot` to `0` for v1 inputs
   to preserve current behaviour.
4. Add a bridge_test_harness test that round-trips both v1 and v2
   snapshots through `Lua_DumpState` and validates the version field.

This is mechanically simple but was deprioritised in favour of the editor
service wiring (Part 3.4) and the ledger warning reduction (Part 5.3).

### `requires_live_game.md`

The user's plan called for a `knowledge-base/requires_live_game.md` doc
listing every feature that genuinely needs the game running. This was not
written this session because every Phase 3.2 + 3.3 helper landed in the
test harness without needing the game (the synthetic player image proves
the same offsets and call shapes the live game uses). The remaining
"requires live game" items are:

- `SWFOC_GodMode` / `SWFOC_OneHitKill` — the harness tests the detour
  logic against a fake `SetHP`, but installation against the real RVA
  needs the game.
- `SWFOC_DumpState` — the harness tests the snapshot writer, but
  capturing a real game snapshot needs the game running.
- The 7 ledger entries above where Ghidra/Binary Ninja are required for
  the cross-validation (they don't strictly need the game, but they do
  need the static analyser tools to be live).
