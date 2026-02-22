# Build Fingerprint Contract

A fingerprint represents the exact runtime module basis used by capability maps.

## Required Fields

1. `fingerprintId`
2. `fileSha256`
3. `moduleName`
4. `productVersion`
5. `fileVersion`
6. `timestampUtc`
7. `moduleList`
8. `sourcePath`

## Fingerprint ID Rule

- `fingerprintId = <moduleNameWithoutExtLower>_<sha256Prefix16>`

Example:

- `starwarsg_d3e0b6d3a9a31f42`

## Output Location

- `TestResults/research/<runId>/fingerprint.json`

## Notes

- Fingerprints are immutable identifiers for map lookup.
- If fingerprint changes, capability map must be considered unknown until revalidated.
