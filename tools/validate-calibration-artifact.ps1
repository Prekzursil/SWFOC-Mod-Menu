param(
    [Parameter(Mandatory = $true)][string]$ArtifactPath,
    [string]$SchemaPath = "tools/schemas/calibration-artifact.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "validation-helpers.ps1")

if (-not (Test-Path -Path $ArtifactPath)) {
    throw "Artifact file not found: $ArtifactPath"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$artifact = Get-Content -Raw -Path $ArtifactPath | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json
$errors = New-ValidationErrorList

foreach ($required in $schema.required) {
    Require-ValidationField -Object $artifact -Field ([string]$required) -Errors $errors
}

if ($artifact.schemaVersion -ne "1.0") {
    Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
}

foreach ($required in @("generatedAtUtc", "profileId", "moduleFingerprint", "candidates")) {
    Require-ValidationField -Object $artifact -Field $required -Errors $errors
}

$candidates = @($artifact.candidates)
if ($candidates.Count -eq 0) {
    Add-ValidationError -Errors $errors -Message "candidates must contain at least one entry"
}

for ($i = 0; $i -lt $candidates.Count; $i++) {
    $candidate = $candidates[$i]
    foreach ($required in @("symbol", "source", "healthStatus", "confidence")) {
        Require-ValidationField -Object $candidate -Field $required -Errors $errors -Prefix "candidates[$i]"
    }

    $confidence = [double]$candidate.confidence
    if ($confidence -lt 0 -or $confidence -gt 1) {
        Add-ValidationError -Errors $errors -Message "candidates[$i].confidence must be between 0 and 1"
    }
}

if ($null -ne $artifact.process) {
    foreach ($required in @("pid", "name", "path", "commandLineAvailable", "launchKind", "launchReasonCode")) {
        Require-ValidationField -Object $artifact.process -Field $required -Errors $errors -Prefix "process"
    }
}

if ($Strict) {
    if ([string]::IsNullOrWhiteSpace([string]$artifact.moduleFingerprint) -or [string]$artifact.moduleFingerprint -eq "session_unavailable") {
        Add-ValidationError -Errors $errors -Message "strict mode requires non-placeholder moduleFingerprint"
    }
}

Write-ValidationResult -Errors $errors -Label "calibration artifact" -Path $ArtifactPath
