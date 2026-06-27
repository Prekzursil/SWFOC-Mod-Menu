# SWFOC-Mod-Menu

Profile-driven trainer/editor for **Star Wars: Empire at War / Forces of Corruption** with support for base game and major FoC mod stacks.

## Supported Profiles

- `base_sweaw` (base Empire at War)
- `base_swfoc` (base Forces of Corruption)
- `aotr_1397421866_swfoc` (Awakening of the Rebellion)
- `roe_3447786229_swfoc` (ROE submod)
- `universal_auto` (user-facing auto resolver that maps to concrete internal profile variants)

## What This Repository Contains

This repository is **code-first** and intentionally excludes full local mod mirrors and large game assets.

Included:

- .NET solution and runtime/editor code
- profile packs, schemas, catalogs, and helper hooks
- tests and CI workflows
- diagnostics and launch-context tooling

Excluded:

- full Workshop/local mod content trees
- large media/model assets
- generated build artifacts

## Core Capabilities

- Profile inheritance + metadata-driven routing
- Attach/process selection for SWFOC launch realities (`STEAMMOD`, `MODPATH`, `StarWarsG`)
- Signature-first symbol resolution with validated fallback offsets
- Runtime memory actions, code patches, helper actions, and freeze orchestration
- Live Ops workflows:
  - selected-unit transaction lab (apply/revert/baseline restore)
  - action reliability surface (`stable`/`experimental`/`unavailable`)
  - spawn preset studio with batch operations
- **Operator preset hierarchy** (iter 76-80, 2026-04-28):
  - **Per-tab scalar presets** — single-click value sets for the most common configurations:
    - **Combat** — Easy / Normal / Hard / Hardcore (damage + fire-rate multipliers in one click)
    - **Speed** — global game speed (Pause / 0.5× / 1× / 2× / 4×) + per-faction (Snail / Slow / Normal / Fast) + per-unit (Slow / Normal / Fast / Sprint)
    - **Hero Lab** — respawn time (Quick 2.5s / Normal 5s / Slow 15s / Glacial 60s)
  - **Cross-tab composite presets** (Quick Actions tab) — single-click bundles tying multiple tabs together:
    - **Tournament setup** — Hard combat scalars + slow respawn + real-time
    - **Sandbox setup** — god mode + heal + uncap + drain + 2× speed + quick respawn
    - **Streaming setup** — hide HUD + freeze AI + slow-mo + slow respawn
    - Plus iter-64 originals: Battle setup (god + heal + uncap + drain) and Filming setup (hide UI + freeze AI + god + permadeath)
  - All presets surface their `LIVE` / `MIXED` / `PHASE 2 PENDING` capability badges so the operator never confuses "bridge call succeeded" with "engine state changed"
- Save decode/edit/validate/write workflow
- Save Lab patch-pack workflow:
  - schema-path typed patch export/import
  - compatibility preview (profile/schema/hash)
  - strict/non-strict apply toggle (strict default ON)
  - atomic apply with backup + receipt
  - rollback from latest backup
- Mod Compatibility Studio (M3):
  - draft profile onboarding from launch samples (`STEAMMOD`/`MODPATH`)
  - calibration artifact export
  - compatibility report with promotion gate verdict
- Ops Hardening (M4):
  - transactional profile update install + rollback
  - support bundle export (logs/runtime/repro bundles/telemetry)
  - telemetry snapshot export for drift diagnostics
- Dependency-aware action gating for mod/submod contexts
- Launch-context detector script for reproducible diagnostics
- **v5 Mod Editor Expansion** (21 features across 6 waves):
  - Unit Roster Browser — searchable unit database from mod XML
  - Enhanced Spawner — cross-faction, tactical/galactic/reinforcement modes
  - Faction Dashboard — god-view of all factions (credits, units, planets, tech)
  - Ownership Transfer — change unit/planet ownership between factions
  - Planet Manager — view/modify planet owner, station level, buildings, corruption
  - Fleet/Garrison Manager — fleet composition and movement
  - Faction Seat Switching — play as any faction mid-game
  - AI Brain Controller — suspend/resume AI, set difficulty per faction
  - Cooldown Manager — reset ability cooldowns for selected units
  - Camera Director — cinematic camera control with keyframe recording
  - Story Event Console — fire story events, custom event triggers
  - Damage Log & Battle Statistics — real-time combat analytics with CSV export
  - Mod Conflict Detector — identify overlapping definitions across mods
  - Diplomacy Editor — make/break alliances (mod-aware)
  - Corruption Controller — FoC corruption types with proper engine registration

## Prerequisites

- Windows 10/11
- .NET SDK 8.x (repo pinned through `global.json`)
- Python 3.10+ (for tooling scripts)

## Build and Test

```powershell

# from repo root

dotnet restore SwfocTrainer.sln
dotnet build SwfocTrainer.sln -c Release
```

Quick verification using Makefile:

```bash
make verify    # Run deterministic test suite
make build     # Build the solution
make clean     # Clean build artifacts
```

Quick Windows launchers in repo root (double-click):

- `launch-app-release.cmd` builds Release if needed, then starts `SwfocTrainer.App.exe`.
- `launch-app-debug.cmd` builds Debug if needed, then starts `SwfocTrainer.App.exe`.
- `run-deterministic-tests.cmd` runs the non-live deterministic test suite.
- `run-live-tests.cmd` runs live profile tests (expected to skip when no live SWFOC process is available).

Deterministic test suite (direct command):

```powershell
dotnet test tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj `
  -c Release --no-build `
  --filter "FullyQualifiedName!~SwfocTrainer.Tests.Profiles.Live&FullyQualifiedName!~RuntimeAttachSmokeTests"
```

For troubleshooting test failures, build issues, or environment setup problems, see `docs/TROUBLESHOOTING.md`.

## CI

GitHub Actions workflows:

- `.github/workflows/ci.yml`: restore, build, deterministic tests, launch-context fixture smoke checks
- `.github/workflows/release-portable.yml`: portable package + checksum + GitHub Release publish on tags

## Reviewer Automation

Reviewer assignment is handled by a REST-only workflow to avoid GraphQL fragility:

- Workflow: `.github/workflows/reviewer-automation.yml`
- Roster contract: `config/reviewer-roster.json`
- Script: `tools/request-pr-reviewers.ps1`

Behavior:

- Requests non-author reviewers from the configured roster.
- If no eligible reviewer exists, applies fallback label/comment (`needs-reviewer`) and keeps workflow green.

Runbook: `docs/REVIEWER_AUTOMATION.md`

## Launch Context Tooling

```powershell
python tools/detect-launch-context.py --from-process-json tools/fixtures/launch_context_cases.json --profile-root profiles/default --pretty
```

This emits normalized launch context + profile recommendation JSON for runtime parity validation.

## Repro Bundle Tooling

```powershell
pwsh ./tools/run-live-validation.ps1 -Configuration Release -NoBuild -Scope FULL -EmitReproBundle $true
pwsh ./tools/validate-repro-bundle.ps1 -BundlePath TestResults/runs/<runId>/repro-bundle.json -SchemaPath tools/schemas/repro-bundle.schema.json -Strict
```

Runtime/mod triage should attach `repro-bundle.json` + `repro-bundle.md` from `TestResults/runs/<runId>/`.

## R&D Research Tooling (Deep RE Track)

```powershell
pwsh ./tools/research/run-capability-intel.ps1 -ModulePath "C:\\Games\\Star Wars Empire at War\\corruption\\swfoc.exe" -ProfilePath profiles/default/profiles/base_swfoc.json
pwsh ./tools/research/capture-binary-fingerprint.ps1 -ModulePath "C:\\Games\\Star Wars Empire at War\\corruption\\swfoc.exe"
pwsh ./tools/research/generate-signature-candidates.ps1 -FingerprintPath TestResults/research/<runId>/fingerprint.json
pwsh ./tools/research/normalize-signature-pack.ps1 -InputPath TestResults/research/<runId>/signature-pack.json
pwsh ./tools/validate-binary-fingerprint.ps1 -FingerprintPath tools/fixtures/binary_fingerprint_sample.json -SchemaPath tools/schemas/binary-fingerprint.schema.json -Strict
pwsh ./tools/validate-signature-pack.ps1 -SignaturePackPath tools/fixtures/signature_pack_sample.json -SchemaPath tools/schemas/signature-pack.schema.json -Strict
```

Contracts:

- `docs/RESEARCH_GAME_WORKFLOW.md`
- `tools/research/build-fingerprint.md`
- `tools/schemas/binary-fingerprint.schema.json`
- `tools/schemas/signature-pack.schema.json`

## Save Patch-Pack Tooling

```powershell
pwsh ./tools/validate-save-patch-pack.ps1 -PatchPackPath tools/fixtures/save_patch_pack_sample.json -SchemaPath tools/schemas/save-patch-pack.schema.json -Strict
pwsh ./tools/export-save-patch-pack.ps1 -OriginalSavePath <original.sav> -EditedSavePath <edited.sav> -ProfileId base_swfoc -SchemaId base_swfoc_steam_v1 -OutputPath TestResults/patches/example.patch.json -BuildIfNeeded
pwsh ./tools/apply-save-patch-pack.ps1 -TargetSavePath <target.sav> -PatchPackPath TestResults/patches/example.patch.json -TargetProfileId base_swfoc -Strict $true -BuildIfNeeded
```

## Calibration Workflow (Realtime Reliability)

1. Run live attach against target profile.
2. Capture diagnostics (`reasonCode`, symbol source, dependency state).
3. Run launch-context tool against captured process command line/path.
4. If symbol drift is observed, open a **Calibration** issue using the template.
5. Update signature/metadata with evidence and keep fallback-only changes explicitly marked.

## Packaging

```powershell
pwsh ./tools/package-portable.ps1 -Configuration Release
```

Output: `artifacts/SwfocTrainer-portable.zip`

GitHub Releases are the primary distribution channel. See `docs/RELEASE_RUNBOOK.md` for tag policy, checksum verification, and rollback steps.

## Roadmap and Execution

- Execution board: `TODO.md`
- Roadmap workflow: `docs/ROADMAP_WORKFLOW.md`
- Save Lab operator workflow: `docs/SAVE_LAB_RUNBOOK.md`
- Mod onboarding workflow: `docs/MOD_ONBOARDING_RUNBOOK.md`
- Release operations: `docs/RELEASE_RUNBOOK.md`
- KPI baseline and governance: `docs/KPI_BASELINE.md`
- Fleet baseline lite (for repo rollout): `docs/FLEET_BASELINE_LITE.md`
- Plan archive: `(new)codex(plans)/`
- Profile format contract: `docs/PROFILE_FORMAT.md`

## Master Loop Capstone — iter 100-321 (2026-04-23 → 2026-05-07)

**5th capstone update; mirrors iter-222 / iter-254 / iter-265 / iter-273 cadence at canonical ~30-iter interval.** Covers iter 273-321 (49 iters) end-to-end.

### Headline numbers

| Metric | Value |
|--------|-------|
| **LIVE bridge wires** | 142 (iter-186 capped the multi-arg expansion at the 12-helper dispatcher matrix) |
| **Native UX buttons across 22+ tabs** | 100+ (closed at iter-216 milestone, expanded through iter-321) |
| **Icon-consumer tabs** | **5** (Spawning iter-308 + Galactic iter-317 + HeroLab iter-318 + PlayerState iter-319 + Asset Browser iter-321) |
| **Codified memory rules** | **8** (iter-256 AOB drift / iter-283 bidirectional drift / iter-293 empirical-first / iter-293 iterative-deferral / iter-302 engine-already / iter-311 optional-default-null / iter-311 status-badge-as-docs / iter-316 extract-on-second-use) |
| **Operator changelog supplements** | **10** (iter-220/iter-235/iter-241/iter-247/iter-253/iter-262/iter-280/iter-293/iter-311/iter-320) |
| **Multi-iter arcs completed** | 8 (Camera + Cinematic + TaskForce + SetFireRate + FreezeCredits + SetCameraPos + SetUnitField + Thread B Overlay + Thread C Savegame + Thread D Asset Extraction + Thread D UI Integration) |
| **Phase2HookPending count** | 24 (drifted down from 26 → 25 → 24 across iter-237 + iter-296 catalog flips) |
| **Reverse-orphan audits** | 3 clean passes (iter-255 / iter-263 / iter-272) |
| **Editor publish size** | ~157 MB (republished after every substantive iter) |
| **Bridge harness pass rate** | 1100/0 (unchanged across the entire window) |
| **Verifier ledger lint** | 0/0 at 318 entries |

### Major arc completions in this window

#### Thread B — Overlay Phase 2-full (iter 275-285)
- Vendored 12 ImGui v1.91.5 files into `swfoc_overlay/` + DX9 backend
- 4-row HUD strip with ProgressBar + faction tinting + cinematic fade
- Tier 2 multipliers (damage + firerate) + Tier 3 kill/death tally + units-alive + scenario-event ring + mission timer
- 3 NEW bridge wires (kills/deaths/units-alive) + GetFireRateMultiplierGlobal getter pair completion
- Ships as production-ready overlay DLL (~1.04 MB)

#### Thread C — Savegame Editor (iter 286-293)
- RGMH chunk format empirically REd via Python parser → C# port → fixer subcommands → JSON schema → operator PowerShell wrapper
- Mod-fingerprint mechanism PROVEN: chunk 0x3EA ObjectType references (NOT bytes 17-20 hash hypothesis as initially suspected)
- 5 CLI tools: parser.py + fixer.py + objtype_lister.py + validate_mod.py + Inspect-Savegame.ps1
- 2 codified memory rules: `feedback_empirical_first_for_format_re.md` + `feedback_iterative_deferral_keeps_velocity.md`

#### Thread D — Asset Extraction (iter 304-310)
- Python `meg_parser.py` (V1 .meg archive parser, ~200 LoC) + `dds_decoder.py` (Pillow-first DXT decoder, ~190 LoC) + `thumbnail_cache.py` (content-keyed SHA256 cache, ~165 LoC)
- C# read-side mirror at `SwfocTrainer.Core.Assets.ThumbnailCache` + `UnitIconResolver` with `Resolve` + `ResolvePortrait` + `ResolveFactionEmblem` + `ResolvePlanetIcon`
- Settings tab UI field for IconsRoot + Browse button + status badge
- Live hot-swap on Settings.IconsRoot change (no editor restart)
- 2 codified memory rules: `feedback_engine_already_does_this.md` (iter-302) + `feedback_optional_default_null_constructor_extension.md` + `feedback_status_badge_as_inline_docs.md` (iter-311)

#### Thread D — UI Integration (iter 313-321)
**ALL 5 tabs operator-visible end-to-end:**
| Tab | Iter | Resolver | Default size | UI shape |
|-----|------|----------|--------------|----------|
| Spawning (units) | 308 | `Resolve` | 32px | ListBox DataTemplate |
| Galactic (planets) | 317 | `ResolvePlanetIcon` | 32px in 40px row | DataGrid TemplateColumn |
| Hero Lab (portraits) | 318 | `ResolvePortrait` | 64px in 72px row | DataGrid TemplateColumn |
| Player State (emblems) | 319 | `ResolveFactionEmblem` | 24px | ComboBox ItemTemplate |
| Asset Browser (cross-class) | 321 | All 4 via `ResolveIconForCategory` switch | 32px | DataGrid + filter (last iter-313 honest defer) |

5 inline catalog drift catches (4 iter-296 in iter-317 + 1 iter-295 in iter-318) reinforced `feedback_allactions_count_pin_drift.md` (iter-195/iter-208 codified).

### Pattern lessons codified across the window

1. **`feedback_aob_drift_across_binary_versions.md`** (iter-256) — community CE table AOBs lose accuracy across binary versions
2. **`feedback_infra_claim_drift_bidirectional.md`** (iter-283) — bidirectional 5-sec grep before writing addition code
3. **`feedback_empirical_first_for_format_re.md`** (iter-293) — ship Python parser ASAP when RE'ing binary formats
4. **`feedback_iterative_deferral_keeps_velocity.md`** (iter-293) — prototype-FIRST + defer port for 6× cycle-time advantage
5. **`feedback_engine_already_does_this.md`** (iter-302) — engine Lua API > filesystem > RVA pin decision tree
6. **`feedback_optional_default_null_constructor_extension.md`** (iter-311) — extend ctors with `Type? newDep = null`
7. **`feedback_status_badge_as_inline_docs.md`** (iter-311) — status string doubles as inline operator documentation
8. **`feedback_extract_on_second_use.md`** (iter-316) — write logic INLINE first; extract helper at SECOND consumer

The "delay commitment" trio (iter-302 + iter-311 + iter-316) is now load-bearing across every UI integration iter.

### Key tools shipped this window

- `tools/asset_extractor/meg_parser.py` — V1 .meg archive parser
- `tools/asset_extractor/dds_decoder.py` — DDS texture decoder via Pillow
- `tools/asset_extractor/thumbnail_cache.py` — content-keyed SHA256 thumbnail cache
- `tools/savegame_parser/parser.py` + `fixer.py` + `stub_xml_generator.py` + `Inspect-Savegame.ps1` — savegame editor toolchain
- `tools/savegame_parser/integrity.py` — SHA256 pre/post integrity guards (iter-298)

### Operator workflow (full asset pipeline, iter-321 end-to-end)

1. Extract MasterTextures.meg via Python CLI: `python tools/asset_extractor/meg_parser.py extract MasterTextures.meg --out C:/swfoc_extracted_dds/`
2. Warm thumbnail cache: `python tools/asset_extractor/thumbnail_cache.py warm C:/swfoc_extracted_dds/`
3. Editor → Settings tab → Browse → pick `C:/swfoc_extracted_dds/`
4. **All 5 icon-consumer tabs immediately render icons** (no restart). Operator uses Asset Browser to discover what's available across all 4 asset classes in one place.

### Honest defer to iter-322+

| Item | Why deferred | Recommended iter |
|------|-------------|------------------|
| A1.x SetFireRate / per-hero respawn / SetGameSpeed | Confirmed defer per ledger; needs WeaponClass RTTI dissection or per-tick MinHook detour | TBD |
| Audit B last wire (`faction-roster-by-build-tab`) | iter-299 honest defer; needs additional bridge wire | iter-322+ |
| Live SWFOC verify against operator's real MasterTextures.meg | Requires running the actual game | iter-323+ |
| Weapon/ability icon classes | 2 more asset classes; same pattern as iter-308/313/314/315 but different DDS prefixes | iter-322+ |

### Next-session pickup

iter-322 will pick from:
1. Phase2HookPending re-audit pass (~16-iter cadence due since iter-274)
2. Weapon/ability icon classes (extends iter-313 LocateByConvention to 6th + 7th plugin)
3. Live SWFOC verify against operator's real MasterTextures.meg

## Security and Reporting

Please use `SECURITY.md` for vulnerability disclosure workflow.
