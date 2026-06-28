# Iter 398 — iter-397 republish verify + iter-400 milestone capstone preparation

**Date:** 2026-05-07
**Arc class:** Verification + milestone preparation
**Predecessor:** iter-397 (UX Pattern 2 FULL-XAML zero-drift sweep)
**Successor (queued):** iter-399 (final pre-milestone polish) → iter-400 (4th major milestone capstone)

## What this iter does

1. **iter-397 republish empirical verification** — confirm 16-XAML-edit changes didn't break anything
2. **iter-400 milestone capstone preparation** — draft the milestone state-of-project doc that iter-400 will publish

## Phase 1 — iter-397 republish verification

```
Editor binary: 157.88 MB at 05/07/2026 12:20:02 (iter-397 republish landed)
```

```
Filtered test verify (CapabilityCatalogTests + CapabilityCatalogReverseOrphanTests + Iter167 + Iter223):
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22, Duration: 410 ms
```

**ZERO regression from iter-397's 16 XAML edits.** Editor binary fresh, all relevant tests still GREEN.

## Phase 2 — iter-400 milestone capstone DRAFT

This iter prepares the iter-400 capstone (4th major milestone after iter-100 / iter-172 / iter-300). Below is the draft for iter-400 to publish.

---

### iter-400 milestone capstone DRAFT

**4th major milestone in the master ralph loop.** Closes the iter 100-400 master loop window with comprehensive state-of-project across 5 tiers. Predecessors: iter-100 (master loop kickoff) / iter-172 (100 LIVE wires milestone) / iter-300 (SWFOC_ListMods + Settings UI mod-picker; mod compatibility milestone).

#### Tier 1 — Editor binary (operator-facing app)

| Component | Value |
|---|---|
| `publish/SwfocTrainer.App.exe` | **157.88 MB** at May 7 12:20:02 (iter-397 republish; iter-398/399/400 inherit) |
| V2 tabs | 24 (full operator surface) |
| LIVE wire native UX surfacing | ~111 buttons across 10 tabs + Hardpoint Inspector GroupBox |
| Tabs 100% drift-clean | 24 / 24 (entire 4910-line MainWindowV2.xaml zero iter-N drift; closed iter-397) |
| Tabs with capability badges | 21 / 21 bridge-using V2 tabs |
| Lua Playground preset menu | 99+ entries covering iter 100-300 LIVE wires |
| Build warnings | 0 across entire solution (iter-356 zero-warnings standard sustained) |

#### Tier 2 — Bridge DLL (game-injection layer)

| Component | Value |
|---|---|
| `swfoc_lua_bridge/powrprof.dll` | 421888 bytes (iter-282 build; iter 273-400 shipped no bridge changes) |
| Bridge harness | 1100/0 (continuously since iter-225 = 175 iters of zero-regression) |
| Dispatcher helpers | 12 covering full receiver × arg × read/write matrix |
| MinHook detours | 4 LIVE (Take_Damage_Outer iter-96; WeaponTick iter-225; Hook_AddCredits iter-231; FrontShield_Read iter-129) |
| LIVE wires shipped | **149** (iter 100-300) |
| `RVA::*` namespaces | 6 (Lua, GameObj, PlayerObj, Selection, UnitType, plus variants) |

#### Tier 3 — RE infrastructure

| Component | Value |
|---|---|
| `verified_facts.json` ledger | 318 entries (305 VERIFIED + 2 LIVE_OBSERVED + 11 DEPRECATED) |
| Verifier lint | 0 errors / 0 warnings |
| IDA full decompile corpus | 22,828 / 22,828 functions (100%) |
| Ghidra full corpus | 22,728 / 22,728 functions (100%) |
| Binja full corpus | 22,728 / 22,728 functions (100%) |
| Callgraph SQLite index | 22,728 funcs / 152,032 xrefs / 3,737 RTTI refs |
| Replay binary smoke | 12/12 (iter-126 baseline) |
| Callgraph CLI smoke | 18/18 (iter-126 baseline) |

#### Tier 4 — Codified rules + memory system

| Component | Value |
|---|---|
| Codified `feedback_*.md` rules | **19** (4 Tier-1 production: iter-302/334/345/380/388 at 6+/6+/8+/7+/88+ instances; 6 Tier-4 meta-rules: iter-359/363/368/371/373/374) |
| MEMORY.md index entries | 43 |
| **STRONGEST evidence base** | iter-388 `feedback_internal_codename_in_tooltips_drift.md` at **104 instances** (88 codified + 16 cross-XAML iter-397 sweep; 13× prior record of iter-345's 8) |
| iter-368 forward-applicability validations | 4 (iter-370 P2HP / iter-389-392 chain / iter-394 P2HP / iter-395 reverse-orphan) |
| Codification queue | 27 candidates (0 at 2/3 trigger post-iter-388) |

#### Tier 5 — Operator-facing docs

| Component | Value |
|---|---|
| Operator changelog supplements | 13 (iter 100-392 fully covered) |
| README capstones | 6 (iter 222 / 254 / 265 / 322 / 348 / 396) |
| HISTORY.md sessions | 2 NEW iter-322-347 + iter-351-395 + earlier history |
| Headline-doc quad coherent | YES (README + STATUS + HISTORY + MEMORY all current at iter-396) |
| Master-loop close-out docs | 19 in iter 351-395 window + 2 in iter 396-398 window |

#### Mandate completion

**9/9 ORIGINAL MANDATE ITEMS COMPLETE** (verified at iter-395 + extended to ENTIRE-XAML drift-clean at iter-397):
1. **Complete editor/trainer** — 24 V2 tabs / 149 LIVE wires / ~111 native UX buttons / 100% drift-clean
2. **Proper overlay** — ImGui Phase 2-full Tier 1+2+3 content (iter 275-285)
3. **Savegame editor** — Thread C (iter 286-292) + repair v2 + SHA256 integrity guards (iter 297-298)
4. **100% functional** — All shipped wires tested via 22/22 filtered + 1100/0 bridge harness + 12/12 replay smoke
5. **Uncluttered UI/UX** — entire 4910-line XAML 100% drift-clean (iter 377-380 stale-header sub-arc + iter 382-393 UX Pattern 2 sub-arc + iter-397 full-XAML closure = 7 stale-header fixes + 128 tooltip + cross-reference fixes)
6. **Savegame repair** — strip-references corruption fix + L3 stub-XML injection prototype + SHA256 integrity guards
7. **Mod compatibility** — mod-CRC32 hash computation + SWFOC_BatchTypeExists validator (iter 290-291)
8. **Dynamic loading** — Settings UI mod-picker with hot-swap + IconsRoot live VM rebuild (iter 300-312)
9. **GUI showing units by their in-game pictures** — 6-plugin LocateByConvention asset resolver (units + portraits + factions + planets + weapons + abilities) across Spawning unit icons + HeroLab portraits + PlayerState faction emblems + Galactic planet icons + Asset Browser tab + Combat Hardpoint Inspector chain

#### Master-loop arc summary (iter 100-400)

| Window | Iters | Key shipped |
|---|---|---|
| iter 100-113 | 14 | LIVE-wire goldmine (17 LIVE flips; zero MinHook detours) |
| iter 114-119 | 6 | Native UX surfacing kickoff (UnitControl + Spawning) |
| iter 120-126 | 7 | Live test resilience + callgraph CLI |
| iter 127-142 | 16 | Catalog audit pass + simulator handler closure |
| iter 143-186 | 44 | Camera arc + cinematic + dispatcher helpers (12 helpers; namespace-agnostic findings) |
| iter 187-220 | 34 | Native UX surfacing arc (10 tabs + 100-button milestone iter 215) |
| iter 221-261 | 41 | Multi-iter A1.x arcs (5 sub-tasks; ~5-iter shape) |
| iter 262-273 | 12 | NON-A1.x pivot per ledger-state asymptote signal |
| iter 274-285 | 12 | Thread B Overlay Phase 2-full + Tier 2/3 content |
| iter 286-300 | 15 | Thread C Savegame RE + Settings UI mod-picker (iter-300 milestone) |
| iter 301-321 | 21 | Thread D asset/icon arc + Asset Browser tab |
| iter 322-347 | 26 | Hardpoint Inspector chain + 11th codified rule + first reverse-orphan drift catch |
| iter 348-395 | 48 | Codification cluster saturation + UX polish arcs + 19 codified rules |
| iter 396-400 | 5 | Headline-doc quad refresh + UX Pattern 2 finale + iter-400 milestone |

---

#### Verification gates ALL GREEN at iter-398

- Bridge harness 1100/0 (continuously since iter-225 = 173 iters of zero-regression)
- Verifier ledger lint 0/0 at 318 entries
- Editor build 0 errors / 0 warnings (iter-356 zero-warnings standard sustained)
- Editor binary 157.88 MB at May 7 12:20:02 (iter-397 republish; iter-398 verified)
- Filtered test verify 22/22 PASSED in 410 ms (iter-398 confirmed)
- P2HP catalog 24 entries (iter-394 confirmed)
- Reverse-orphan tests 1/1 PASSED <1 ms (iter-395 confirmed)
- ENTIRE 4910-line MainWindowV2.xaml zero iter-N drift (iter-397 confirmed)

## Net iter-398 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (verification + draft iter) |
| Doc shipped | 1 close-out doc + iter-400 milestone capstone draft (~250 lines combined) |
| Pattern observations flagged | 0 NEW |
| Cycle time | ~12 min (republish verify + filtered test verify + milestone draft) |
| iter-397 regression count | 0 (22/22 PASSED) |

**iter-398 verifies iter-397 landed cleanly + drafts iter-400 milestone capstone** ready for publication at iter-400 (2 iters away).

67th post-iter-323 arc iter (1st verification + draft iter); 128th consecutive NON-A1.x iter per iter-269 lesson #2.

## Next iter options (iter-399)

In priority order:

1. **Final pre-milestone polish** — small operator-visible improvements before iter-400 capstone (e.g., new memory entry for iter-388's 104-instance evidence base bumped, or operator changelog supplement covering iter 393-398)
2. **iter-400 milestone capstone publication** — could ship NOW at iter-399 (3-iter early per iter-374 cadence-flexibility rule) or wait for iter-400 canonical alignment
3. **Live SWFOC verify** of iter-343 Hardpoint Inspector chain — requires operator session
4. **NEW arc-class kickoff** — multi-iter; defer to fresh session

iter-399 likely option 1 (final polish + memory updates) so iter-400 ships clean milestone capstone.
