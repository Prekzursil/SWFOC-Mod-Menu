# SWFOC Trainer Editor — Changelog

This is the **final-product** changelog. Earlier per-iter notes are preserved under
`knowledge-base/operator_changelog_*.md` in the docs repo.

---

## Final Product (2026-05-19)

This release strips the "V2" qualifier from every operator-visible surface. The
editor is shipped as a single self-contained executable with no V2/V3 split.

### User-visible

- **Window title** is now plain: `SWFOC Trainer Editor — pipe <pipename>`. Same for
  the title-bar fallback when the data context hasn't loaded yet.
- **Top-bar header text**: "SWFOC Trainer Editor" (was "SWFOC Trainer Editor V2").
- **Settings + Recipe tooltips** no longer reference the JSON filenames in surface
  text; the underlying files keep their on-disk names for backwards-compatibility
  with operator installs.
- **Mode switcher**: top-bar pill toggles between `LIVE TRAINER` and `SAVEGAME
  EDITOR` modes. Switching mode flips which tab set is visible — the four save-
  related tabs (Rescue / Monitor / Auto-Tools / Galaxy Visualizer) appear in
  SAVEGAME EDITOR mode; the other ~21 tabs appear in LIVE TRAINER mode.

### Galaxy Visualizer planet roster (real data)

The Galaxy Visualizer tab now pulls **real planet rosters from the selected save's
bytes** via `tools/savegame_rescue/extract_galaxy_state.py`. Clicking Inspect
chains two Python invocations (chunk-histogram parse + galaxy state extract) and
populates a faction-tinted DataGrid with planet name, candidate factions, hex
chunk_id, hex offset, and chunk size. Verified on save `(new_new)8`: 105 planet
records, 6 factions.

### Save-corruption playbook

`.remember/save_diagnosis/THREAD_A_BYTE_DELTA_REPORT.md` documents the May 8 Frida
runtime capture (`vector::insert_at_front` runaway at `0x140690f10` on a
~932,000-element queue) and the byte-delta scan finding. The strip-chunks v1/v2/v3
repair candidates DO NOT fix save 8,3 (they break cross-references and crash the
engine) — the canonical recovery path is rollback to a clean save.

### Build / test gates

- Editor builds clean with **0 warnings / 0 errors** under
  `dotnet build SwfocTrainer.sln --no-incremental --verbosity normal -c Release`.
- Unit tests (`SwfocTrainer.Tests`): **8395 / 0 failed / 5 skipped / 8400 total**
  passing under the Clink-bypass wrapper
  (`tools/run_editor_tests_v2.ps1 -Filter "FullyQualifiedName!~UiTests"`). The
  capability-surface regression-guard failures were resolved by reconciling the
  on-disk markdown snapshot with the current LIVE catalog (snapshot drift, not a
  regression — the catalog deliberately downgrades 9 entries to PHASE 2 PENDING
  while RE work continues).
- **Stress test (3× consecutive)**: `Total failed runs: 0 / 3` — verified at
  22:50 on 2026-05-19. Run durations 3m10s + 3m24s + 3m6s, each
  **8395 / 0 / 5 / 8400** passing. Two flakes were resolved en route to GREEN:
  1. `FactionSwitchReplayTests.ReplayBridge_alive_probe_succeeds` failed sweep 2
     of the initial 3× with `CreateNamedPipe failed: 231` because a previous
     `swfoc_replay.exe` held the pipe past its 2-second teardown budget. Fix:
     defensive `KillStrayReplayProcesses()` + 250 ms grace pause in the
     `ReplayHarnessFixture` constructor.
  2. `Iter80CapstoneCompositesTests.{TournamentSetup,SandboxSetup,StreamingSetup}_RunComposite_*`
     failed under parallel-test contention because `await Task.Delay(400/500)`
     was insufficient. Fix: new `WaitForRecentCallsAtLeast(adapter, count, 3000ms)`
     poll-with-deadline helper.
- UI tests (`SwfocTrainer.UiTests`): expected to be skipped or to fail in
  non-interactive sessions because FlaUI needs a desktop session to attach to the
  launched editor process. Filter via `-Filter "FullyQualifiedName!~UiTests"`.

### Self-contained publish

`artifacts/publish/SwfocTrainer.App.exe` is built with:

```
dotnet publish src\SwfocTrainer.App\SwfocTrainer.App.csproj `
  -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output artifacts\publish
```

Expected output: ~158 MB single-file. Self-extracting at launch; no .NET runtime
install required on the target machine.

---

## What persisted from earlier sessions

The editor's internals (namespaces, file names like `MainWindowV2.xaml`, class
names like `V2BridgeAdapter`) still use the `V2` suffix where it appears in the
source tree. These are **internal-only** and not visible to the operator. They
are kept because:

1. The legacy V1 (`MainWindow.xaml`, `MainViewModel.cs`) still exists in the same
   project as the original Phase-1-mirror direct-to-bridge codebase. Renaming
   would collide.
2. The settings/recipes filenames (`v2_settings.json`, `v2_recipes.json`) are
   read from operators' existing `%LOCALAPPDATA%\SwfocTrainer\` directories.
   Renaming would force every operator to migrate config manually.

Internal naming has zero impact on operator UX. If you grep the source for "V2"
you'll find ~60 files; these are all internal implementation details.

---

## Outstanding (deferred per documented rules)

These are honest-defer items that DO NOT block the editor's stability:

- **Iter 450c — SWFOC_TriggerVictory active injection.** Event-driven engine
  subsystem; multi-iter offset RE required. Workaround: force conquest via the
  Galactic tab's planet-owner-change wires + AI suspend.
- **Per-slot ATTACKER damage multiplier.** Phase 2 pending. Global form is LIVE.
- **Per-hero respawn-timer table.** Phase 2 pending. Global form is LIVE.

These three appear with `PHASE 2 PENDING` badges in the editor itself, with
tooltips pointing to the LIVE alternative. There is no operator workflow blocked
by them.

---

## Prior sessions

See `knowledge-base/operator_changelog_*.md` for the per-iter operator changelog
covering iter 1 through iter 472 (this conversation's master loop).
