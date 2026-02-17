# Architecture

## Execution pipeline

1. Profile selected (`IProfileRepository.ResolveInheritedProfileAsync`).
2. Process located (`IProcessLocator`).
3. Dependency sanity checks (workshop IDs, marker files).
4. Signature resolution (`ISignatureResolver`) with fallback offsets.
5. Action validation (`ActionPayloadValidator`).
6. Execution routing (`TrainerOrchestrator.ExecuteAsync`):
   - **Freeze** actions → `IValueFreezeService` (orchestrator-local, avoids circular DI).
   - **Memory / CodePatch / Helper / Save** actions → `IRuntimeAdapter.ExecuteAsync`.
7. Readback verification for memory actions.
8. Audit logging (`IAuditLogger`) in `%LOCALAPPDATA%\SwfocTrainer\logs`.

## ExecutionKind dispatch

| Kind        | Handler               | Notes                                         |
|-------------|-----------------------|-----------------------------------------------|
| `Memory`    | `RuntimeAdapter`      | Direct read/write, credits has trampoline hook |
| `CodePatch` | `RuntimeAdapter`      | NOP/toggle patches with rollback               |
| `Helper`    | `RuntimeAdapter`      | Lua helper-mod script execution                |
| `Save`      | `RuntimeAdapter`      | Schema-driven save edits                       |
| `Freeze`    | `TrainerOrchestrator` | Delegates to `IValueFreezeService`             |

## Credits system fallback chain

1. **Trampoline hook** (ideal): AOB `F3 0F 2C 50 70 89 57` → code-cave injection at `cvttss2si` → captures float context pointer → supports lock mode.
2. **Direct int write** (fallback): Always writes to resolved `credits` symbol address regardless of hook status. Works even when AOB scan fails.
3. Diagnostics in result: `hookInstalled`, `hookError`, `hookTickObserved`, `forcedFloatBits`, context addresses.

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

## Data roots

- Local profile pack root in app: `profiles/default` (portable-friendly).
- Save default root: `C:\Users\Prekzursil\Saved Games\Petroglyph`.
- Workshop roots checked:
  - `D:\SteamLibrary\steamapps\workshop\content\32470`
  - `/mnt/d/SteamLibrary/steamapps/workshop/content/32470`
