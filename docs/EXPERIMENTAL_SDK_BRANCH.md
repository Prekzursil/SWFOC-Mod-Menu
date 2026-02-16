# Experimental SDK Branch Policy

This document applies to branch `rd/hardcore-sdk-v1` only.

## Scope

- This branch hosts branch-only R&D for a custom SDK-like runtime path.
- Mainline behavior must remain unchanged when the SDK gate is disabled.
- The default runtime path remains the existing memory/helper/codepatch pipeline.

## Feature Gate

- Environment variable: `SWFOC_EXPERIMENTAL_SDK`
- Enabled values: `1`, `true`, `on`, `yes`, `enabled`
- When not enabled, SDK execution returns `sdk_feature_gate_disabled` and does not run.

## Safety Contract

- No blind fixed-address execution.
- SDK capability must resolve from profile operation map + symbol health.
- Mutating SDK operations are blocked when capability is `Degraded` or `Unavailable`.
- Read-only operations can run in degraded mode.
- When blocked, debug-console fallback is prepare-only and auditable.

## Runtime Diagnostics

Attach metadata includes:

- `runtimeModeHint`
- `runtimeModeEffective`
- `runtimeModeReasonCode`
- `processSelectionReason`
- `sdkCapabilitiesAvailable`
- `sdkCapabilitiesDegraded`
- `sdkCapabilitiesUnavailable`
- `sdkCapabilitiesSummary`

Action diagnostics include:

- `sdkOperationId`
- `sdkCapabilityState`
- `sdkCapabilityReasonCode`
- `failureReasonCode`
- `fallbackMode`
- optional `debugConsoleCommand`

## Current v1 Command Set

- `list_selected`
- `list_nearby`
- `spawn`
- `kill`
- `set_owner`
- `teleport`
- `set_planet_owner`
- `set_hp`
- `set_shield`
- `set_cooldown`

## Fallback Track

- Fallback adapter prepares debug-console command payloads only.
- v1 mapping currently supports `SwitchControl` templates for owner operations.
- Fallback is never auto-executed in this branch.
