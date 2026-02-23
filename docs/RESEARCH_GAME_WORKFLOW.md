# Game Workflow Research Contract (R&D)

This document defines the reverse-engineering evidence contract for the `rd/deep-re-universal-sdk` track.

## Scope

- Analyze only binaries and mod payloads you legally own locally.
- Publish only derived metadata artifacts (hashes, signatures, capability maps, notes).
- Never commit proprietary game binaries, workshop archives, or extracted copyrighted content.

## Mandatory Artifact Set
Each research run must emit:

- `TestResults/research/<runId>/fingerprint.json`
- `TestResults/research/<runId>/signature-pack.json`
- `TestResults/research/<runId>/analysis-notes.md`

## Provenance Requirements
For every conclusion, record:

1. Source module path.
2. Fingerprint ID and SHA256.
3. Tool command used.
4. Date/time (UTC).
5. Analyst note.

## Safety Rules

1. No blind fixed-address execution in production paths.
2. Capability uncertainty blocks mutating operations.
3. Unknown fingerprints default to fail-closed behavior.

## Promotion Rule
No R&D signature/capability artifact may be promoted to mainline without:

- deterministic tests,
- schema validation,
- explicit reason-code evidence for degraded/unavailable operations.
