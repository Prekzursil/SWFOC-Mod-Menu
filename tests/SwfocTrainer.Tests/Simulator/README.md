# SWFOC In-Memory Simulator Harness

**Created:** 2026-04-27 (iter 22 of the Ralph polish loop)

Lets editor features be functionally tested **without a live SWFOC session**. The simulator pairs the real bridge wire protocol (named pipe + 0x00-terminated ASCII) with a synthetic `FakeGameState` that mutates exactly like the live engine for the subset of `SWFOC_*` functions registered.

## Why this exists

Until iter 22, the only way to verify "did Spawn 3 Rebel troopers actually spawn 3 Rebel troopers" was to alt-tab into a running game and eyeball it. That blocked CI, gated every change on a live session, and missed silent regressions.

The simulator removes that gate. Tests run in-process at unit-test speed, exercise the **same** `V2BridgeAdapter → NamedPipeLuaBridgeClient` stack the editor uses against the real bridge, and assert that **simulated game state mutated** as the feature claims it should.

## Architecture

```
                editor under test                        simulator
   PlayerStateTabViewModel ─┐                         ┌─ SwfocSimulator
   SpawningTabViewModel  ───┤                         │   ├── FakeGameState
   UnitControlTabViewModel ─┤                         │   │   ├── List<FakePlayer>
   ...                      │                         │   │   ├── List<FakeUnit>
                            │                         │   │   ├── List<FakePlanet>
                            ▼                         │   │   └── HashSet<StoryFlags>
                  V2BridgeAdapter ──── named pipe ───►│   └── handlers (per SWFOC_* fn)
                            │       (real protocol)   ▼
                  NamedPipeLuaBridgeClient        FakeBridgePipeServer
```

Three layers, sharply separated:

| Layer | File | Responsibility |
|---|---|---|
| Transport | `FakeBridgePipeServer.cs` | Named-pipe loop, 0x00 terminator, longest-prefix dispatch |
| State | `FakePlayer.cs` / `FakeUnit.cs` / `FakePlanet.cs` / `FakeGameState.cs` | Mock world data |
| Semantics | `SwfocSimulator.cs` | Per-function handlers; parses Lua args; reads/mutates state |

## How to use it in a test

```csharp
[Fact]
public async Task SpawnUnit_AddsAliveUnitsToWorld()
{
    var state = FakeGameState.NewTacticalSkirmish();
    using var sim = new SwfocSimulator(state);
    sim.Start();

    var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
    var adapter = new V2BridgeAdapter(pipe);

    var round = await adapter.SendRawAsync(
        "return SWFOC_SpawnUnit(\"Rebel_Trooper_Squad\", 0, 3)",
        CancellationToken.None);

    round.Succeeded.Should().BeTrue();
    state.Units.Should().HaveCount(3);
    state.Units.All(u => u.Alive).Should().BeTrue();
}
```

The `using var` block tears down the named pipe; each test gets a fresh simulator on a per-test pipe name (no cross-test interference).

## Adding a new handler (Phase B onwards)

When the editor calls a new `SWFOC_*` function and the simulator returns `(sim: unhandled probe — handler not registered)`, do the following:

1. **Identify the wire format** — what does the real bridge return for this function? Check the call site in `swfoc_lua_bridge/lua_bridge.cpp` and the editor's parse code in the relevant `*TabViewModel.cs`.
2. **Add a handler in `SwfocSimulator.RegisterHandlers()`**:
   ```csharp
   Reg("return SWFOC_YourFunc", HandleYourFunc);
   ```
3. **Implement the handler**: parse args from the Lua call (use `ExtractFirstIntArg` / `ExtractIntArgs` / `ExtractFirstStringArg`), mutate `GameState`, return the wire string. Always slice inside the parens — function names like `SWFOC_SetHumanPlayer_v3` contain digits.
4. **If you need new state** (e.g. tech levels, hardpoints, AI behavior trees), add fields to `FakeGameState` or a new `Fake*.cs` file.
5. **Add an E2E test** in `E2E/SwfocSimulatorEndToEndTests.cs` that drives the function through the real adapter stack and asserts state.

## What's covered (Phase A + B + C — iters 22-23)

| Domain | Functions | Tests | File |
|---|---|---|---|
| **Phase A — diagnostics** | `GetVersion`, `GetBuildInfo`, `DiagListRegisteredFunctions`, `DiagSelfTest` | 1 | `E2E/SwfocSimulatorEndToEndTests.cs` |
| **Phase A — player state** | `GetAllPlayers`, `GetLocalPlayer`, `SetHumanPlayer_v3`, `GetCredits`, `SetCredits` | 4 | same |
| **Phase A — spawning** | `BatchTypeExists`, `ListUnitTypes`, `SpawnUnit` | 3 | same |
| **Phase A — unit control** | `ListTacticalUnits`, `KillUnit`, `SetUnitInvuln`, `SetUnitHull`, `PreventUnitDeath` | 5 | same |
| **Phase A — galactic / story** | `RevealAll`, `GetPlanets`, `FireStoryEvent` | 3 | same |
| **Phase A — operator scenarios** | spawn → kill → faction-switch chains, multi-step galactic flows | 4 | `E2E/OperatorScenarioTests.cs` |
| **Phase B — combat scalars** | `SetDamageMultiplier`, `SetFireRate`, `SetUnitShield`, `OneHitKill` | 5 | `E2E/PhaseBSimulatorTests.cs` |
| **Phase B — speed** | `SetGameSpeed`, `SetPerFactionSpeedMultiplier`, `SetUnitSpeed` | 3 | same |
| **Phase B — hero lab** | `ListHeroes`, `HeroInstantRespawn`, `HeroStatEdit`, `SetHeroRespawn`, `SetPermadeath` | 6 | same |
| **Phase B — AI brain** | `GetAiBrain`, `NullAiBrain`, `AttachAiBrain`, `FreezeAI` | 3 | same |
| **Phase B — diplomacy** | `SetDiplomacy` (Allied / Neutral / Hostile + reject bad relation) | 2 | same |
| **Phase B — economy** | `SetIncomeMultiplier`, `DrainEnemyCredits` | 2 | same |
| **Phase B — galactic** | `ChangePlanetOwner`, `GetTechForSlot`, `SetTechForSlot`, `InstantBuild` | 2 | same |
| **Phase B — event stream** | `EventStreamDrain`, `EventControl` (clear / pause / resume) | 3 | same |
| **Phase C — global toggles** | `GodMode`, `HealAllLocal`, `FreeBuild`, `FreeCam`, `FreezeCredits`, `ToggleOHKAttackPower`, `CombinedGodOHK`, `UncapCredits`, `GetMaxCredits` | 9 | `E2E/PhaseCSimulatorTests.cs` |
| **Phase C — inspector** | `InspectUnit`, `GetSelectedUnit`, `EnumerateUnits` | 3 | same |
| **Phase C — hardpoints** | `GetHardpoints` (status reflects invuln flag) | 2 | same |
| **Phase C — overrides** | `SetTargetFilter`, `SetUnitCapOverride`, `SetUnitField` | 3 | same |
| **Phase C — diagnostics** | `DiagGameTick`, `DumpState`, `Log`, `GetPlayerWrapper`, `SetCreditsForSlot` | 6 | same |

**Total**: ~50 SWFOC_* functions registered, **66 simulator E2E tests**, all running against the real `V2BridgeAdapter → NamedPipeLuaBridgeClient` stack.

## Phase D candidates (future iterations)

1. **VM-driven scenario tests** — instead of bypassing the editor and calling `SendRawAsync` directly, construct real `PlayerStateTabViewModel` / `SpawningTabViewModel` / `UnitControlTabViewModel` instances and drive them through their public commands (`RefreshSlotMapCommand`, `SpawnCommand`, etc.). Validates the *full* editor stack including command can-execute logic, observable-collection updates, and INPC notifications.
2. **Stress tests** — 1000-unit spawn/kill cycles to find perf regressions or memory leaks in the bridge transport.
3. **Concurrency tests** — overlapping refresh/auto-refresh cycles to find race conditions in the auto-refresh driver against bridge replies.
4. **Wire-format fuzzing** — mutate response bytes and verify the editor's parsers reject malformed input gracefully (don't crash, surface a useful error).
5. **Scenario builders** — fluent helpers like `FakeGameState.WithUnits(n).WithHero("Han_Solo").WithDeadFromSlot(2)` so test setup gets terser as coverage grows.

## What this does NOT replace

- **Live RVA verification** — when the bridge gets a new C++ function, the ledger still needs 2-tool consensus before it can be marked VERIFIED. The simulator only proves the editor's BRIDGE-CALL path is correct; it doesn't validate that the bridge itself implements the function correctly against the engine.
- **WPF window tests** — the simulator works at the bridge layer. Pixel-level theme verification (`DarkModeContrastTests`) and FlaUI-driven UI walks (`WpfTabAuditTests`) still need a launched window.
- **End-to-end live-game smoke** — the final pre-release pass still uses `knowledge-base/go_live_smoke_checklist_*.md` against an actual SWFOC session. The simulator catches *most* breakage before that point.
