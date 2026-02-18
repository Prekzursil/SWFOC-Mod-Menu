## Summary

- What changed?
- Why was it needed?

## Scope

- [ ] Runtime logic
- [ ] Profile metadata/schema
- [ ] UI/App behavior
- [ ] CI/Tooling
- [ ] Docs only

## Affected Profiles

- [ ] `base_sweaw`
- [ ] `base_swfoc`
- [ ] `aotr_1397421866_swfoc`
- [ ] `roe_3447786229_swfoc`
- [ ] `custom`

## Reliability Evidence

- Repro bundle JSON: `<path or artifact URL>`
- Repro bundle markdown: `<path or artifact URL>`
- Launch reason code(s): `<reasonCode list>`
- Runtime mode diagnostics: `<hint/effective/reasonCode>`
- Classification: `<passed|skipped|failed|blocked_environment|blocked_profile_mismatch>`

## Validation Evidence

- [ ] Deterministic tests run
- [ ] Launch-context fixture smoke run
- [ ] Repro bundle validated (`tools/validate-repro-bundle.ps1`)
- [ ] Live/manual verification (if applicable)

### Commands / Results

Paste key command outputs or concise summaries.

## Runtime Safety Checklist

- [ ] No destructive behavior added
- [ ] Profile compatibility impact reviewed (`base`, `aotr`, `roe`, `custom`)
- [ ] Signature/fallback changes include explicit rationale
- [ ] Dependency-sensitive actions behavior verified

## Metadata / Contract Changes

- [ ] No public contract changes
- [ ] Contract changed and docs updated (`docs/PROFILE_FORMAT.md`)

## Linked Issues

Closes #
