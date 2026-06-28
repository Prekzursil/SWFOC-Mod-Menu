# iter-309 — V2Settings.IconsRoot + MainViewModelV2.ResolveIconsRoot wiring (Thread D arc post-finale)

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (Thread D arc, post-finale closeout)
**Predecessor:** iter-308 (Spawning tab unit-icon column + UnitIconResolver service)
**Successor (queued):** iter-310 (Settings tab UI field for IconsRoot + Browse button + live SWFOC verify)

## What changed (3 files modified + 2 test files new; ~210 LoC; **12/12 iter-309 + combined 53/53 PASS**)

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/Infrastructure/V2Settings.cs` (+~12 LoC):
  - NEW property `IconsRoot: string?` with `[JsonPropertyName("iconsRoot")]` attribute (matches sibling lower-camel-case convention).
  - Defaults to null; null = no icons, graceful.

- **MODIFY** `SWFOC editor/src/SwfocTrainer.App/V2/ViewModels/MainViewModelV2.cs` (+~25 LoC):
  - Added `using SwfocTrainer.Core.Assets;`
  - Replaced `Spawning = new SpawningTabViewModel(bridge);` with `Spawning = new SpawningTabViewModel(bridge, new UnitIconResolver(ResolveIconsRoot(settings)));`
  - NEW `internal static string? ResolveIconsRoot(V2Settings settings)` helper extracting the resolution precedence into a unit-testable static method.

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/App/V2/Infrastructure/Iter309IconsRootSettingsTests.cs` (~85 LoC, **5 facts** PASS):
  - `IconsRoot_DefaultsToNull`
  - `IconsRoot_RoundTripsThroughJson`
  - `IconsRoot_NullValue_RoundTripsAsNull`
  - `JsonPropertyName_IsLowerCamelCaseIconsRoot`
  - `OldSettingsFile_WithoutIconsRoot_DeserializesGracefully` (backward-compat for pre-iter-309 v2_settings.json)

- **NEW** `SWFOC editor/tests/SwfocTrainer.Tests/App/V2/ViewModels/Iter309IconsRootResolutionTests.cs` (~110 LoC, **7 facts** PASS):
  - `Settings_HasExplicitIconsRoot_TakesPrecedence_OverEnvVar`
  - `Settings_NullIconsRoot_FallsThroughToEnvVar`
  - `Settings_EmptyIconsRoot_FallsThroughToEnvVar`
  - `Settings_WhitespaceIconsRoot_FallsThroughToEnvVar`
  - `Both_NullSettings_AndNullEnv_ReturnsNull`
  - `Both_EmptySettings_AndEmptyEnv_ReturnsNull`
  - `NullSettings_Throws` (programmer-bug boundary check)
  - Pinned to NEW `[Collection("IconsRootEnv")]` (orthogonal to iter-307+308's `[Collection("ThumbnailCacheEnv")]` — different env var, different collection).

## Resolution precedence (canonical)

```csharp
internal static string? ResolveIconsRoot(V2Settings settings)
{
    ArgumentNullException.ThrowIfNull(settings);
    if (!string.IsNullOrWhiteSpace(settings.IconsRoot))
        return settings.IconsRoot;
    var fromEnv = Environment.GetEnvironmentVariable("SWFOC_EXTRACTED_DDS_ROOT");
    return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
}
```

1. **`settings.IconsRoot`** (operator-explicit, persisted to `%APPDATA%\SwfocTrainer\v2_settings.json`)
2. **`SWFOC_EXTRACTED_DDS_ROOT` env var** (operator-explicit, session-only)
3. **null** = no icons (UnitIconResolver gracefully returns null IconPath, hides Image control)

Whitespace-only values at any level normalize to null.

## End-to-end operator workflow (Thread D arc COMPLETE + WIRED)

After iter-309, an operator's full setup is:

```bash
# 1. One-time per game install: extract MasterTextures.meg
python tools/asset_extractor/meg_parser.py "C:\Games\SWFOC\Data\MasterTextures.meg" --extract-all C:\Games\SWFOC\extracted

# 2. One-time per game install: cache thumbnails (loop over Units dir)
for dds in C:\Games\SWFOC\extracted\Data\Art\Textures\Units\*.dds; do
    python tools/asset_extractor/thumbnail_cache.py "$dds" --size 32
done

# 3a. Operator-explicit Option A — edit v2_settings.json (persisted across sessions)
# Add to %APPDATA%\SwfocTrainer\v2_settings.json:
#   "iconsRoot": "C:\\Games\\SWFOC\\extracted"

# 3b. Operator-explicit Option B — env var (session-only, quick test path)
$env:SWFOC_EXTRACTED_DDS_ROOT = "C:\Games\SWFOC\extracted"

# 4. Launch editor → Spawning tab → unit types render with their in-game icons
SwfocTrainer.App.exe
```

iter-310 will add a Settings tab UI field for IconsRoot so operators can configure it without editing JSON manually.

## Verification gates ALL GREEN

```
[run_editor_tests_v2] dotnet test --filter "FullyQualifiedName~Iter309"
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 37 ms

[run_editor_tests_v2] dotnet test --filter "...~Iter307|...~Iter308|...~Iter309"
Passed!  - Failed:     0, Passed:    53, Skipped:     0, Total:    53, Duration: 161 ms
```

- Editor build: GREEN (Core + App + Tests + dependents compiled cleanly)
- iter-309 pin tests: **Passed 12/12 in 37 ms** ✓
- iter-307 + iter-308 + iter-309 combined: **Passed 53/53 in 161 ms** ✓ (no regression in iter-307's 21 or iter-308's 20)
- Bridge harness inherits 1100/0 (no bridge changes)
- Verifier ledger lint inherits 0/0 at 318 entries (no ledger changes)
- Phase2HookPending count: 24 → 24 unchanged

## Iter-308 [Collection] orthogonality pattern — 1st validation

iter-308 introduced `[Collection("ThumbnailCacheEnv")]` to serialize tests touching `SWFOC_THUMB_CACHE` env var. iter-309 introduces `[Collection("IconsRootEnv")]` for tests touching `SWFOC_EXTRACTED_DDS_ROOT`. **Orthogonal env vars use orthogonal collections** — they can run in parallel safely while each collection serializes its own env-var users.

This is a **1st validation of the iter-308 codification candidate** `feedback_xunit_env_var_race_pattern.md`. The rule shape: when test classes touch a process-wide env var, pin them to a `[Collection]` named after the env var (or its semantic group). Different env vars → different collections.

## Iter-301 optional-default-null pattern — 3rd application (codification trigger)

- iter-301: `SettingsTabViewModel(V2Settings settings, V2BridgeAdapter? bridge = null)`
- iter-308: `SpawningTabViewModel(V2BridgeAdapter bridge, UnitIconResolver? iconResolver = null)`
- iter-309: composition-root wiring at `MainViewModelV2.ctor` calls `new SpawningTabViewModel(bridge, new UnitIconResolver(ResolveIconsRoot(settings)))` — **the previously-optional param is now always passed at the real composition root.**

iter-309 demonstrates the **complete pattern lifecycle**: introduce optional dep at iter N (no break), wire at composition root at iter M (operator value lands), pin tests verify at both ends.

**Codification candidate `feedback_optional_default_null_constructor_extension.md` reaches 3-instance threshold.** Rule shape: extend constructor with optional dependency defaulting to null + pin existing callers via default + add real wiring at composition root in a separate iter + pin both with tests. Total cost: 1-line signature change + 1-line composition-root edit + N pin tests. Total ripple: zero.

## Pattern lessons

### *Static testable helper extraction (NEW pattern observation)*

The original 4-line resolution logic was inline in the `MainViewModelV2.ctor`. Testing it would require constructing the FULL VM with all 20+ service dependencies — prohibitively expensive for 12 small precedence checks.

Refactor to `internal static string? ResolveIconsRoot(V2Settings settings)` — same logic, no behavior change, but now testable in isolation. Cost: 12 LoC. Benefit: 12 pin tests with no service-graph stubs.

This is the **TestPyramid-respecting refactor**: prefer cheap unit tests over expensive integration tests when the logic is straightforward enough that the integration test wouldn't add information.

### *Backward-compat tested explicitly*

`OldSettingsFile_WithoutIconsRoot_DeserializesGracefully` constructs a literal pre-iter-309 JSON string and round-trips it. Zero JSON deserialization errors + IconsRoot defaults to null + other properties preserved. **Operators upgrading from any prior iter see no breakage** — pinned in test, not just claimed in commit message.

### *Whitespace-only values normalize to null at every layer*

`!string.IsNullOrWhiteSpace(...)` instead of `!= null`. Operator typo safety: if someone writes `"iconsRoot": "   "` in JSON, the resolver treats it as unset. Same for env vars. Caught all 4 whitespace cases in tests (`null` / `""` / `"   "` / both-empty).

## What's intentionally NOT done in iter-309 (deferred to iter-310)

- **Settings tab UI field for IconsRoot** — needs a TextBox + Browse button in MainWindowV2.xaml's Settings tab. iter-310 polish; operator can use the env var or edit v2_settings.json directly meanwhile.
- **Live SWFOC verify against operator's real MasterTextures.meg** — requires operator's game install. iter-309 ships the wiring; iter-310 (or operator session) does the live verify checkpoint.
- **Settings.IconsRoot change → live VM rebuild** — currently the resolver is constructed once at MainViewModelV2 startup. Changing IconsRoot would require an editor restart. iter-310 could add a "Reload icons" button OR settings change handler.
- **Asset Browser tab** — separate panel showing all extracted icons in a thumbnail grid. iter-311+.

## Verification checklist

- [x] V2Settings.IconsRoot property added with proper JSON attribute
- [x] MainViewModelV2 wires UnitIconResolver via ResolveIconsRoot helper
- [x] ResolveIconsRoot precedence implemented + unit-tested (settings → env var → null)
- [x] Backward-compat verified via literal-JSON deserialization test
- [x] 12 iter-309 pin tests authored across 2 test files
- [x] **iter-309 pin tests Passed 12/12 in 37 ms** ✓
- [x] **Combined iter-307+308+309 Passed 53/53 in 161 ms** ✓ (no regression)
- [x] Editor build GREEN
- [x] Bridge harness inherits 1100/0
- [x] Verifier ledger lint inherits 0/0 at 318 entries
- [x] iter-308 `[Collection]` env-var-isolation pattern validated (1st application post-codification-candidate)
- [x] iter-301 optional-default-null pattern reaches 3rd application (codification trigger)
- [ ] State docs synced
- [ ] Task #560 marked completed; iter-310 (Settings UI field + live verify) queued
