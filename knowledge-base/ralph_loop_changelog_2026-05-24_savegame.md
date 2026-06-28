# Operator Changelog 2026-05-24 — Thread C Savegame-Editor C# Port Arc (spec-iter 286-292)

**Coverage**: savegame-editor spec, steps 286–292 (the deferred C# production
port + WPF tab), committed in the editor repo at master-loop iters 552–577.
**Cadence**: post-arc operator changelog supplement (mirrors the iter-280
NEW-arc-class capstone format: TL;DR + per-iter walk-through + operator
checklist).

## TL;DR — what this gives you

- **A Savegame Editor tab is now in the trainer.** Open a
  `.PetroglyphFoC64Save`, view its chunk hierarchy, repair mod-change
  corruption, edit individual micro-chunks, and re-anchor a drifted mod hash —
  no CLI required.
- **Mod-change corruption is recoverable in-app.** When you update or swap a
  mod, the loader's freshly-computed ObjectType hash no longer matches the one
  baked into your save → crash or silent desync. The validator detects this
  and the **re-anchor** button rewrites the embedded hash to match the active
  mod, restoring playability.
- **Corrupt saves get fixed without launching the game.** The fixer strips
  malformed/truncated trailing chunks while preserving everything before the
  damage (and any trailing clean leaves), then re-validates.
- **This is the C# port of the 2026-05-07 Python CLI toolkit.** The CLI tools
  (`tools/savegame_parser/*.py`) still work; this arc moved that capability
  into the editor as a native tab so it ships in the published binary.
- **All gates green**: savegame test surface 90/0/0; bridge harness 1100/0
  (untouched); ledger lint 0/0 (untouched). Editor binary republished
  (Release, self-contained).

## How to use it (operator workflow)

1. **Open the editor** (republished binary in `artifacts/publish`), switch to
   the **Savegame Editor** tab.
2. **Load** a save from `C:\Users\<you>\Saved Games\Petroglyph\Empire At War -
   Forces of Corruption\Save\`.
3. **If it won't load in-game after a mod change** → run **Validate against
   active mod**. A `Mismatch` status with `Needs re-anchor` means the embedded
   ObjectType hash drifted. Click **Re-anchor** → **Save**.
4. **If the file is structurally corrupt** (truncated autosave, garbage chunk
   header) → run **Fix**. The fixer strips the bad chunk(s) and reports how
   many were dropped; the recovered file re-validates through the parser.
5. **To inspect/edit** → expand the chunk tree, pick a micro-chunk, edit its
   int32 / raw / string value, **Save**. The write-back engine propagates size
   changes up through nested containers automatically.

## Per-step walk-through

### iter-286 — RGMH format RE + master-loader walk (2026-05-07)

**Type**: RE / design. **Anchor**: `RGMH` magic + version `0x01` + struct size
`0x2028` + 16 B UUID + UTF-16LE `"Forces of Corruption game"` label; chunk body
with 8 B headers (bit 31 = sub-chunks), primary container `0x3E8` (1000); 7
micro-chunk type codes. Master loader `sub_140052D10` (RVA `0x140052D10`, 53
callees) walked via `tools/callgraph_query.py`. No new 2-tool RVAs surfaced for
the port → ledger untouched.

### iter-287 — `SavegameParser` (commit `8ec29f2`)

C# port of the chunk reader: header parse, chunk enumeration, micro-chunk
extraction across all 7 type codes (0x00 raw, 0x01–0x04 int32, 0x05
string/blob, 0x06 int array). 8-test pin file (`SavegameParserTests.cs`).

### iter-288 — `SavegameFixer` (commit `b537ea1`)

Corruption fixer: **strip-bad-chunk** (drops the malformed/overflowing chunk,
keeps the rest, including trailing clean leaves) + **truncate-at-failure** +
micro-chunk salvage. No-op on already-clean saves (returns the same buffer).
`SavegameFixerTests.cs` carries the **>80% recovery-rate** gate.

### iter-289 / 289b / 289c — edit engine + view-model + WPF view (`46c115f`, `7bf10f4`, `b902f34`)

`SavegameDocument` + `EditableChunk` give set/delete micro-chunk with
dirty-tracking and automatic nested-container size propagation on `Serialize`.
`SavegameEditorTabViewModel` exposes load/edit/fix/mod-hash actions;
`SavegameEditorTab.xaml` is the UserControl with a binding-contract pin test.

### iter-290 — `ModHashValidator` (commit `bf53a2a`)

CRC32 ObjectType fingerprint (`Crc32.cs`), `Validate(document, modXml) →
{Match | Mismatch}` + `NeedsReAnchor`, and `ReAnchor(document, modXml)` that
rewrites the embedded type-0x01 mod-CRC micro-chunk in the `0x3E8` body.
`ModHashValidatorTests.cs`: known-good match + mismatch types + re-anchor
round-trip → 100% precision (no false positives on matching saves).

### Tab registration (`d8d6b3c`, `b773180`)

Savegame Editor tab wired into the App shell (`MainWindowV2.xaml` +
`MainViewModelV2.cs`), savegame-mode-scoped visibility. A sympathetic stale
Lua-Playground `max_speed` preset pin was corrected to `SWFOC_`-prefixed names
in the same window. Independently review-approved (iter-574).

### iter-291 — end-to-end integration suite (commit `b59d498`)

8 cases exercising the full `parser → fixer → editor → mod-hash` chain on
synthetic in-memory fixtures: truncated chunk, malformed header, damaged
mid-container, clean no-op + delete, full 4-stage mod-context re-anchor, BMP
thumbnail preservation, nested-container size propagation, deep truncation +
combined delete/edit. All 8 green.

## Operator-trust notes

- The savegame surface makes **no bridge calls** and touches **no live game
  process** — it operates on save files at rest. "Fix succeeded" means the file
  re-validates through the parser; "Re-anchor succeeded" means the embedded
  hash now matches the active mod. Confirm playability with an in-game load.
- **Back up your save before fixing.** The fixer is conservative
  (strip-bad-chunk, not rewrite) but corruption recovery is inherently lossy
  for the dropped chunk(s).
- Recovery semantics are fully tested against synthetic corruption; the
  validator is **not** auto-regressed against your real ~214 MB autosave corpus
  (size + privacy). See `iter292_savegame_close_audit.md` §Residuals.

## Verification snapshot

| Gate | Result |
|---|---|
| Savegame editor suite (`Category=Savegame`) | **90 / 0 / 0** (170 ms) |
| Bridge harness | n/a — inherits **1100 / 0** (zero bridge files touched) |
| Verifier ledger lint | n/a — inherits **0 / 0** (ledger untouched) |
| Editor binary | republished — Release, self-contained single-file → `artifacts/publish` |

## Arc status

Thread C (Savegame Editor) is at **acceptance**. With editor-100 (iter-505) and
overlay-interactive (iter-549) already complete, **all 3 specs in `ralph.yml`
are at acceptance** — the master loop is ready for `LOOP_COMPLETE` (appended by
the Ralph coordinator iter).
