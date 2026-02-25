Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$errors = New-Object System.Collections.Generic.List[string]

function Add-Error {
    param([string]$Message)
    $script:errors.Add($Message)
}

function Confirm-File {
    param([string]$Path)
    if (-not (Test-Path -Path $Path)) {
        Add-Error "missing file: $Path"
    }
}

function Confirm-Contains {
    param(
        [string]$Path,
        [string[]]$Needles
    )

    if (-not (Test-Path -Path $Path)) {
        Add-Error "missing file: $Path"
        return
    }

    $content = Get-Content -Raw -Path $Path
    foreach ($needle in $Needles) {
        if (-not $content.Contains($needle)) {
            Add-Error "$Path missing required text: $needle"
        }
    }
}

$requiredFiles = @(
    "AGENTS.md",
    "src/SwfocTrainer.Runtime/AGENTS.md",
    "tools/AGENTS.md",
    "tests/AGENTS.md",
    ".github/pull_request_template.md",
    ".github/ISSUE_TEMPLATE/bug.yml",
    ".github/ISSUE_TEMPLATE/calibration.yml",
    ".github/workflows/reviewer-automation.yml",
    ".github/workflows/release-portable.yml",
    "config/reviewer-roster.json",
    "docs/MOD_ONBOARDING_RUNBOOK.md",
    "docs/RELEASE_RUNBOOK.md",
    "docs/release-notes-template.md",
    "docs/RESEARCH_GAME_WORKFLOW.md",
    "tools/research/source-corpus.md",
    "tools/research/build-fingerprint.md",
    "tools/research/capture-binary-fingerprint.ps1",
    "tools/research/extract-pe-metadata.ps1",
    "tools/research/generate-signature-candidates.ps1",
    "tools/research/normalize-signature-pack.ps1",
    "tools/research/run-capability-intel.ps1",
    "tools/ghidra/run-headless.ps1",
    "tools/ghidra/run-headless.sh",
    "tools/ghidra/emit-symbol-pack.py",
    "tools/ghidra/emit-artifact-index.py",
    "tools/ghidra/check-determinism.py",
    "tools/validate-ghidra-symbol-pack.ps1",
    "tools/validate-ghidra-artifact-index.ps1",
    "tools/validate-binary-fingerprint.ps1",
    "tools/validate-signature-pack.ps1",
    "tools/schemas/calibration-artifact.schema.json",
    "tools/schemas/binary-fingerprint.schema.json",
    "tools/schemas/signature-pack.schema.json",
    "tools/schemas/ghidra-symbol-pack.schema.json",
    "tools/schemas/ghidra-analysis-summary.schema.json",
    "tools/schemas/ghidra-artifact-index.schema.json",
    "tools/schemas/support-bundle-manifest.schema.json",
    "tools/fixtures/binary_fingerprint_sample.json",
    "tools/fixtures/signature_pack_sample.json",
    "tools/fixtures/ghidra_symbol_pack_sample.json",
    "tools/fixtures/ghidra_analysis_summary_sample.json",
    "tools/fixtures/ghidra_artifact_index_sample.json"
)

foreach ($path in $requiredFiles) {
    Confirm-File -Path $path
}

$agentsRequiredHeaders = @("## Purpose", "## Scope", "## Required Evidence")
Confirm-Contains -Path "AGENTS.md" -Needles $agentsRequiredHeaders
Confirm-Contains -Path "src/SwfocTrainer.Runtime/AGENTS.md" -Needles $agentsRequiredHeaders
Confirm-Contains -Path "tools/AGENTS.md" -Needles $agentsRequiredHeaders
Confirm-Contains -Path "tests/AGENTS.md" -Needles $agentsRequiredHeaders

Confirm-Contains -Path ".github/pull_request_template.md" -Needles @(
    "## Affected Profiles",
    "## Reliability Evidence",
    "Launch reason code(s)",
    "Classification"
)

Confirm-Contains -Path ".github/ISSUE_TEMPLATE/bug.yml" -Needles @(
    "id: run_id",
    "id: repro_bundle_json",
    "id: repro_bundle_md",
    "id: classification",
    "id: launch_context"
)

Confirm-Contains -Path ".github/ISSUE_TEMPLATE/calibration.yml" -Needles @(
    "id: run_id",
    "id: bundle_json",
    "id: bundle_md",
    "id: runtime_mode"
)

Confirm-Contains -Path ".github/workflows/release-portable.yml" -Needles @(
    "SwfocTrainer-portable.zip.sha256",
    "gh release",
    "publish_release"
)

if (Test-Path -Path "config/reviewer-roster.json") {
    try {
        $roster = Get-Content -Raw -Path "config/reviewer-roster.json" | ConvertFrom-Json
        foreach ($key in @("version", "users", "teams", "fallbackLabel", "fallbackCommentEnabled")) {
            if (-not ($roster.PSObject.Properties.Name -contains $key)) {
                Add-Error "config/reviewer-roster.json missing key: $key"
            }
        }

        if ([string]::IsNullOrWhiteSpace($roster.fallbackLabel)) {
            Add-Error "config/reviewer-roster.json fallbackLabel must be non-empty"
        }
    }
    catch {
        Add-Error "config/reviewer-roster.json is not valid JSON: $($_.Exception.Message)"
    }
}

if ($errors.Count -gt 0) {
    Write-Output "policy contract validation failed:"
    foreach ($validationError in $errors) {
        Write-Output " - $validationError"
    }
    exit 1
}

Write-Output "policy contract validation passed"
