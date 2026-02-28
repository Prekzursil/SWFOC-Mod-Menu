# Custom Auto-Discovery Profiles

This folder is the output target for draft profiles generated from workshop discovery seeds.

## Runbook

1. Discover top workshop mods (live or deterministic fixture mode):
   - `python3 tools/workshop/discover-top-mods.py --source-file tools/fixtures/workshop_topmods_sample.json --output TestResults/tmp-topmods.json --limit 3`
2. Enrich discovered mods into profile seeds:
   - `python3 tools/workshop/enrich-mod-metadata.py --input TestResults/tmp-topmods.json --output TestResults/tmp-seeds.json --source-run-id <runId>`
3. Validate contracts in strict mode:
   - `pwsh tools/validate-workshop-topmods.ps1 -Path TestResults/tmp-topmods.json -Strict`
   - `pwsh tools/validate-generated-profile-seed.ps1 -Path TestResults/tmp-seeds.json -Strict`
4. Generate draft profile files:
   - `pwsh tools/workshop/generate-profiles-from-seeds.ps1 -SeedsPath TestResults/tmp-seeds.json`

## Output Path Defaults

- `ProfilesRootPath`: `profiles/custom`
- `Namespace`: `profiles`
- Effective draft profile path: `profiles/custom/profiles/*.json`

Each generated draft includes metadata markers:

- `origin=auto_discovery`
- `sourceRunId=<run id>`
- `confidence=<0..1>`
- `parentProfile=<candidate base profile>`
