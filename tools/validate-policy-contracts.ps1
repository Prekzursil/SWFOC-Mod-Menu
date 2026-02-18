Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$errors = New-Object System.Collections.Generic.List[string]

function Add-Error {
    param([string]$Message)
    $script:errors.Add($Message)
}

function Require-File {
    param([string]$Path)
    if (-not (Test-Path -Path $Path)) {
        Add-Error "missing file: $Path"
    }
}

function Require-Contains {
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
    ".github/ISSUE_TEMPLATE/calibration.yml"
)

foreach ($path in $requiredFiles) {
    Require-File -Path $path
}

$agentsRequiredHeaders = @("## Purpose", "## Scope", "## Required Evidence")
Require-Contains -Path "AGENTS.md" -Needles $agentsRequiredHeaders
Require-Contains -Path "src/SwfocTrainer.Runtime/AGENTS.md" -Needles $agentsRequiredHeaders
Require-Contains -Path "tools/AGENTS.md" -Needles $agentsRequiredHeaders
Require-Contains -Path "tests/AGENTS.md" -Needles $agentsRequiredHeaders

Require-Contains -Path ".github/pull_request_template.md" -Needles @(
    "## Affected Profiles",
    "## Reliability Evidence",
    "Launch reason code(s)",
    "Classification"
)

Require-Contains -Path ".github/ISSUE_TEMPLATE/bug.yml" -Needles @(
    "id: run_id",
    "id: repro_bundle_json",
    "id: repro_bundle_md",
    "id: classification",
    "id: launch_context"
)

Require-Contains -Path ".github/ISSUE_TEMPLATE/calibration.yml" -Needles @(
    "id: run_id",
    "id: bundle_json",
    "id: bundle_md",
    "id: runtime_mode"
)

if ($errors.Count -gt 0) {
    Write-Host "policy contract validation failed:" -ForegroundColor Red
    foreach ($error in $errors) {
        Write-Host " - $error" -ForegroundColor Red
    }
    exit 1
}

Write-Host "policy contract validation passed"
