# SWFOC Trainer {{TAG}}

## Highlights

- Summary of key changes in this release.

## Included Artifacts

- `SwfocTrainer-portable.zip`
- `SwfocTrainer-portable.zip.sha256`

## Known Limitations

- Live profile validation still requires local SWFOC sessions on Windows.
- Runtime actions are profile-gated and may be unavailable when dependency markers are unresolved.
- Some features remain behind reliability gates to prevent unsafe writes.

## Verification

- Validate checksum before use:

```powershell
Get-FileHash .\SwfocTrainer-portable.zip -Algorithm SHA256
Get-Content .\SwfocTrainer-portable.zip.sha256
```

## Rollback
If this release is invalid, follow `docs/RELEASE_RUNBOOK.md` rollback steps and revert consumers to the last known-good tag.
