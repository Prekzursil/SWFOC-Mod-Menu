# Architecture

## Execution pipeline

1. Profile selected (`IProfileRepository.ResolveInheritedProfileAsync`).
2. Process located (`IProcessLocator`).
3. Dependency sanity checks (workshop IDs, marker files).
4. Signature resolution (`ISignatureResolver`) with fallback offsets.
5. Action validation (`ActionPayloadValidator`).
6. Execution routing (`TrainerOrchestrator.ExecuteAsync`):
   - **Freeze** actions → `IValueFreezeService` (orchestrator-local, avoids circular DI).
   - **Memory / CodePatch / Helper / Save / Sdk** actions → `IRuntimeAdapter.ExecuteAsync`.
7. Readback verification for memory actions.
8. Audit logging (`IAuditLogger`) in `%LOCALAPPDATA%\SwfocTrainer\logs`.

## Tiered backend routing (vNext)

Mutating actions now use a fail-closed backend decision tree before execution:

1. Probe extender capability (`IExecutionBackend.ProbeCapabilitiesAsync`).
2. Resolve route (`IBackendRouter.Resolve`) using:
   - profile `backendPreference`
   - profile `requiredCapabilities`
   - current mode/context and process host metadata
3. Route by priority:
   - Layer A: `Extender` backend (named-pipe bridge to native host)
   - Layer B: `Helper` backend (Lua helper scripts)
   - Layer C: `Memory` backend (legacy symbol/code-patch path)
4. If capability is uncertain for a mutating action and profile preference is hard-extender:
   - block execution with explicit `SAFETY_FAIL_CLOSED` reason code.

Route diagnostics are emitted on every action result:

- `backendRoute`
- `routeReasonCode`
- `capabilityProbeReasonCode`
- `capabilityCount`

## ExecutionKind dispatch

| Kind        | Handler               | Notes                                         |
|-------------|-----------------------|-----------------------------------------------|
| `Memory`    | `RuntimeAdapter`      | Direct read/write, credits has trampoline hook |
| `CodePatch` | `RuntimeAdapter`      | NOP/toggle patches with rollback               |
| `Helper`    | `RuntimeAdapter`      | Lua helper-mod script execution                |
| `Save`      | `RuntimeAdapter`      | Schema-driven save edits                       |
| `Sdk`       | `RuntimeAdapter`      | Extender-routed command path (`Extender` layer) |
| `Freeze`    | `TrainerOrchestrator` | Delegates to `IValueFreezeService`             |

## Credits extender path

`set_credits` for FoC profiles is routed through `ExecutionKind.Sdk` and must resolve to the `Extender` backend:

1. Probe extender bridge capabilities (`probe_capabilities`).
2. If `set_credits` is unavailable/unknown and mutation is requested, route is fail-closed (`SAFETY_MUTATION_BLOCKED`).
3. If available, execute `set_credits` over named pipe with one-shot or lock semantics.
4. Result diagnostics include stable route and hook tags (`backendRoute`, `routeReasonCode`, `hookState`).

## Save Lab patch-pack pipeline (M2)

1. Load target save through `ISaveCodec`.
2. Export typed field diff using `ISavePatchPackService.ExportAsync`.
3. Validate compatibility + preview with:
   - `ISavePatchPackService.ValidateCompatibilityAsync`
   - `ISavePatchPackService.PreviewApplyAsync`
4. Apply atomically via `ISavePatchApplyService.ApplyAsync`:
   - in-memory operation apply
   - schema validation before write
   - backup file `<target>.bak.<runId>.sav`
   - receipt file `<target>.apply-receipt.<runId>.json`
5. Roll back using `ISavePatchApplyService.RestoreLastBackupAsync`.

## Projects

- `SwfocTrainer.Core`: contracts, records, orchestrator, audit logger.
- `SwfocTrainer.Runtime`: process + memory + signature scan.
- `SwfocTrainer.Profiles`: manifest/profile loading, inheritance, updates.
- `SwfocTrainer.Catalog`: prebuilt catalog loading and XML fallback parser.
- `SwfocTrainer.Helper`: helper-mod deployment and hash verification.
- `SwfocTrainer.Saves`: schema-driven save parse/edit/validate/write + patch-pack export/apply/rollback.
- `SwfocTrainer.App`: WPF shell and user workflows.
- `native/SwfocExtender.*`: native extender skeleton (`Core`, `Bridge`, `Overlay`, `Plugins`) for in-process vNext capabilities.

## Data roots

- Local profile pack root in app: `profiles/default` (portable-friendly).
- Save default root: `C:\Users\Prekzursil\Saved Games\Petroglyph`.
- Workshop roots checked:
  - `D:\SteamLibrary\steamapps\workshop\content\32470`
  - `/mnt/d/SteamLibrary/steamapps/workshop/content/32470`
