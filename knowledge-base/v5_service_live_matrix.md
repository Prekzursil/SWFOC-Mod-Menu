# v5 Service Live Test Matrix - 2026-04-06 23:12

Game: SWFOC + Thrawn's Revenge, tactical space battle, Underworld (Zann Consortium)

> **Audit note (postmortem 2026-04-06):** Every row in the original table was marked "PASS" based on the bridge returning a non-error response. That was wrong. A bridge "OK" only proves the Lua wrapper ran without raising an error — it does NOT prove the in-game side effect occurred. This table has been re-graded against whether the side effect was actually observed. Confidence vocabulary:
>
> - **VERIFIED** — The returned value is real game state data (e.g., a numeric credits balance, a faction name) that can only come from a working call.
> - **VERIFIED-NEGATIVE** — A probe returned `nil` for something we expected to exist, which is itself a useful, verified negative result (we now know the global is NOT installed in this build).
> - **VERIFIED-EXISTS** — A `type()` probe confirmed a Lua global is a userdata, but the call was never actually invoked with real arguments. The function exists; whether it does anything useful is unknown.
> - **LIVE_OBSERVED** — The call was invoked, the Lua wrapper did not error, and the bridge returned a response. The in-game side effect was NOT probed.
> - **UNVERIFIED** — Either we didn't really call it, or the call pattern is too ambiguous to grade.
> - **REFUTED** — We called it with `ok=true` but a follow-up probe proved the side effect did not happen.

| # | Service | Lua | Response | Confidence | Side effect probed? |
|---|---|---|---|---|---|
| 1 | FactionDashboard | `local p = Find_Player("UNDERWORLD"); if p then return tostri` | `21137.80078125` | **VERIFIED** | Yes — the returned number IS the probe (live credits balance) |
| 2 | StoryEvent | `Story_Event("GENERIC")` | `OK` | **LIVE_OBSERVED** | **No** — game event log was never inspected. "OK" only means the Lua wrapper ran without error. |
| 3 | CameraDirector(letterbox) | `Letter_Box_On()` | `OK` | **LIVE_OBSERVED** | **No** — screen was never checked for actual letterbox bars. |
| 4 | CameraDirector(letterbox) | `Letter_Box_Off()` | `OK` | **LIVE_OBSERVED** | **No** — screen state never inspected. |
| 5 | CameraDirector(zoom) | `Zoom_Camera(1.0)` | `OK` | **LIVE_OBSERVED** | **No** — camera zoom level never sampled before/after. |
| 6 | CameraDirector(rotate) | `Rotate_Camera_By(0)` | `OK` | **LIVE_OBSERVED (meaningless)** | **No** — camera heading never sampled. Also, rotation angle is 0, so even a successful call would produce no visible change. This test is not useful evidence for the API working. |
| 7 | AiControl | `return type(Suspend_AI)` | `userdata` | **VERIFIED-EXISTS** | N/A — existence probe only. Never actually called with a duration; AI pause behavior never checked. |
| 8 | EnhancedSpawn(type) | `return type(Find_Object_Type("Vengeance_Frigate"))` | `userdata` | **VERIFIED** | Yes — the return value IS the probe (a live type handle, not nil, proves the type is loaded in tactical mode). |
| 9 | RosterBrowser | `local t = Find_Object_Type("Vengeance_Frigate"); local objs ` | `1` | **VERIFIED** | Yes — the count (1) IS the probe (`Find_All_Objects_Of_Type` actually enumerated the world and returned a count). |
| 10 | Diplomacy(global probe) | `return type(Make_Ally) .. " " .. type(Make_Enemy) .. " " .. ` | `nil nil nil nil` | **VERIFIED-NEGATIVE** | Yes — this IS the useful finding. All four diplomacy globals (`Make_Ally`, `Make_Enemy`, `Is_Ally`, `Is_Enemy`) return `nil`, proving they are NOT installed as Lua globals in this build. The KB entry that claims they are globals is wrong; the next step is to check whether they live as PlayerObject methods or in a different namespace, or simply don't exist in this mod. |
| 11 | Diplomacy(call) | `local r = Find_Player("REBEL"); local e = Find_Player("EMPIR` | `Make_Ally ok=false err=pipe:1: attempt to call global 'Make_` | **VERIFIED-NEGATIVE** | Yes — the error "attempt to call global Make_Ally" confirms row 10. |
| 12 | CooldownManager(probe) | `local p = Find_Player("local"); local methods = ""; for _,m ` | `player methods: ` | **UNVERIFIED** | The response is an empty method list. Either the iteration pattern is wrong (likely — `for _,m in player` doesn't iterate a userdata metatable in Lua 5.0 that way), or the player userdata has no iterable method list. This row does NOT prove anything about CooldownManager either way. |
| 13 | PlanetManager(probe) | `return type(Find_Planet) .. " " .. type(Get_Planet)` | `nil nil` | **VERIFIED-NEGATIVE** | Yes — confirmed both globals are nil. Next step: search the binary for "Find_Planet" / "Get_Planet" string registrations. |
| 14 | FleetManager(probe) | `return type(Create_Task_Force) .. " " .. type(Find_All_Objec` | `nil userdata` | **VERIFIED-NEGATIVE (Create_Task_Force) / VERIFIED-EXISTS (Find_All_Objects_Of_Type)** | Yes for both halves. |
| 15 | FactionSwitch(probe) | `local p = Find_Player("local"); return type(p.Change_Owner) ` | `nil nil` | **VERIFIED-NEGATIVE** | Yes — confirms `PlayerObject.Change_Owner` is not exposed. |
| 16 | ModConflict(probe) | `return type(GameRandom) .. " " .. type(Find_Object_Type)` | `userdata userdata` | **VERIFIED-EXISTS** | N/A — existence probe only. |
| 17 | Corruption(probe) | `local p = Find_Player("UNDERWORLD"); local methods = ""; for` | `underworld methods: ` | **UNVERIFIED** | Same issue as row 12 — the iteration pattern returned an empty method list. Does not prove anything about Corruption. |
| 18 | DamageLog(probe) | `return type(Find_Player("local"):Get_Faction_Name()) .. " " ` | `string number` | **VERIFIED** | Yes — confirms `Get_Faction_Name()` returns a string and `Get_Credits()` returns a number. This overlaps with the VERIFIED rows in `v5_service_api_findings.md`. |
| 19 | OwnershipTransfer(probe) | `local t = Find_Object_Type("Vengeance_Frigate"); local objs ` | `userdata nil` | **UNVERIFIED** | The `nil` on the second half is ambiguous — without the full Lua script and what it was checking, we can't tell if it's a real negative or a broken probe. |

## Spawn_Unit — REFUTED

Not in the matrix above because it was called later in the session, but it MUST be recorded here because this is the most consequential correction:

- **Claim:** Spawn_Unit returning `ok=true` from the bridge meant the spawn worked.
- **Confidence:** **REFUTED.**
- **Evidence basis:** Spawn_Unit was invoked multiple times in phase 2k. Each call returned `ok=true`. A follow-up `Find_All_Objects_Of_Type` on the enemy faction still showed only 1 Nebulon and 1 Corvette — the same counts as before the calls. No additional enemy units ever appeared.
- **Next-step verification:**
  1. Re-run the exact spawn Lua alongside pre/post `Find_All_Objects_Of_Type` counts, with a 5s delay on the post-read.
  2. Hook the game-side spawn function with Frida to capture the actual argument tuple and see whether the Lua wrapper even reaches the C++ spawn path.
  3. Cross-reference the argument shape against a vanilla script in `Data/Scripts/` that is known to successfully spawn units.

