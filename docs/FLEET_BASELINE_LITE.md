# Fleet Baseline Lite

This repository baseline package is intended for rollout to additional repos after Wave-1 validation.

## Included components

1. `AGENTS.md` operating contract
2. `agent_task.yml` issue intake template
3. PR template with Summary/Risk/Evidence/Rollback/Scope Guard
4. Agent label sync workflow
5. Agent queue workflow
6. Starter agent profiles
7. KPI weekly digest workflow
8. Branch protection audit workflow

## Rollout rule

Only apply this package to non-Wave repositories after Wave-1 KPIs remain stable for at least two weekly cycles.

## SWFOC-specific overlay constraints

When adapting this baseline to other runtime/game-modding contexts, include these SWFOC-specific requirements:

### Runtime evidence requirements

1. **Reproducible bundle mandate**
   - Runtime/mod bugfixes must include `repro-bundle.json` + `repro-bundle.md`
   - Bundle location: `TestResults/runs/<runId>/`
   - Schema validation required (see `tools/schemas/repro-bundle.schema.json`)
   - Bundle must include: run ID, timestamp, environment snapshot, test outcomes

2. **Classification categories**
   - `passed` - Test executed successfully, expected behavior confirmed
   - `skipped` - Test intentionally skipped with justification
   - `failed` - Test failed, regression or defect detected
   - `blocked_environment` - Test cannot run due to environment constraints
   - `blocked_profile_mismatch` - Test cannot run due to profile/mod incompatibility

3. **Evidence completeness gate**
   - PRs touching runtime/tooling/tests require evidence field completion
   - Policy enforced via `.github/workflows/policy-contract.yml`
   - Missing evidence blocks merge

### Reason-code diagnostics constraints

1. **Launch context reason codes**
   - Every runtime attach/process selection must emit explicit reason code
   - Reason codes explain profile recommendation logic
   - Examples: `steammod_exact_aotr`, `modpath_hint_roe`, `foc_safe_starwarsg_fallback`
   - Tool: `tools/detect-launch-context.py`

2. **Symbol health diagnostics**
   - Symbol resolution must report health status: `Healthy`, `Degraded`, `Unresolved`
   - Include confidence score and source (signature vs fallback offset)
   - Emit calibration artifact on attach with module fingerprint

3. **Profile compatibility tracking**
   - Affected profiles must be declared in PR template checkboxes
   - Profile IDs: `base_sweaw`, `base_swfoc`, `aotr_1397421866_swfoc`, `roe_3447786229_swfoc`, `custom`
   - Profile metadata contract: `docs/PROFILE_FORMAT.md`

4. **Failure reason attribution**
   - Runtime action failures must include explicit reason codes
   - Avoid silent success when artifacts are missing
   - Use structured diagnostics (not free-form error messages)

### Signature/offset safety rules

- No blind fixed-address runtime actions
- Prefer signature-first resolution with validated fallback offsets
- Fallback-only changes require explicit rationale in PR
- Keep profile compatibility explicit (base vs mod-specific)

### Testing requirements

- Deterministic test suite must pass before merge
- Live/process tests excluded from CI (require manual validation)
- Canonical verification command documented in `AGENTS.md`
- Test evidence required for all runtime behavior changes
