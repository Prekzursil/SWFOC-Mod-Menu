# Session 2026-04-06 Postmortem — Live Bridge Test Over-Claims

**Author of correction pass:** follow-on session, 2026-04-06 (documentation-only)
**Scope:** This document is a flat audit log of every runtime/empirical claim made during the 2026-04-06 live-bridge session, graded against the actual evidence we had at the time the claim was written.

## Why this exists

User called out:
> "live tests are not probed properly, he doesnt properly probe results like spawn or so that i dont think work properly"

That is correct. In the 2026-04-06 session, many bridge calls were marked "PASS" or "Confirmed Working" solely because the `\\.\pipe\swfoc_bridge` round-trip returned a non-error response. That conflates two very different things:

1. **The Lua wrapper ran without raising a Lua-level error.** (What a bridge "OK" actually proves.)
2. **The in-game side effect specified by the API actually happened.** (What was never probed.)

A documentation-only correction pass was done on three files:

- `knowledge-base/static_analysis_qa.md` (Q9 hardpoint claim)
- `knowledge-base/v5_service_api_findings.md` (runtime API matrix)
- `knowledge-base/v5_service_live_matrix.md` (every PASS row)

This file is the flat audit log.

## Confidence vocabulary used across all three files

| Label | Meaning |
|---|---|
| **VERIFIED** | The returned value is real game state data (e.g., a live credits number, a faction name) that can ONLY come from a working call. Read-only queries that return specific data are safe to tag VERIFIED. |
| **VERIFIED-NEGATIVE** | A probe returned `nil` or an error for something we expected to exist. This is itself a useful, verified negative — e.g., "Make_Ally is NOT a Lua global in this build." |
| **VERIFIED-EXISTS** | A `type()` probe confirmed a Lua global is a userdata. The function exists in the Lua state; whether it does anything useful when invoked with real arguments is unknown. |
| **LIVE_OBSERVED** | We invoked the call, the Lua wrapper did not raise an error, and the bridge returned a response. **The in-game side effect was NOT probed.** Every camera command and every `Story_Event` call falls into this category. |
| **UNVERIFIED** | Either we didn't really call it, or the call pattern is too ambiguous to grade. |
| **REFUTED** | We called it with `ok=true` but a follow-up probe proved the side effect did not happen. |

## Flat audit log

| # | Claim (as originally written 2026-04-06) | Original label | Corrected label | Evidence we actually had | What we'd need for VERIFIED |
|---|---|---|---|---|---|
| 1 | `Make_Invulnerable(true) + Set_Cannot_Be_Killed(true)` protects the HULL from incoming damage | "empirically confirmed" | **LIVE_OBSERVED (hull freeze)** | `Get_Hull()` samples across ~60s of active combat: Vengeance stayed at `0.998000`, Nebulon dropped from `0.5520 → 0` and was destroyed. This IS real observation of hull state. | Already sufficient for "hull stays at a fixed value under fire." This one actually holds up. |
| 2 | Hardpoint propagation for `Make_Invulnerable(true)` is "confirmed YES" | "Hardpoint propagation: YES — confirmed" | **UNVERIFIED (runtime) / STATIC-ONLY (decompile)** | The IDA Hex-Rays decompile of the Lua wrapper at `0x14057D550` shows a loop that iterates `QueryInterface(obj, 22)` children and calls the same behavior-attach path on each. That is a code-path claim, not a runtime observation. The only "evidence" at runtime was "the ship kept firing weapons" — which is circumstantial and does not prove per-hardpoint flag state. The Lua API exposes NO hardpoint enumeration (`Get_All_Hardpoints`, `Get_Hardpoint_Count`, etc. are all nil on the userdata), so no direct hardpoint inspection was attempted. | Cheat Engine read of byte `+0x3A7` on each child hardpoint after the call, OR a Frida hook on `Take_Damage_Outer` (RVA `0x38A350`) during focus fire on a specific hardpoint showing the damage is rejected. |
| 3 | `Spawn_Unit(...)` returning `ok=true` means the spawn worked | "Confirmed Working" (type==userdata) + implied "call works" | **REFUTED** | Called multiple times in phase 2k with `ok=true`. Follow-up `Find_All_Objects_Of_Type` on the enemy faction continued to show only 1 Nebulon and 1 Corvette — the original counts. The spawned units never appeared in subsequent enumeration. The `ok=true` only meant the Lua wrapper did not raise a Lua-level error; it did NOT mean the engine instantiated a game object. | Pre-call and post-call `Find_All_Objects_Of_Type` counts with a +5s delay, showing the count increase. Alternatively, Frida hook on the game-side spawn function to confirm the wrapper actually reaches the C++ spawn path with valid arguments. |
| 4 | `Take_Damage(N)` on a game object is a no-op for hull modification | "tested values 0.5, 1.0, 10000, 100 with BOTH protections removed, hull never changed" | **UNVERIFIED (not REFUTED)** | The observation is real — `Get_Hull()` did not change across multiple 1/2/3-arg invocations. BUT: we don't know if we called it correctly. The Lua-exposed `Take_Damage` is almost certainly the C++ damage receiver and very likely needs a `damage source` object (a weapon, a projectile, or a game object reference) that Lua cannot synthesize. Passing raw numbers may have been silently discarded by the wrapper. So the hypothesis "Take_Damage is a no-op" cannot be concluded; the correct hypothesis is "our invocation pattern did not match the argument schema." | Frida hook on `Take_Damage_Outer` (RVA `0x38A350`) during real combat to capture the actual argument tuple the engine passes when a weapon hit lands, then replay that exact tuple from Lua. If it STILL no-ops, then REFUTED. |
| 5 | `Story_Event("GENERIC")` — "side effect fired, returned `fired`" / later `OK` | "Confirmed Working" | **LIVE_OBSERVED** | The bridge returned the string `fired` (and in the live matrix, `OK`). That only proves the Lua wrapper executed. The game's story-event dispatch log was never inspected to confirm an event with that name actually triggered a handler. "fired" is what the Lua wrapper prints, not proof the story engine dispatched. | Inspect the game's story-event log / cinematic trigger state after the call, or hook the StoryEvent dispatch path and confirm the event ID propagates. |
| 6 | `Letter_Box_On()` — "OK" | "PASS" | **LIVE_OBSERVED** | The bridge returned `OK`. The screen was never checked for the actual letterbox bars. The "OK" is indistinguishable between "the function worked and bars are drawn" and "the function no-op'd because we're not in a cinematic mode." | Visual check — screenshot or in-game observation — that the letterbox bars actually appear. |
| 7 | `Letter_Box_Off()` — "OK" | "PASS" | **LIVE_OBSERVED** | Same as row 6, inverse. | Visual check that the bars disappear. |
| 8 | `Zoom_Camera(1.0)` — "OK" | "PASS" | **LIVE_OBSERVED** | Bridge returned `OK`. Camera zoom state was not sampled before or after. Also, `1.0` is a likely no-op value (unity zoom factor), so even success would produce no visible change. | Sample the camera zoom field (via Cheat Engine or a Ghidra-located offset), call Zoom_Camera with a non-trivial value (e.g., 2.0), and sample again. |
| 9 | `Rotate_Camera_By(0)` — "OK" | "PASS" | **LIVE_OBSERVED (meaningless test)** | Bridge returned `OK`. Camera heading was not sampled. Rotating by 0 degrees is mathematically a no-op, so this call tells us nothing. | Re-run with a non-zero angle (e.g., 90) and sample camera heading before and after. |
| 10 | `Point_Camera_At(unit)` — "userdata" | "Confirmed Working" | **VERIFIED-EXISTS only** | The `type()` probe confirmed the global is a userdata. It was never invoked with a real unit argument, so no camera movement was ever observed. | Invoke with a real unit from `Find_All_Objects_Of_Type`, sample camera position before and after. |
| 11 | `Suspend_AI(seconds)` — "userdata" | "Confirmed Working" | **VERIFIED-EXISTS only** | Same as row 10 — `type()` probe only. Never actually called with a duration; enemy AI pause behavior never observed. | Call `Suspend_AI(10)` in a tactical battle, watch whether enemy units stop moving/firing for 10 seconds. |
| 12 | `Make_Ally`, `Make_Enemy`, `Is_Ally`, `Is_Enemy` as globals (DiplomacyService) | "Confirmed correct (verified against KB, not yet runtime-confirmed)" | **VERIFIED-NEGATIVE** | Row 10 of the live matrix: `return type(Make_Ally) .. " " .. type(Make_Enemy) .. " " .. type(Is_Ally) .. " " .. type(Is_Enemy)` returned `nil nil nil nil`. Row 11 confirmed with an actual call: `attempt to call global 'Make_Ally'` error. So these are definitively NOT installed as Lua globals in our build. This is a useful negative finding — the KB entry that claims they are globals is wrong. | Search the binary (IDA string xref on "Make_Ally") for where/if this function is actually registered. If found, it may be a PlayerObject method, a DiplomacyService namespace method, or absent from this mod. |
| 13 | `Find_Player("local"):Get_Credits()` returning `17454.78515625` | "Confirmed Working" | **VERIFIED** | The returned value IS real game state data (live credits balance). Read-only query returning specific data is a valid VERIFIED. | Already sufficient. |
| 14 | `Find_Player("local"):Get_Faction_Name()` returning `UNDERWORLD` | "Confirmed Working" | **VERIFIED** | Same — returned real live data. | Already sufficient. |
| 15 | `Find_Player("local"):Get_Tech_Level()` returning `2` | "Confirmed Working" | **VERIFIED** | Same — returned real live data. | Already sufficient. |
| 16 | `Find_Player("local"):Get_Name()` returning `Zann Consortium` | "Confirmed Working" | **VERIFIED** | Same — returned real live data matching the known local player. | Already sufficient. |
| 17 | `SWFOC_GetCredits()` incrementing `13710 → 17454` | "Confirmed Working" | **VERIFIED** | The value changed over time in a way consistent with galactic-conquest passive income, and matches the numbers from `Find_Player("local"):Get_Credits()`. | Already sufficient. |
| 18 | `Find_Object_Type("Vengeance_Frigate")` returning `userdata` | "Confirmed Working" | **VERIFIED** | The return value IS the probe — a live type handle (not nil) in tactical mode proves the type table is loaded and the lookup succeeded. | Already sufficient. |
| 19 | `Find_All_Objects_Of_Type(t)` count == 1 (for the Vengeance in tactical) | "Confirmed Working" | **VERIFIED** | The count IS the probe — enumeration returned a real integer matching the number of Vengeance units the player had on the field. | Already sufficient. |
| 20 | CooldownManager probe returning `player methods: ` (empty) | "PASS" | **UNVERIFIED** | The iteration pattern `for _,m in player` does not iterate a userdata metatable in Lua 5.0 — it iterates either an error or the userdata's environment table, which may be empty by design. The empty result does NOT prove "no methods"; it proves "our iteration pattern didn't see any." | Replace the probe with `getmetatable(player)` enumeration (Lua 5.0 metatable pattern) and iterate `__index` if it's a table, or dump the C++ method registration block from the binary. |
| 21 | Corruption probe returning `underworld methods: ` (empty) | "PASS" | **UNVERIFIED** | Same issue as row 20 — the iteration pattern is broken, the empty result is not evidence. | Same next-step as row 20. |
| 22 | PlanetManager probe — `type(Find_Planet) .. type(Get_Planet)` returns `nil nil` | "PASS" | **VERIFIED-NEGATIVE** | Both globals are confirmed nil. | Already sufficient as a negative. Next step is to search the binary for the real planet lookup entry points. |
| 23 | FleetManager probe — `type(Create_Task_Force) .. type(Find_All_Objects_Of_Type)` returns `nil userdata` | "PASS" | **VERIFIED-NEGATIVE (Create_Task_Force) / VERIFIED-EXISTS (Find_All_Objects_Of_Type)** | Both halves directly observed. | Already sufficient for both halves. |
| 24 | FactionSwitch probe — `type(p.Change_Owner)` returns `nil` | "PASS" | **VERIFIED-NEGATIVE** | Directly observed. | Already sufficient. |
| 25 | ModConflict probe — `type(GameRandom) .. type(Find_Object_Type)` returns `userdata userdata` | "PASS" | **VERIFIED-EXISTS** | Existence probe only. | To upgrade, actually call `GameRandom()` and see if it returns a real pseudo-random number, and call `Find_Object_Type(...)` with a known type name. |
| 26 | DamageLog probe — `type(Get_Faction_Name()) .. type(Get_Credits())` returns `string number` | "PASS" | **VERIFIED** | The underlying read-only queries already verified in rows 13-16. | Already sufficient. |
| 27 | OwnershipTransfer probe — `userdata nil` | "PASS" | **UNVERIFIED** | Without the full Lua script of the probe and a description of what the two fields were checking, we can't grade this. The `nil` on the second half might be a real negative OR a broken probe. | Re-run the probe with the full Lua script documented in the matrix row, then grade based on what each field actually tests. |

## Root-cause summary

1. **Bridge `OK` was treated as ground truth for side effects.** The bridge's `OK` is only a Lua-level "no error" signal. Any claim about an in-game side effect (camera moved, unit spawned, event fired, letterbox drawn) requires a second probe that reads game state AFTER the call, or a Frida hook that confirms the engine's side effect path was actually taken.

2. **Existence probes were upgraded to "Confirmed Working" silently.** A `type(X) == "userdata"` result was often recorded in the same "Confirmed Working APIs" table as read-only queries that returned live data. Those are different confidence levels and must not share a table.

3. **Meaningless identity-parameter calls.** `Zoom_Camera(1.0)` (unity zoom) and `Rotate_Camera_By(0)` (zero rotation) are no-ops even on success. They should never have been treated as evidence that the API works.

4. **Circumstantial evidence was elevated to direct evidence.** "The ship kept firing weapons, therefore hardpoints are intact" is circumstantial. A ship can fire with some hardpoints destroyed. Only a direct read of per-hardpoint state is direct evidence.

5. **Lua 5.0 iteration idioms were not audited.** The CooldownManager and Corruption probes both used iteration patterns that return empty results on userdata in Lua 5.0, so "empty method list" was not evidence of anything.

## Files edited in this correction pass

- `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\static_analysis_qa.md` — Q9 hull-vs-hardpoint split, Q8 hardpoint-propagation claim requalified from "YES — confirmed" to "static-analysis only (UNVERIFIED at runtime)."
- `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\v5_service_api_findings.md` — runtime API matrix re-graded; Spawn_Unit REFUTED section added; Take_Damage UNVERIFIED section added; diplomacy globals re-labeled VERIFIED-NEGATIVE; camera + StoryEvent re-labeled LIVE_OBSERVED.
- `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\v5_service_live_matrix.md` — every PASS row re-graded with a confidence column and a "side effect probed?" column; Spawn_Unit REFUTED section added at the bottom.
- `C:\Users\Prekzursil\Downloads\swfoc_memory\knowledge-base\session_2026-04-06_postmortem.md` — this file (new).

## Recommended process fix for future live sessions

Before marking any runtime claim as Confirmed / PASS / Working, answer these four questions in writing:

1. What specific game-state value does this call claim to change?
2. How will I read that value BEFORE the call?
3. How will I read that value AFTER the call?
4. If the bridge returns `OK` but the value did not change, what is my next step?

If the call has no observable game-state value (e.g., `Story_Event` with no follow-up state probe), then the correct label is LIVE_OBSERVED, NOT VERIFIED. That label is legitimate and useful — it means "we know the Lua wrapper runs without error" — but it must not be confused with "the game behavior changed."
