# Custom Profiles (Auto-Discovery)

This folder stores generated draft profiles for workshop mods that are outside the base shipped set.

## Generation Workflow

1. Discover and normalize workshop mods:

```powershell
python3 tools/workshop/discover-top-mods.py --output TestResults/mod-discovery/<runId>/top-mods.json --limit 10
```

1. Enrich top-mod records into onboarding seeds:

```powershell
python3 tools/workshop/enrich-mod-metadata.py --input TestResults/mod-discovery/<runId>/top-mods.json --output TestResults/mod-discovery/<runId>/generated-profile-seeds.json --source-run-id <runId>
```

1. Validate artifact contracts:

```powershell
pwsh tools/validate-workshop-topmods.ps1 -Path TestResults/mod-discovery/<runId>/top-mods.json -Strict
pwsh tools/validate-generated-profile-seed.ps1 -Path TestResults/mod-discovery/<runId>/generated-profile-seeds.json -Strict
```

1. Generate draft profiles:

```powershell
pwsh tools/workshop/generate-profiles-from-seeds.ps1 -SeedPath TestResults/mod-discovery/<runId>/generated-profile-seeds.json -OutputRoot profiles/custom -NamespaceRoot custom -Force
```

## Metadata Contract

Generated drafts include metadata keys used by onboarding and diagnostics:

- `origin=auto_discovery`
- `sourceRunId=<discovery run id>`
- `confidence=<0..1>`
- `parentProfile=<candidate base profile id>`
- `requiredWorkshopIds=<comma-delimited list>`
- `profileAliases=<comma-delimited aliases>`
- `localPathHints=<comma-delimited launch path hints>`

## Safety Notes

- Generated profiles are drafts. Runtime mutation remains fail-closed unless capability proof is verified.
- Review every generated draft before promotion to a default profile pack.
- Keep discovery artifacts under `TestResults/mod-discovery/<runId>/` for reproducibility.
