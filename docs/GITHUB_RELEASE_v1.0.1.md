# v1.0.1 — Bridge Hotfix (2026-05-20)

Single-day hotfix release. **Editor binary is unchanged from v1.0.0.** Only the bridge DLL (`powrprof.dll` deployed alongside `StarWarsG.exe`) is updated.

This release fixes two operator-reported bugs surfaced during live-game testing of v1.0.0 against vanilla SWFOC galactic mode.

---

## Fixes

### Credits multiplier now scales income only

**Before**: `SWFOC_SetCreditsMultiplierGlobal(10)` made unit costs 10× as well as daily income, so the buff actually made you poorer faster.

**Root cause**: SWFOC routes 47 different credit operations (gains, spends, rewards, payroll, building costs) through a single `AddCredits` engine function. The bridge's hook scaled `delta * multiplier` regardless of sign — multiplying both income (positive delta) and costs (negative delta).

**Fix**: Sign-gate at the hook level. Positive deltas (income) are scaled; negative deltas (purchases) pass through unchanged.

**Verification**: Live-game observation with 5× multiplier active during AI ticks — UNDERWORLD (player) gained 5× normal income; REBEL and EMPIRE (AI) spent credits at unchanged rates. The differential ratio is the proof of the sign-gate.

### `SWFOC_DoString` now returns the evaluated value

**Before**: `SWFOC_DoString("return 1+2+3")` returned literal `1` instead of `6`. The bridge was discarding the value the user's code pushed onto the Lua stack and replacing it with a hard-coded success flag.

**Fix**: On success, return what the executed code pushed. On failure, return `(nil, errmsg)`. The Lua Playground tab and all `Bridge*Dispatcher.cs` consumers that wrap as `return SWFOC_DoString(...)` now see actual results.

### `Lua_ListMods` snprintf hardening

**Before**: Semgrep flagged a `CWE-787 Out-of-bounds Write` pattern at `lua_bridge.cpp:3225-3236`. `snprintf` return value (which can exceed the buffer size on truncation) was used directly as a `memcpy` length.

**Fix**: Clamp `rowLen` to `sizeof(row)-1` on truncation; skip iteration on encoding error (`rowLen < 0`). Defensive — no exploitable in-the-wild path identified, but the pattern is now safe.

---

## Verification

**Autonomous test battery**: 48 probes, 43 PASS + 3 honest PHASE 2 PENDING + 1 tactical-only block (expected) + 1 known pre-existing engine quirk. Full report at `.remember/autonomous_test_report_2026-05-20.md`.

**Build gates** (unchanged from v1.0.0):
- Editor build: 0 warnings / 0 errors (`dotnet build -c Release --no-incremental`)
- Editor unit tests: **8404 / 0 failed / 5 skipped / 8409 total**
- Bridge harness (C++): **1100 / 0** (also re-run after the credits-mult fix)
- Replay binary smoke: **12 / 12**
- Verifier RVA ledger lint: **0 / 0 @ 341 entries**
- Stress test 3× consecutive: **3 / 3 GREEN**

---

## Installation

### If upgrading from v1.0.0

Only the bridge DLL needs replacing. Editor binary stays the same.

```powershell
# Verify the new bridge before deploying
Get-FileHash powrprof.dll -Algorithm SHA256
# Expected: FAEA82B6F08F3162ED9E55ADDAB2F727B94174999271D28F577CF2677904A824

# Deploy alongside StarWarsG.exe
Copy-Item powrprof.dll "$gameDir\powrprof.dll" -Force
```

### Fresh install

1. Download `SwfocTrainerEditor_v1.0.1.zip` from the release assets.
2. Verify both SHA256 checksums against `SHA256SUMS.txt`.
3. Copy `powrprof.dll` into your SWFOC `corruption` folder (alongside `StarWarsG.exe`).
4. Run `SwfocTrainer.App.exe` and connect via the Connection & Diagnostics tab.

---

## What's still PHASE 2 PENDING (carried over from v1.0.0)

These three engine surfaces remain badged orange in the editor with their LIVE alternatives cited in the tooltip. No change from v1.0.0 — the hotfix scope was the bridge bugs, not the deferred RE work.

- `SWFOC_TriggerVictory` (force a victory condition). Workaround: Galactic tab planet-owner change + AI suspend.
- Per-slot attacker damage multiplier. LIVE alternative: `SWFOC_SetDamageMultiplierGlobal`.
- Per-hero respawn-timer table. LIVE alternative: `SWFOC_SetHeroRespawn`.

iter-450a (VictoryMonitor AwaitingVictoryTestType field layout RE) is documented in `knowledge-base/iter450a_victory_monitor_layout_decode_kickoff.md` and ready for a future v1.1 / v1.2 RE session.

---

## SHA256 checksums

```
84F96AB2ABA954698C17CACDAD67617DD393F6DAA46A3692BB3D3EB7E7B751EE  SwfocTrainer.App.exe (unchanged from v1.0.0)
FAEA82B6F08F3162ED9E55ADDAB2F727B94174999271D28F577CF2677904A824  powrprof.dll        (NEW for v1.0.1)
```
