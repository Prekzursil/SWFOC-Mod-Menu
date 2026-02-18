# SWFOC Source Corpus (R&D)

Last reviewed: 2026-02-18 UTC

## Official Sources
1. Petroglyph mod support/tooling page
- URL: https://www.petroglyphgames.com/eawmodtool/
- Notes: Official mod support location, debug executable references, tooling updates.

## Platform Build Signals
1. SteamDB patch history for app 32470
- URL: https://steamdb.info/app/32470/patchnotes/
- Notes: Build-drift signal for version-gating and fingerprint management.

## Community Validation Sources
1. Steam Community discussions (mod launch/command patterns)
- URL: https://steamcommunity.com/app/32470/discussions/
- Notes: Useful for practical launch edge cases; treat as version-sensitive.
2. Revora/ModDB guides
- Notes: Useful for workflow context only; not authoritative for runtime safety decisions.

## Source Priority Policy
1. Official Petroglyph/Steam signals.
2. In-repo deterministic tests and captured artifacts.
3. Community guidance (advisory only).
