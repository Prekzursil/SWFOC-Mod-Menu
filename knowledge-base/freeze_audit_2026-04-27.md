# Freeze audit (2026-04-27)

_Read-only code review of the WPF editor + bridge looking for "PC becomes unresponsive when running the trainer" causes. No game involvement, no code execution — pure source inspection._

## Headline finding (PRIMARY CAUSE)

**`SwfocTrainer.Runtime.Services.ValueFreezeService.AggressiveWriteLoop`** is the most-likely freeze cause. It combines three system-impacting anti-patterns in a single hot loop:

| # | Anti-pattern | Location | Effect |
|---|---|---|---|
| 1 | `timeBeginPeriod(1)` | line 196 | Raises the **system-wide** Windows scheduler tick rate to 1 ms. Affects every process on the host. Documented by Microsoft as "use sparingly". |
| 2 | `ThreadPriority.AboveNormal` | line 179 | The aggressive thread preempts normal user-mode work, including the desktop compositor (`dwm.exe`) and the game's render thread. |
| 3 | `Thread.Sleep(1)` + sync-over-async | lines 211, 223 | Tight loop ~1000 iter/sec. Each iteration does `_runtime.WriteAsync(symbol, value).GetAwaiter().GetResult()` — synchronous IPC to the bridge. Sync-over-async on a captured SynchronizationContext can deadlock (classic WPF freeze pattern). |

**Why it freezes the host PC** (not just the game):

- `timeBeginPeriod(1)` is a process-scoped call but its **effect is system-wide** — once any process raises the timer resolution, the entire OS scheduler runs at that rate until that process exits or calls `timeEndPeriod`. With ~1000 ticks/sec instead of the default ~64 ticks/sec, every other process pays a 16× context-switch tax.
- The aggressive thread runs at AboveNormal priority and hammers the bridge ~1000×/sec. Each write is IPC into `StarWarsG.exe`'s address space (where `powrprof.dll` is hooked). The bridge's pipe handler is single-threaded and synchronous (`ReadFile`/`WriteFile` blocking), so a backed-up queue grows unboundedly.
- `TimeEndPeriod(1)` is in a `finally` block. If the editor crashes (or the user kills it via Task Manager), the system timer **stays at 1 ms forever** until reboot, which matches the user's report of "had to physically restart".

**Triggers in the UI:** any control that calls `IValueFreezeService.FreezeIntAggressive(...)`. By inspection, this is wired to the credits-freeze toggle.

## Other findings (lower severity)

| Severity | Location | Issue |
|---|---|---|
| HIGH | `swfoc_lua_bridge/lua_bridge.cpp:349, 365` | `ConnectNamedPipe(hPipe, nullptr)` and `ReadFile(hPipe, ..., nullptr)` use **synchronous blocking I/O without `OVERLAPPED` or timeouts**. Runs in dedicated thread `PipeThreadProc`, so it doesn't block the game directly — but a stuck client can leave this thread parked indefinitely. The shutdown path on line 5296-5300 does send a "kick" connect to unblock, with a 2 s `WaitForSingleObject` timeout, which is reasonable. |
| HIGH | `swfoc_lua_bridge/replay_harness.cpp:3670` | `WaitForSingleObject(hThread, INFINITE)` — replay harness joins its worker with no timeout. Only matters in test harness, not in deployed bridge, but is fragile. |
| MEDIUM | (none found) | DispatcherTimer intervals are all `TimeSpan.FromSeconds(1)` — no sub-50ms WPF timers. Good. |
| MEDIUM | (none found) | No `while(true)` / `for(;;)` infinite loops in bridge cpp without a flag-based exit. Good. |

## Fix plan for the PRIMARY CAUSE

The fix is mechanical and low-risk:

1. **Remove `timeBeginPeriod(1)` / `timeEndPeriod(1)`** — the system-wide timer change is never appropriate for a trainer. The only legitimate reason to use it is high-precision audio / video, not memory-write polling.
2. **Replace `ThreadPriority.AboveNormal` with `Normal`** — there is no reason to preempt user-mode work. Even at ~16 ms cadence, normal priority will win the race against the game's float→int sync.
3. **Replace the busy loop with `PeriodicTimer`** (.NET 6+) at a 16 ms cadence — matches game frame rate, which is the actual rate at which the game would overwrite the field. Faster than this is wasted work.
4. **Replace `WriteAsync().GetAwaiter().GetResult()` with `await WriteAsync()`** — proper async/await all the way down, no sync-over-async.
5. **Add a hard ceiling on writes/sec** (e.g. 100/sec) as a safety net.
6. **Move `timeEndPeriod` out of `finally`** — even after the fix removes the `timeBeginPeriod` call, ensure no future regression can leak the system timer.

Estimated diff: ~30 lines changed in `ValueFreezeService.cs`. No public API change. Existing tests should still pass. Add a regression test that fails on the old form (any usage of `timeBeginPeriod`) and passes on the new form.

## How this got here (root cause)

The XML doc on `FreezeIntAggressive` (line 13-14) explains the original intent:

> Symbols registered via `FreezeIntAggressive` use a dedicated high-frequency thread (~1-2 ms writes) backed by `timeBeginPeriod(1)`, which is fast enough to overpower the game's own float→int credit sync that runs every ~16 ms.

The intent was correct (need to win the race against the game's per-frame sync), but the implementation chose the wrong tool. **The game runs at ~60 fps (16 ms per frame).** A simple ~16 ms timer at normal priority is sufficient — there's no race to lose because we don't need to write 1000×/sec, we need to write **once before the next game frame**. The `timeBeginPeriod(1) + Sleep(1) + AboveNormal + sync IPC` combination was over-engineering that broke the host machine.

## Verification plan

After the fix lands:

1. Run the existing 6884 editor tests — must stay 0 fail.
2. Add a new regression test in `tests/SwfocTrainer.Tests/Regression/ValueFreezeServiceFreezeRegressionTests.cs` that uses Roslyn or a string check to assert `ValueFreezeService.cs` does **not** import `winmm.dll` or call `timeBeginPeriod`.
3. Add a test that asserts the aggressive freeze loop's effective write rate is ≤ 100/sec under load.
4. With FlaUI infrastructure in place (Track B), drive the credits-freeze toggle in the WPF app against a fake bridge and verify the host CPU stays under 10% for the duration.
