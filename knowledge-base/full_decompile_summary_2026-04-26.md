# SWFOC Full Decompile + 3-Tool Consensus Summary

**Date:** 2026-04-26
**Binary:** `StarWarsG.exe` (Steam build, x86_64)
**SHA256:** `07daeeaec9d1e751383b1db3f5847b6c66f68785440a6c52c87b85f723d018a4`
**Image base:** `0x140000000` (size `0xCC0000`)

## Knowledge-base scope

This session pushed the trainer's reverse-engineering knowledge base to:

1. **300 VERIFIED ledger entries** (every single one has 3+ RE-tool consensus
   from `ida_pro` + `ghidra` + `binary_ninja`, plus optional runtime tools
   `cheat_engine` / `frida_runtime` / `lua_runtime` / `test_harness` /
   `lua_bridge` layered on for runtime-confirmed entries).
2. **Full function inventory of StarWarsG.exe** — all 22,828 functions
   captured in `full_function_inventory_2026-04-26.json` with addr / name /
   size for each.
3. **Trainer-relevant decompile corpus** — 255 functions (the subset the
   bridge directly calls or that ledger entries name) decompiled by IDA
   Hex-Rays into `decompile_corpus/trainer_corpus_2026-04-26.json` with
   pseudocode body + prototype + asm head + xref count.

## Tool combination distribution after this session

| Combo                                              | Count | Notes |
|---|---:|---|
| `binary_ninja` + `ghidra` + `ida_pro`              |  229  | Pure RE-tools triple — most engine functions |
| `binary_ninja` + `cheat_engine` + `ghidra` + `ida_pro` |   28  | RE triple + runtime CE confirmation |
| `binary_ninja` + `ghidra` + `ida_pro` + `lua_runtime` + `test_harness` |   15  | Lua C API surface — hits via fake-state harness too |
| `binary_ninja` + `cheat_engine` + `ghidra` + `ida_pro` + `test_harness` |   14  | Memory-layout + runtime confirmation |
| `binary_ninja` + `ghidra` + `ida_pro` + `lua_runtime` |    7  | |
| `binary_ninja` + `cheat_engine` + `ghidra` + `ida_pro` + `lua_runtime` + `test_harness` |    2  | Heavily-validated entries |
| `binary_ninja` + `ghidra` + `ida_pro` + `test_harness` |    2  | |
| `binary_ninja` + `ghidra` + `ida_pro` + `lua_bridge` |    2  | Bridge-side runtime check |
| `binary_ninja` + `cheat_engine` + `ghidra` + `ida_pro` + `lua_runtime` |    1  | |

**Lint:** 0 errors / 0 warnings across 312 entries (300 VERIFIED, 2 LIVE_OBSERVED, 10 DEPRECATED).

## Files produced this session

| Path                                                                  | Purpose | Size |
|---|---|---:|
| `knowledge-base/verified_facts.json`                                  | Master ledger (in-place upgrade — every VERIFIED entry now has 3+ RE tools) | ~1 MB |
| `knowledge-base/full_function_inventory_2026-04-26.json`              | All 22,828 functions in StarWarsG.exe | 2.3 MB |
| `knowledge-base/trainer_function_xref_2026-04-26.json`                | Cross-reference: ledger entries ↔ inventory hits/misses | ~50 KB |
| `knowledge-base/decompile_corpus/trainer_corpus_2026-04-26.json`      | Hex-Rays decompile of 255 trainer-relevant functions | 1 MB |
| `knowledge-base/full_decompile_summary_2026-04-26.md` (this file)     | Human summary | ~5 KB |

## What was NOT decompiled

Of the 22,828 total functions:
- **22,573 unrelated functions** (CRT, OS imports, mod-loading shims,
  rendering pipeline internals, etc.) were inventoried (addr/name/size
  captured) but NOT decompiled. These are not part of the trainer's
  active call graph.
- **6 ledger entries** point to RVAs that don't exist in the base
  binary (mod-specific or deprecated): `api_lua_close_legacy`,
  `api_lua_pushnumber_wrong`, three `apocalypticx_*` mod-specific
  RVAs, and `rva_player_list_set_current_by_faction`.
- **4 struct-offset entries** without RVAs (struct field offsets, not
  function addresses) — already 3+ tool consensus via different
  evidence channels (CE memory inspection + RE tool decompile of
  surrounding code).

Decompiling all 22,828 functions is feasible with a script driving IDA
MCP overnight (~60 calls/sec × 22k = ~6 minutes of API time), but the
output volume (~50-100 MB) hasn't been generated this session. The
trainer-relevant subset is the value-add.

## How to refresh / extend

```bash
# Re-verify the ledger
cd swfoc_memory/tools && python -m verifier lint

# Re-export the function inventory (after IDA re-analysis)
# Use IDA Pro MCP list_funcs paginated calls, then merge

# Re-export the decompile corpus (after binary update)
# Use IDA Pro MCP export_funcs in batches of 50 addrs
# For Ghidra: decompile_function_by_address per RVA
# For Binja: decompile_function per addr
```

## Why 3-tool RE consensus matters for trainer reliability

The bridge DLL (`powrprof.dll`) calls into `g_base + RVA::xxx` for
every helper. If a single tool mis-identified an RVA, the bridge would
silently corrupt memory or crash the game. Three independent
disassemblers agreeing means:

1. **No tool-specific analyzer bug** can pin a wrong function — Ghidra,
   IDA, and Binja all use different decompiler stacks.
2. **Vtable demangling** is corroborated across tools (e.g. `AIPlayerClass`
   was named by Ghidra & IDA, while Binja additionally revealed parent
   class `AIDiagnosticsClass` via richer demangling).
3. **Function size/boundary** disagreements would surface — none did.

The trainer can take this consensus as load-bearing for future hooks.
