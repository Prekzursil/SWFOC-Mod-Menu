# iter-292 — Thread C Savegame-Editor C# Port arc FINALE (7/7): close-out audit

**Date:** 2026-05-24
**Spec:** `.ralph/specs/savegame-editor.md` (savegame-engineer hat)
**Arc class:** Thread C C#-production-port — multi-iter arc finale (mirrors iter-228 / 234 / 240 / 246 / 261 / 279 template)
**Spec iter chain:** spec-iter 286 → 292 (7 steps)
**Predecessors:** iter-286 (RE kickoff) + iter-287 (parser) + iter-288 (fixer) + iter-289/289b/289c (edit engine + view-model + WPF view) + iter-290 (mod-hash validator) + iter-291 (integration tests).
**Successor:** Ralph coordinator iter — appends `LOOP_COMPLETE` to STATUS.md (all 3 specs at acceptance).

> **Numbering-collision reconciliation (READ FIRST).** This arc's spec-iter
> numbers (286–292) collide with the master-loop iters 286–293 that ran on
> 2026-05-07. Those earlier docs (`iter286_savegame_format_re_kickoff.md` …
> `iter293_thread_c_close.md`) closed the **Python CLI toolkit**
> (`tools/savegame_parser/*.py`) and explicitly **deferred** the C# port +
> WPF tab to "iter-294+ optional UX polish" (see `iter293_thread_c_close.md`
> §"Iter-294+ candidates → Track A"). The `savegame-editor.md` spec is that
> deferred Track A, now executed as the `SwfocTrainer.Savegame` C# project +
> `SavegameEditorTab` WPF surface. This file (`iter292_savegame_close_audit.md`)
> is therefore a **distinct deliverable** from `iter292_savegame_strip_fix_complete.md`
> (Python `strip-missing-types` close) — it closes the C# port, not the CLI.
> The git commits carry the spec-iter numbers (`feat(iter-287)` … `test(iter-291)`),
> committed in the editor repo at master-loop iters 552–577 on 2026-05-22.

## Headline

**Thread C savegame-editor C# port arc COMPLETE.** The deferred Track-A port
shipped a self-contained `SwfocTrainer.Savegame` library (parser → fixer →
edit/write-back engine → CRC32 mod-hash validator), a `SavegameEditorTab` WPF
surface registered in the App shell, and an 8-case end-to-end integration
suite exercising the spec's full `parser → fixer → editor → mod-hash` chain.
Savegame test surface is green at **90 / 0 / 0**; no bridge or ledger files
were touched across the arc (both gates inherit clean).

| Metric | Value |
|---|---|
| Spec steps | **7 / 7 = 100%** (iter 286–292) |
| Editor-repo commits (release/v1.0.2) | **9** (8ec29f2 → b59d498) |
| Savegame surface files changed | **25** |
| Savegame surface insertions | **+3,927** |
| `SwfocTrainer.Savegame` source files | **15** (.cs) + 1 .csproj |
| Savegame test files | **7** (6 unit/binding + 1 integration) |
| Savegame test count | **90** (`Category=Savegame`) |
| Savegame test result | **90 passed / 0 failed / 0 skipped** (170 ms) |
| Bridge harness | n/a (zero bridge files touched) — inherits **1100 / 0** |
| Verifier ledger lint | n/a (verified_facts.json untouched this arc) — inherits **0 / 0** |
| Editor binary | **republished** (Release, self-contained single-file → `artifacts/publish`) |

## What shipped across the arc (spec-iter 286–292)

| Spec iter | Commit | Deliverable | Notes |
|---|---|---|---|
| 286 | (RE, 2026-05-07) | RGMH chunk-format RE + `0x140052D10` master-loader walk | Format anchors carried into the C# port; ledger untouched (no new 2-tool RVAs surfaced this arc). |
| 287 | `8ec29f2` | `SavegameParser` — RGMH header parse + chunk enumeration + 7 micro-chunk type codes (0x00 raw, 0x01–0x04 int32, 0x05 string/blob, 0x06 int array) | `SavegameParser.cs` + `SavegameHeader.cs` + `SaveChunk.cs` + `MicroChunk.cs` + `SavegameReport.cs` + `SavegameFormatException.cs`; `SavegameParserTests.cs` (311 LoC). |
| 288 | `b537ea1` | `SavegameFixer` — strip-bad-chunk + truncate-at-failure + micro-chunk salvage; `>80%` recovery gate | `SavegameFixer.cs` + `SavegameFixReport.cs` + `SavegameFixStrategy.cs`; `SavegameFixerTests.cs` (365 LoC). No-op on already-clean saves (`Strategy.None`, `Output` same-ref). |
| 289 | `46c115f` | edit/write-back engine — `SavegameDocument` + `EditableChunk` (set/delete micro-chunk, dirty-tracking, nested-container size propagation on `Serialize`) | `SavegameDocument.cs` + `EditableChunk.cs`. |
| 289b | `7bf10f4` | `SavegameEditorTabViewModel` — load / edit / fix / mod-hash actions | parameterless-ctor view-model. |
| 289c | `b902f34` | `SavegameEditorTab.xaml` WPF view — UserControl + binding-contract pin | `SavegameEditorTabTests.cs` (316) + `SavegameEditorTabViewBindingTests.cs` (130). |
| 290 | `bf53a2a` | `ModHashValidator` — CRC32 ObjectType fingerprint + `Validate` + `ReAnchor` round-trip | `ModHashValidator.cs` + `Crc32.cs` + `ModHashStatus.cs` + `ModHashValidationResult.cs`; `ModHashValidatorTests.cs` (13 KB). |
| (561) | `d8d6b3c` | register Savegame Editor tab in App shell (MainWindowV2.xaml + MainViewModelV2.cs) | App-shell wiring; `Iter561SavegameEditorTabRegistrationTests.cs`. Owned by editor-polish, committed during the savegame arc tail; review-approved iter-574. |
| (562) | `b773180` | correct stale `max_speed` honest-defer preset pin to `SWFOC_`-prefixed names | sympathetic Lua-Playground pin fix that travelled with tab registration. |
| 291 | `b59d498` | 8-case end-to-end integration suite | `SavegamePipelineIntegrationTests.cs` (407 LoC). Full 4-stage chain. |

### iter-291 integration suite — 8 end-to-end cases

`tests/SwfocTrainer.Tests/Savegame/IntegrationTests/SavegamePipelineIntegrationTests.cs`
runs each corrupt buffer through `Fixer.Fix → Parser re-validate → Document
open + mutate + Serialize → Parser re-verify`, with one case adding the
`ModHashValidator` stage so all four spec stages fire on one fixture:

1. `TruncatedFinalChunk_FixStrips_MicroChunkEditRoundTrips` — corruption type 1 (truncated mid-chunk).
2. `MalformedChunkHeader_FixStrips_MicroChunkEditRoundTrips` — corruption type 2 (garbage size field).
3. `DamagedMidContainer_FixKeepsTrailingLeaves_EditRoundTrips` — strip-bad-chunk keeps trailing leaves a truncate would lose.
4. `AlreadyCleanSave_FixIsNoOp_MicroChunkDeleteRoundTrips` — no-op fix + micro-chunk delete persists.
5. `ModContextSave_FixThenValidateThenReAnchor_AllFourStagesGreen` — full `parser → fixer → editor → mod-hash` chain + mismatch → re-anchor → match.
6. `BmpThumbnailSave_FixPreservesThumbnail_EditRoundTrips` — BMP thumbnail prefix survives fixer + editor (corruption type 3 region).
7. `NestedContainerSurvivesFix_NestedMicroChunkEdit_SizePropagates` — child-leaf micro-chunk growth propagates size up the container.
8. `DeepTruncation_FixRecovers_DeleteAndEditCombined_RoundTrips` — two mutations (delete + edit) survive one round-trip.

## Acceptance-criteria audit (spec §"Acceptance criteria")

| Criterion | Status | Evidence |
|---|---|---|
| Parser enumerates all chunks (100%) | ✅ MET | `SavegameParserTests` (311 LoC) — header parse + chunk enum + all 7 micro-chunk type codes round-trip. |
| CLI fixer recovers playable save (>80% recovery) | ✅ MET | `SavegameFixerTests` (365 LoC) carries the `>80%` recovery-rate gate; strip-bad-chunk + truncate-at-failure + micro-chunk salvage. |
| WPF editor read → modify → write (100% test cases) | ✅ MET | `SavegameEditorTabTests` (316) + `SavegameDocumentTests` + integration round-trips: read, mutate micro-chunk, write back, re-read, assert mutation persisted. |
| Mod-hash validator flags mismatch (100% precision, no false positives) | ✅ MET | `ModHashValidatorTests` — known-good match + mismatch types + re-anchor round-trip; integration case #5 confirms `Match` after re-anchor. |
| End-to-end smoke (4 stages green per fixture) | ✅ MET | `SavegamePipelineIntegrationTests` — 8 cases, 4-stage chain. |
| Master loader RVA `0x140052D10` walked + verified | ✅ MET (iter-286 RE) | Walked during the 2026-05-07 RE kickoff; no new 2-tool RVAs surfaced for the C# port, so the ledger was not extended this arc. |
| Verifier ledger lint 0/0 | ✅ INHERITED | `verified_facts.json` untouched across the arc. |
| Editor full suite green | ✅ MET | `Category=Savegame` 90/0/0; full suite green per recent runs (iter-574 reviewer pass + iter-577). |
| Bridge harness 1100/0 | ✅ INHERITED | zero bridge files touched (Thread C is editor-side only). |

### Honest deviation — synthetic in-code corpus vs `Fixtures/` directory

The spec acceptance text references physical fixture directories
(`tests/SwfocTrainer.Tests/Savegame/Fixtures/known_good/` and `/corrupt/`)
and JSON sidecar inventories. **The implementation uses synthetic in-memory
savegame buffers and in-code `SavegameInventory` records instead** — there is
no physical `Fixtures/` directory. This is a deliberate, documented divergence
(see the `SavegamePipelineIntegrationTests` class XML doc and iter-577 close):
the synthetic buffers exercise the same evidence (3 corruption types, BMP
thumbnail, mod-context, nested container) with no file-copy indirection and
are consistent with the five sibling savegame test files. The user's real
~214 MB `[AutoSave].PetroglyphFoC64Save` corpus was the RE input back in
iter-286 but is not bundled into the test tree (size + privacy). **Net effect:
the *recovery semantics* are fully covered; the *real-corpus regression* is
not automated.** Tracked as a residual below.

## Arc-level pattern lessons

### Lesson #1 — Deferred polish can become the spec, with collision cost

iter-293 (2026-05-07) deferred the C# port as "optional UX polish." It was
later promoted to a first-class spec (`savegame-editor.md`) but **reused the
same iter numbers (286–292)**, colliding with the master loop. The cost
surfaced as repeated phantom-recovery confusion (iters 552–578) where agents
could not tell whether "iter-292" meant the Python close or the C# close.
**Pattern:** when promoting a deferred track to a new spec, give it a fresh,
non-overlapping step-number namespace (or prefix step IDs with the spec name)
to avoid cross-referencing ambiguity in a long-running loop.

### Lesson #2 — Synthetic in-memory fixtures beat file fixtures for binary-format round-trip tests

All 90 savegame tests build their inputs in code (`BuildHeader`, `Leaf`,
`Truncated`, `Container`, `BmpBlock`). This kept the test tree free of large
binary blobs, made every byte of every fixture auditable in the diff, and let
each case state its expected chunk inventory inline. **Pattern:** for
binary-format parsers/fixers, prefer programmatic fixture builders over
checked-in sample files — the cost is a small builder helper; the payoff is
zero binary bloat + fully-reviewable fixtures + trivial edge-case authoring.
(Trade-off: real-corpus regression must be tracked separately — see residual.)

### Lesson #3 — Phantom-recovery discipline survives a 10-iter missed-emit storm

The arc tail (iters 561–578) hit a 10-iter missed-`emit` chain. Recovery held
because every iter (a) re-read git head + working tree from disk before
acting, (b) committed before emitting (guardrail 1012), and (c) the binding
hat-guidance travelled in the scratchpad across re-emits. **Pattern:** disk is
ground truth; the compaction summary and the event bus are both lossy. Verify
the side effect (commit landed, file on disk), never the intent.

## Verification gates (this iter)

| Gate | Result | Source |
|---|---|---|
| Savegame editor suite (`Category=Savegame`) | **90 / 0 / 0** (170 ms) | `logs/iter292_savegame_tests.log` |
| Solution build | clean (Debug, via test wrapper) | same run |
| Editor binary republish (Release, self-contained) | see `logs/iter292_republish.log` | `dotnet publish … -p:PublishSingleFile=true -p:SelfContained=true → artifacts/publish` |
| Bridge harness | n/a — inherits **1100 / 0** | zero bridge files touched |
| Verifier ledger lint | n/a — inherits **0 / 0** | `verified_facts.json` untouched |

## Residuals (non-blocking)

1. **Real-corpus regression not automated** — the synthetic in-code corpus
   covers recovery semantics but not the user's real ~214 MB autosave. A
   future iter could add an opt-in, env-gated test that runs the fixer over a
   local corpus path when present (skipped in CI).
2. **iter-574 reviewer FYIs** (forwarded, not filed against the diff):
   `SavegameDocument.LoadAsync` LOH slurp of large saves + path-validation
   hardening on the editor tab's file-open. Both are robustness polish, not
   correctness defects.
3. **Numbering namespace** — leave the spec-iter labels as committed; do not
   renumber history. Future specs should namespace their step IDs.

## Close-out checklist

- [x] `SwfocTrainer.Savegame` C# library complete (15 source files).
- [x] `SavegameEditorTab` WPF surface + App-shell registration (committed + review-approved iter-574).
- [x] 8-case end-to-end integration suite (`test(iter-291)` `b59d498`).
- [x] Savegame test surface green (90 / 0 / 0).
- [x] Bridge harness + ledger lint inherit clean (untouched).
- [x] Editor binary republished (Release self-contained → `artifacts/publish`).
- [x] iter-292 close-out audit written (this file).
- [x] Operator changelog supplement written (`ralph_loop_changelog_2026-05-24_savegame.md`).
- [x] STATUS.md updated with Thread C completion.
- [ ] `LOOP_COMPLETE` — deferred to the Ralph coordinator iter (all 3 specs at acceptance).

## Closing capstone

The savegame editor is now a **first-class in-trainer surface**, not just a CLI
toolkit: an operator can open a `.PetroglyphFoC64Save`, see its chunk
hierarchy, fix mod-change corruption, edit micro-chunks, and re-anchor a
drifted mod hash — all from a WPF tab in the published binary. Thread C
(Savegame Editor) joins editor-100 (acceptance @ iter-505) and
overlay-interactive (complete @ iter-549) at acceptance. With this arc closed,
**all 3 specs in `ralph.yml` are at acceptance** and the master loop is ready
for `LOOP_COMPLETE`.
