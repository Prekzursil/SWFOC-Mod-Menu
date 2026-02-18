# Save Lab Runbook

## Goal

Run the full M2 Save Lab workflow end-to-end:

1. load and edit a save
2. export a patch pack
3. import + preview compatibility
4. apply atomically with backup/receipt
5. restore backup if needed.

## Prerequisites

- Windows PowerShell 7 (`pwsh`) for CLI wrappers.
- .NET 8 SDK installed and available in PATH.

## In-App Flow (Save Editor tab)

1. Select profile (`base_sweaw`, `base_swfoc`, `aotr_1397421866_swfoc`, `roe_3447786229_swfoc`).
2. Click `Browse` and choose `.sav` target.
3. Click `Load`.
4. Edit one or more schema fields and verify `Preview Diff` output.
5. Click `Export Patch Pack` and save `*.json` artifact.
6. Click `Load Patch Pack` for the exported file.
7. Click `Preview Patch Apply` and confirm:
   - compatibility grid has no `error`
   - operation grid shows expected field mutations.
8. Choose `Strict (require exact source hash)`:
   - ON (default): source hash mismatch blocks apply.
   - OFF: source hash mismatch remains a warning.
9. Click `Apply Patch Pack`.
10. Capture backup + receipt paths shown in compatibility summary/status.
11. If rollback is required, click `Restore Last Backup`.

## Tooling Commands

Validate schema contract for any patch file:

```powershell
pwsh ./tools/validate-save-patch-pack.ps1 -PatchPackPath <patch.json> -SchemaPath tools/schemas/save-patch-pack.schema.json -Strict
```

Export patch pack from original/edited saves:

```powershell
pwsh ./tools/export-save-patch-pack.ps1 \
  -OriginalSavePath <original.sav> \
  -EditedSavePath <edited.sav> \
  -ProfileId base_swfoc \
  -SchemaId base_swfoc_steam_v1 \
  -OutputPath TestResults/patches/example.patch.json \
  -BuildIfNeeded
```

Apply patch pack atomically:

```powershell
pwsh ./tools/apply-save-patch-pack.ps1 \
  -TargetSavePath <target.sav> \
  -PatchPackPath <patch.json> \
  -TargetProfileId base_swfoc \
  -Strict $true \
  -BuildIfNeeded
```

## Expected Artifacts

For a successful apply on `C:\...\campaign.sav`:

- backup: `campaign.sav.bak.<runId>.sav`
- receipt: `campaign.sav.apply-receipt.<runId>.json`

## Failure Semantics

- `CompatibilityFailed`: schema/profile mismatch or strict source-hash mismatch.
- `ValidationFailed`: operations applied in-memory but schema validation failed.
- `WriteFailed` / `RolledBack`: write path failure; rollback attempted automatically.

## Notes

- Patch operations require `newValue`; `oldValue` is optional in v1.
- Preview evaluates compatibility against the selected target profile (not only pack metadata profile).
