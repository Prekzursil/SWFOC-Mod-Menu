# Phase 2 hook backlog — 2026-04-26

This catalogues every bridge helper currently shipping as a Phase-1 stub
(records intent into a pending-state map; replay mirror only). Each entry
either has an IDA-pinned hook target (rare) or is BLOCKED-NO-RVA pending
multi-tool ledger consensus.

Per CLAUDE.md `tools_consensus >= 2` rule: a Phase 2 hook ships only when
the target RVA has at least two-tool VERIFIED entries in
`verified_facts.json` (typically `ida_pro` + one of `ghidra` /
`binary_ninja` / `frida_runtime` / `lua_runtime`).

The badge taxonomy each helper exposes via the V2 UI (Unit F):

- `Live` — direct engine call, observable mutation
- `ReplayVerified` — replay mirror green, live behaviour unverified
- `Phase2HookPending` — Phase 1 mirror works, Phase 2 detour BLOCKED-NO-RVA
- `RequiresLiveSwfoc` — needs live game; offline harness can't exercise
- `Unavailable` — registered but out-of-scope this release

## Catalogue

| # | Helper | Bridge fn | Status | Phase 1 (replay) | Phase 2 target | Block reason |
|---|---|---|---|---|---|---|
| 1 | `SWFOC_SetIncomeMultiplier` | `Lua_SetIncomeMultiplier` | Phase2HookPending | ReplayVerified | per-tick income scheduler | BLOCKED-NO-RVA — need IDA find of credit-grant per-frame loop |
| 2 | `SWFOC_SetGameSpeed` | `Lua_SetGameSpeed` | Phase2HookPending | ReplayVerified | game-tick scheduler delta | BLOCKED-NO-RVA |
| 3 | `SWFOC_FreezeCredits` | `Lua_SetFreezeCredits` | Phase2HookPending | ReplayVerified | credit-write detour | BLOCKED-NO-RVA |
| 4 | `SWFOC_SetBuildSpeed` | `Lua_SetBuildSpeed` | Phase2HookPending | ReplayVerified | per-build-tick progress | BLOCKED-NO-RVA |
| 5 | `SWFOC_SetPerFactionSpeedMultiplier` | `Lua_SetPerFactionSpeedMultiplier` | Phase2HookPending | ReplayVerified | locomotor-global multiplier | BLOCKED-NO-RVA |
| 6 | `SWFOC_SetDamageMultiplier` | `Lua_SetDamageMultiplier` | Phase2HookPending | ReplayVerified | ApplyDamage scaling site | BLOCKED-NO-RVA — RVA `0x38A350` unverified, single-tool |
| 7 | `SWFOC_SetFireRate` | `Lua_SetFireRate` | Phase2HookPending | ReplayVerified | weapon cooldown reset | BLOCKED-NO-RVA |
| 8 | `SWFOC_SetAreaDamage` | `Lua_SetAreaDamage` | Phase2HookPending | ReplayVerified | Take_Damage_Outer splash branch | BLOCKED-NO-RVA |
| 9 | `SWFOC_SetTargetFilter` | `Lua_SetTargetFilter` | Phase2HookPending | ReplayVerified | targeting-filter predicate | BLOCKED-NO-RVA |
| 10 | `SWFOC_ToggleOHKAttackPower` | `Lua_ToggleOHKAttackPower` | Phase2HookPending | ReplayVerified | unit.attack_power offset | BLOCKED-NO-RVA |
| 11 | `SWFOC_FreezeAI` | `Lua_FreezeAI` | Phase2HookPending | ReplayVerified | AI scheduler dispatch | BLOCKED-NO-RVA — see Unit H2 thread |
| 12 | `SWFOC_FreeCam` | `Lua_FreeCam` | Phase2HookPending | ReplayVerified | camera singleton flag | BLOCKED-NO-RVA |
| 13 | `SWFOC_SetCameraPos` | `Lua_SetCameraPos` | Phase2HookPending | ReplayVerified | camera transform write | BLOCKED-NO-RVA |
| 14 | `SWFOC_SpawnUnit` | `Lua_SpawnUnit` | Phase2HookPending | ReplayVerified | Spawn_Unit engine call + credits-deduction | BLOCKED-NO-RVA |
| 15 | `SWFOC_SetBuildCost` | `Lua_SetBuildCost` | Phase2HookPending | ReplayVerified | credits-deduction site | BLOCKED-NO-RVA |
| 16 | `SWFOC_SetUnitCapOverride` | `Lua_SetUnitCapOverride` | Phase2HookPending | ReplayVerified | unit-cap check | BLOCKED-NO-RVA |
| 17 | `SWFOC_SetUnitField` | `Lua_SetUnitField` | Phase2HookPending | ReplayVerified | per-field offset table | BLOCKED-NO-RVA |
| 18 | `SWFOC_InstantBuild` | `Lua_InstantBuild` | Phase2HookPending | ReplayVerified | build-progress increment patch | BLOCKED-NO-RVA |
| 19 | `SWFOC_FreeBuild` | `Lua_FreeBuild` | Phase2HookPending | ReplayVerified | credits-deduction patch | BLOCKED-NO-RVA |
| 20 | `SWFOC_AttachAiBrain` | `Lua_AttachAiBrain` | Stub | not applicable | `AIPlayerClass::ctor` 0x4AF810 + alloc | BLOCKED-NO-CONSENSUS — single-tool Ghidra; needs IDA + frida_runtime |
| 21 | `SWFOC_SetOrbitalPhase` | `Lua_SetOrbitalPhase` | Phase2HookPending | ReplayVerified | orbital phase enum | BLOCKED-NO-RVA |
| 22 | `SWFOC_SetMusicVolume` | `Lua_SetMusicVolume` | Phase2HookPending | ReplayVerified | music engine singleton | BLOCKED-NO-RVA |
| 23 | `SWFOC_SetVeterancy` | `Lua_SetVeterancy` | Phase2HookPending | ReplayVerified | veterancy field offset | BLOCKED-NO-RVA |
| 24 | `SWFOC_AddMapHint` | `Lua_AddMapHint` | Phase2HookPending | ReplayVerified | hint-system sprite index | BLOCKED-NO-RVA |

## Unblock procedure (per row)

1. Open `StarWarsG.exe` in IDA Pro with IDA-MCP-Server plugin loaded
2. From a Claude session: `ToolSearch select:mcp__ida-pro-mcp__decompile`
3. For each row: search the decompile for the named pattern (e.g. "build progress", "credits write", "weapon cooldown")
4. Add the discovered RVA to `verified_facts.json` with `tools_consensus: ["ida_pro", ...]`
5. Implement the detour in `lua_bridge.cpp` referencing the new ledger entry
6. Add a regression-pair test in `swfoc_lua_bridge/test_harness.cpp`
7. Move the row from `Phase2HookPending` to `Live` in this catalogue + the V2 capability badge JSON

## Per-helper unblock priorities

- **High value, low IDA effort**: `SWFOC_SetIncomeMultiplier` (look for credit-grant in income_per_tick.cpp paths), `SWFOC_FreezeCredits` (xref to PlayerObject credit field write), `SWFOC_FreeBuild` (xref to credit-deduction site)
- **Medium value, medium effort**: combat scaling group (`SetDamageMultiplier`, `SetFireRate`, `SetAreaDamage`)
- **Low value, high effort**: P7 deferred (orbital, music, veterancy, hints) — fine to leave Phase 1 indefinitely

## Notes

The goal of this document is so the V2 UI's capability badge layer (Unit F)
can render an honest "Phase 2 hook pending" indicator beside every Phase-1
helper, instead of the operator clicking a button and seeing it succeed
without any visible game effect.
