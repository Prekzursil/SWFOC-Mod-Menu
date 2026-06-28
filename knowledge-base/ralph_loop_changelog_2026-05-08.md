# Ralph Loop Operator Changelog — 2026-05-08

**Coverage**: iter 294 (Mandate-expansion audit) + iter 296-310 (Thread D asset-extraction arc + post-finale closeouts)

**Predecessor changelog**: `knowledge-base/ralph_loop_changelog_2026-05-06.md` (covered iter 197-220 native UX surfacing)

**Format**: walk-through-every-tab + thread-summary mirroring iter-196's template.

---

## Section 1 — Thread D arc summary (iter 304-310, 7 iters)

**The mandate**: user-stated "nice GUI showing units by their in-game pictures" + "uncluttered UI/UX."

**The shape**: 7-iter arc spanning 4 architectural layers + 2 languages.

| Iter | Layer | LoC | Output |
|------|-------|-----|--------|
| 304 | Python CLI | ~200 | `meg_parser.py` — V1 .meg archive parser/extractor |
| 305 | Python CLI | ~190 | `dds_decoder.py` — Pillow-wrapper DDS decoder (saved ~150 LoC vs bespoke DXT) |
| 306 | Python CLI | ~165 | `thumbnail_cache.py` — content-keyed PNG cache (SHA256 + size suffix) |
| 307 | C# Core | ~285 | `ThumbnailCache.cs` — read-side mirror of iter-306 cache layer |
| 308 | C# Core + App | ~290 | `UnitIconResolver.cs` + `UnitTypeRow.cs` + Spawning tab ListBox `<Image>` template |
| 309 | C# Core + App | ~210 | `V2Settings.IconsRoot` + `MainViewModelV2.ResolveIconsRoot` composition-root wiring |
| 310 | C# App | ~290 | Settings tab "Unit icons" GroupBox (TextBox + Browse + 3-state status badge) |

**Total Thread D LoC**: ~1,630 (Python ~555 + C# ~1,075). End-to-end .meg-to-WPF-icon pipeline complete in under 1700 LoC across 7 iters.

**Operator-visible**: Settings tab → Unit icons GroupBox → Browse → pick extracted-textures root → Save → restart editor → Spawning tab renders unit-type icons.

---

## Section 2 — Operator workflow (canonical, end-to-end)

```bash
# === ONE-TIME PER GAME INSTALL ===

# 1. Extract MasterTextures.meg (operator's game install path varies)
python tools/asset_extractor/meg_parser.py \
    "C:\Games\SWFOC\corruption\Data\MasterTextures.meg" \
    --extract-all C:\Games\SWFOC\extracted

# 2. Cache thumbnails for every extracted DDS
for dds in C:\Games\SWFOC\extracted\Data\Art\Textures\Units\*.dds; do
    python tools/asset_extractor/thumbnail_cache.py "$dds" --size 32
done

# === ONE-TIME PER EDITOR LAUNCH ===

# 3. Configure editor
#    Settings tab → Unit icons GroupBox → Browse... → pick C:\Games\SWFOC\extracted
#    → click Save → restart editor

# === PER SESSION ===

# 4. Open editor → Spawning tab → unit-type ListBox now renders icons
```

**Alternative configuration paths** (iter-309 ResolveIconsRoot precedence):
1. `settings.IconsRoot` (Settings tab UI from iter-310)
2. `SWFOC_EXTRACTED_DDS_ROOT` env var (session-only)
3. Direct edit of `%APPDATA%\SwfocTrainer\v2_settings.json`

All three paths converge at the same `UnitIconResolver` constructor argument.

---

## Section 3 — Settings tab additions

### iter-301 mod-picker GroupBox (already shipped; covered for context)
- "Available Mods" GroupBox with `Currently loaded` badge + DataGrid (Loaded/Name/Path columns) + `Refresh mods` + `Open Mods folder` buttons
- Bridge wires: `SWFOC_GetCurrentMod` (iter-299) + `SWFOC_ListMods` (iter-300)

### iter-310 unit-icons GroupBox (NEW this arc)
- "Unit icons (Spawning tab)" GroupBox at Settings tab Grid Row 10
- Input row: label + TextBox (two-way bound to `IconsRoot`) + `Browse...` button (.NET 8 `OpenFolderDialog`)
- Status row: 3-state badge:
  - `(unset — set this or SWFOC_EXTRACTED_DDS_ROOT env var to see unit icons)` — operator hasn't configured anything
  - `(directory not found)` — operator typo'd path, OR pre-extraction
  - `(no i_button_*.dds files found — run python tools/asset_extractor/meg_parser.py to extract MasterTextures.meg first)` — empty dir
  - `Found N icons (restart editor for changes to take effect)` — happy path
- Status badge counts via the SAME 5-candidate-relpath walk as iter-308 `UnitIconResolver.LocateDds` (badge agrees with what resolver surfaces).

**Settings tab now hosts 2 native GroupBoxes**: Mods + Unit icons. Both follow identical shape (input row + status badge + reuses existing SaveCommand chain).

---

## Section 4 — Spawning tab additions

### iter-308 unit-icon column (NEW this arc)
- Spawning tab unit-type ListBox: `ItemsSource` flipped from `FilteredTypes` (string collection) → `FilteredTypeRows` (`UnitTypeRow` collection) via `SelectedValue`/`SelectedValuePath="TypeId"` indirection
- `<DataTemplate>` with `<StackPanel Orientation="Horizontal">` of `<Image Width="32" Height="32" Source="{Binding IconPath}"/>` + `<TextBlock Text="{Binding TypeId}"/>`
- Null `IconPath` silently hides the Image control (no broken-image placeholder)
- Resolver wired at MainViewModelV2 composition root (iter-309) reads from `Settings.IconsRoot` (iter-310) → operator-configurable

**Spawn-flow logic UNCHANGED** — `SelectedTypeId` string property still used everywhere. WPF `SelectedValue`/`SelectedValuePath` extracts `row.TypeId` for binding. Filter, search, faction/domain filters all key off the underlying `FilteredTypes` string collection unchanged.

---

## Section 5 — Mandate-expansion audit (iter 294 context)

The arc started with iter-294's parallel-agent audit identifying 4 dimensions of operator value gaps:
- **Audit A**: dynamic loading of planets/factions/units (iter-296 → iter-303 closed)
- **Audit B**: enumeration wires (mod picker / current mod / planets) — 5 of 6 closed via iter-296/299/300/301/303
- **Audit C**: savegame integrity (iter-297 + iter-298 closed)
- **Audit D**: asset extraction (iter-304 → iter-310 ARC) ← **THIS ARC**

Thread D's 7 iters closed Audit D end-to-end. Audit B has 1 remaining wire (`faction-roster-by-build-tab` — likely iter-312+).

---

## Section 6 — Test count + verification gates

| Iter | Tests | Status | Notes |
|------|-------|--------|-------|
| 304 | 7/7 | PASS (Python smoke) | Cleanup before close-out |
| 305 | 7/7 | PASS (Python smoke) | Cleanup before close-out |
| 306 | 8/8 | PASS (Python smoke) | Windows CP1252 trap RECURRENCE caught (2nd instance) |
| 307 | 21/21 | PASS in 44 ms | Iter-282 direction-B drift catch (3rd application) |
| 308 | 20/20 | PASS in 98 ms | 3 mid-iter bug catches (path-separator × 2 + xUnit env-var race) |
| 309 | 12/12 | PASS in 37 ms | Iter-308 [Collection] orthogonality validated |
| 310 | 12/12 | PASS in 43 ms | Combined Thread D 65/65 in 176 ms |

**Cumulative Thread D editor-side**: **65 pin tests** (21 iter-307 + 20 iter-308 + 12 iter-309 + 12 iter-310). All GREEN, no regression at any step.

**Bridge harness 1100/0 inherited** continuously (no bridge changes in iter 304-310 arc).

**Verifier ledger lint 0/0 at 318 entries** inherited (no ledger changes).

---

## Section 7 — Pattern-lesson capstone

**4 NEW pattern lessons emerged from this arc** (each at 2-3 instances; most ready for codification at next recurrence).

### 1. Engine-already-does-this extends through 6 layers (iter-302 codified, applied 4× more here)

iter-302 codified the rule "before pinning a new RVA, check the cheaper mechanisms." This arc extended the decision tree to **5 layers**:
1. Engine Lua API (iter-302 baseline)
2. Established library (iter-305 Pillow)
3. In-repo Python infra (iter-306 reused iter-298 + iter-305)
4. Pre-existing C# project (iter-307 caught `SwfocTrainer.Meg` already existing)
5. Filesystem convention walk (iter-308 5-candidate-relpath)

**6 instances of iter-302 in 7 iters.** Compound interest from codified rules paid back at exactly the rate the codification iter (iter-302) predicted.

### 2. Iter-282 bidirectional infra-claim drift direction B (4 applications)

iter-282 codified "before adding code to do X, grep for X-already-existing." The arc applied direction B (claims of MISSING infra were wrong) 4 times:
- iter-307: `SwfocTrainer.Meg` C# project existed — saved ~200-400 LoC of duplicate port work
- iter-308: existing iter-282 + iter-307 evidence informed the resolver scope decision
- iter-309: `SettingsTabViewModel(V2Settings, V2BridgeAdapter? bridge = null)` ctor extension pattern existed (iter-301)
- iter-310: existing iter-301 GroupBox shape reused for unit-icons GroupBox

**5-second `find` / `grep` at iter-top is now the ritual.**

### 3. Optional-default-null constructor extension (3 instances — codification trigger reached)

- iter-301: `SettingsTabViewModel(V2Settings settings, V2BridgeAdapter? bridge = null)`
- iter-308: `SpawningTabViewModel(V2BridgeAdapter bridge, UnitIconResolver? iconResolver = null)`
- iter-309: composition-root wires both at `MainViewModelV2.ctor` — the previously-optional param is now always passed

**Codified this iter** as `feedback_optional_default_null_constructor_extension.md`. Pattern shape: extend constructor with optional dependency defaulting to null + pin existing callers via default + add real wiring at composition root in a separate iter + pin both ends with tests. Total cost: 1-line signature change + 1-line composition-root edit + N pin tests. Total ripple: zero.

### 4. Status badge as inline operator documentation (3 instances — codification trigger reached)

- iter-301: `ModPickerStatus` ("(unknown)" / "Bridge not connected..." / "Found N mods" / "Bridge call failed: ..." / "Opened: ...")
- iter-309: `IconsRootStatus` (3 states from `MainViewModelV2.ResolveIconsRoot`)
- iter-310: `IconsRootStatus` (3 actionable hint strings with embedded CLI commands + env-var names + restart-required note)

**Codified this iter** as `feedback_status_badge_as_inline_docs.md`. Pattern shape: status string doubles as documentation — references exact next-step CLI commands, env-var names, constraints. Operator never has to leave the current tab to figure out what to do next. Cost: ~15 LoC of string literals per badge. Benefit: reduced support burden + faster operator onboarding.

---

## Section 8 — What's NOT done (honest defer to iter-312+)

- **Live VM rebuild on Settings.IconsRoot change** — currently the resolver is constructed once at MainViewModelV2 startup. Operators must restart editor to see icon changes. iter-312+ could add a "Reload icons" button or settings-change handler.
- **Asset Browser tab** — separate panel showing all extracted icons in a thumbnail grid. iter-313+ scope.
- **Hero portraits** — different .meg dir, different naming convention. Same `UnitIconResolver` pattern can address by extending the filename convention beyond `i_button_<name>.dds`. iter-313+.
- **Live SWFOC verify** — requires operator's game install. Honest defer to operator session.
- **Settings tab GroupBox pattern codification** — at 2 instances (iter-301 mod-picker + iter-310 unit-icons). One more recurrence triggers `feedback_settings_tab_groupbox_pattern.md`.
- **Duplicated walk discipline codification** — 1st instance at iter-310 mirroring iter-308. Two more recurrences trigger `feedback_duplicated_walk_discipline.md`.
- **Pre-existing CS8602 nullable warnings** — 5 unrelated test files (Iter161/166/209/214/217) still pending dedicated cleanup iter.

---

## Section 9 — Cumulative session tally (iter 159-310)

After this arc:
- 103 LIVE bridge wires (unchanged from iter-285; Thread D didn't add bridge wires)
- 12 dispatcher helpers (unchanged from iter-186)
- **318 verified ledger entries** (unchanged from iter-258)
- **111 native UX buttons** (was 110; +1 Browse... in iter-310)
- **65 Thread D pin tests** (NEW; iter-307 + 308 + 309 + 310)
- **End-to-end .meg-to-WPF-icon pipeline** (NEW; iter-304 → iter-310)
- **2 NEW codified memory rules** (iter-311 — this iter): `feedback_optional_default_null_constructor_extension.md` + `feedback_status_badge_as_inline_docs.md`
- **42nd consecutive NON-A1.x iter** per iter-269 lesson #2

---

## Section 10 — Next session pickup

**Highest-value continuations**:
1. **iter-312 Live VM rebuild** — closes the "restart editor" honest-defer from iter-310. Operator changes IconsRoot → resolver reconstructs → Spawning tab rows refresh in-place. Estimated: ~30-50 LoC + 2-3 pin tests.
2. **iter-312/313 Asset Browser tab** — separate panel; operator can browse all extracted icons at a glance. Estimated: ~150-250 LoC + XAML.
3. **Iter-312 Hero portraits** — extend `UnitIconResolver` with second filename convention. Estimated: ~30-50 LoC + tests.
4. **Audit B remaining wire** — `faction-roster-by-build-tab` (last of 6 from iter-294 Audit B). Estimated: ~80-120 LoC.

**Audit candidates** (any iter ≥ 350 in current calendar):
- Reverse-orphan snapshot audit (~22-iter cadence; last ran iter-272)
- README capstone update (~30-iter cadence; last ran iter-273)
- Phase2HookPending re-audit (~16-iter cadence; last ran iter-274)
- Lua Playground preset menu refresh (last ran iter-264; covers iter 257-260 wires; nothing new bridge-wise since iter-285)

---

## Verification checklist (this iter)

- [x] Operator changelog covers iter 294 + iter 296-310 walk-through-every-tab format
- [x] 2 codification iters reaching 3-instance threshold shipped (`feedback_optional_default_null_constructor_extension.md` + `feedback_status_badge_as_inline_docs.md`)
- [x] Pure docs iter — no code changes; all gates inherit GREEN
  - Editor build inherits 0/0 from iter-310
  - iter-307+308+309+310 still PASS 65/65 (no regression possible without code changes)
  - Bridge harness inherits 1100/0
  - Verifier ledger lint inherits 0/0 at 318 entries
- [x] Settings tab GroupBox pattern + duplicated walk discipline + status badge patterns documented as candidates for next-recurrence codification
- [ ] State docs synced
- [ ] Task #562 marked completed; iter-312 (live VM rebuild OR Asset Browser kickoff) queued
