# Release Runbook

## Channel Decision
GitHub Releases is the primary distribution channel for SWFOC Trainer.

## Triggering a Release

### Tag-driven publish (recommended)

```powershell
git tag vX.Y.Z
git push origin vX.Y.Z
```

This triggers `.github/workflows/release-portable.yml` to:

- build release binaries,
- package `SwfocTrainer-portable.zip`,
- generate `SwfocTrainer-portable.zip.sha256`,
- publish both files to GitHub Releases.

### Manual dispatch
Use workflow dispatch for dry runs or controlled publish.

- `publish_release=false`: package + artifact only (no GitHub Release publish).
- `publish_release=true`: publish/update release for `tag_name`.

## Artifact Verification
Before using or distributing a release package:

```powershell
Get-FileHash .\SwfocTrainer-portable.zip -Algorithm SHA256
Get-Content .\SwfocTrainer-portable.zip.sha256
```

The computed hash must match the `.sha256` file.

## Security Controls

- Tags for published releases are immutable by policy.
- Release packages include checksum files for integrity verification.
- Do not consume unsigned/unverified artifacts from non-release channels for production usage.

## Rollback Procedure

1. Identify the bad release tag and the last known-good tag.
2. Mark the bad release notes with rollback warning and link to replacement tag.
3. Re-point downstream automation/documentation to the known-good tag.
4. If needed, create a hotfix tag and publish via the same release workflow.

## Dry-Run Evidence Template
Use this in issue/PR comments:

- Workflow run URL
- Tag or manual dispatch inputs
- Uploaded assets
- SHA256 verification command + output summary
