# Profile Format

Each profile JSON maps to `TrainerProfile` in `SwfocTrainer.Core`.

## Required Top-Level Fields

- `id`
- `displayName`
- `exeTarget` (`Sweaw` or `Swfoc`)
- `signatureSets`
- `fallbackOffsets`
- `actions`
- `featureFlags`
- `catalogSources`
- `saveSchemaId`
- `helperModHooks`
- `metadata`

## Signature Fields

Each signature entry supports:

- `name`
- `pattern`
- `offset`
- `valueType`
- `addressMode` (optional, defaults to `HitPlusOffset`)

`addressMode` values:

- `HitPlusOffset`: final address is `patternHit + offset`.
- `ReadAbsolute32AtOffset`: read a 32-bit absolute address at `patternHit + offset` and use it as the final address.
- `ReadRipRelative32AtOffset`: decode a signed 32-bit RIP-relative displacement at `patternHit + offset` and compute the final address relative to the end of the instruction.

## Inheritance Rules

Use `inherits` for layered profiles.

- Child profile values overwrite parent values for:
  - `actions`
  - `featureFlags`
  - `fallbackOffsets`
  - `metadata` keys
- List fields are concatenated:
  - `signatureSets`
  - `catalogSources`
  - `helperModHooks`

## Action Payloads

`payloadSchema.required` lists required JSON keys.

Runtime memory actions currently accept:

- `symbol` + `intValue`
- `symbol` + `floatValue`
- `symbol` + `boolValue`

Credits actions also support:

- `set_credits`: `lockCredits` (optional, default `false`)

Backward compatibility:

- `forcePatchHook` is accepted as an alias for `lockCredits`.

Helper actions should include `helperHookId`.

## Metadata Contract (Standardized)

The `metadata` object is string-keyed and string-valued. Runtime and tooling consume specific keys.

### Required for Mod Profiles

- `requiredWorkshopIds`
  - CSV of required workshop IDs.
  - Example: `"1397421866,3447786229"`
- `requiredMarkerFile`
  - Relative marker file path inside each dependency root.
  - Example: `"Data/XML/Gameobjectfiles.xml"`
- `dependencySensitiveActions`
  - CSV of action IDs to soft-disable when dependency verification is unresolved.

### Optional

- `localPathHints`
  - CSV tokens used to infer profile from `MODPATH`.
- `localParentPathHints`
  - CSV child folder hints used to discover parent dependencies from local mod roots.
- `profileAliases`
  - CSV aliases for profile identification in launch-context heuristics and tooling.
- `criticalSymbols`
  - CSV symbol IDs that require strict write reliability behavior.
  - Critical symbol writes trigger one re-resolve retry before final failure.
- `symbolValidationRules`
  - JSON array (encoded as string) of per-symbol sanity rules.
  - Fields:
    - `Symbol` (required)
    - `Mode` (optional: `Galactic`, `Tactical`, etc.)
    - `IntMin`, `IntMax` (optional integer bounds)
    - `FloatMin`, `FloatMax` (optional floating bounds)
    - `Critical` (optional bool)
  - Example value:
    - `"[{\"Symbol\":\"credits\",\"IntMin\":0,\"IntMax\":2000000000,\"Critical\":true}]"`

### Legacy Compatibility

- `requiredWorkshopId` (singular) is still accepted by runtime/tools for backward compatibility.
- New/updated profiles should use `requiredWorkshopIds`.

## LaunchContext Semantics

`ProcessMetadata` may include `LaunchContext` resolved from process detection + profile metadata hints.

### `LaunchKind`

- `Unknown`
- `BaseGame`
- `Workshop`
- `LocalModPath`
- `Mixed` (both workshop IDs and MODPATH markers present)

### `ProfileRecommendation`

- `ProfileId`: recommended profile ID, nullable.
- `ReasonCode`: stable reason token.
- `Confidence`: `0.0` to `1.0`.

Current reason codes:

- `steammod_exact_roe`
- `steammod_exact_aotr`
- `modpath_hint_roe`
- `modpath_hint_aotr`
- `exe_target_sweaw`
- `foc_safe_starwarsg_fallback`
- `unknown`

## Tooling Contract

`tools/detect-launch-context.py` uses this profile metadata contract directly (no hardcoded mapping table) and emits:

- `schemaVersion`
- `generatedAtUtc`
- `input`
- `launchContext`
- `profileRecommendation`
- `dependencyHints`

## Spawn Preset Contract

Live Ops spawn presets are profile-scoped and optional.

- Path convention:
  - `profiles/default/presets/<profileId>/spawn_presets.json`
- JSON shape:
  - `schemaVersion` (string)
  - `presets` (array)
  - Each preset field:
    - `id` (string, unique per profile)
    - `name` (string)
    - `unitId` (string)
    - `faction` (string)
    - `entryMarker` (string)
    - `defaultQuantity` (int, optional, defaults to `1`)
    - `defaultDelayMs` (int, optional, defaults to `125`)
    - `description` (string, optional)

Behavior:

- If a preset file is missing, runtime tooling can generate fallback presets from catalog sources.
- Preset files are additive and do not change profile inheritance semantics.

## Action Reliability States

Live Ops surfaces action reliability as:

- `stable`
  - mode/dependency gates pass and symbol quality is healthy.
- `experimental`
  - action can run but uses degraded/fallback confidence paths.
- `unavailable`
  - strict mode gate fails, dependency blocks action, or required symbols are unresolved.

Diagnostics keys attached to action results/audit may include:

- `reliabilityState`
- `reliabilityReasonCode`
- `bundleGateResult`
- `transactionId` (for selected-unit transaction workflows)
