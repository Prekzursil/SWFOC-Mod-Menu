param(
    [Parameter(Mandatory = $true)][string]$ArtifactPath,
    [string]$SchemaPath = "tools/schemas/calibration-artifact.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $ArtifactPath)) {
    throw "Artifact file not found: $ArtifactPath"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$artifact = Get-Content -Raw -Path $ArtifactPath | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json
$errors = New-Object System.Collections.Generic.List[string]

function Add-Error {
    param([string]$Message)
    $script:errors.Add($Message)
}

function Require-Field {
    param(
        [object]$Object,
        [string]$Field,
        [switch]$AllowNull
    )

    $prop = $Object.PSObject.Properties[$Field]
    if ($null -eq $prop) {
        Add-Error "missing required field: $Field"
        return
    }

    $value = $prop.Value
    if ($null -eq $value -and -not $AllowNull) {
        Add-Error "null required field: $Field"
        return
    }

    if (-not $AllowNull -and $value -is [string] -and [string]::IsNullOrWhiteSpace($value)) {
        Add-Error "empty required field: $Field"
    }
}

foreach ($required in $schema.required) {
    Require-Field -Object $artifact -Field ([string]$required)
}

if ($artifact.schemaVersion -ne "1.0") {
    Add-Error "schemaVersion must be 1.0"
}

foreach ($required in @("generatedAtUtc", "profileId", "moduleFingerprint", "candidates")) {
    Require-Field -Object $artifact -Field $required
}

$candidates = @($artifact.candidates)
if ($candidates.Count -eq 0) {
    Add-Error "candidates must contain at least one entry"
}

for ($i = 0; $i -lt $candidates.Count; $i++) {
    $candidate = $candidates[$i]
    foreach ($required in @("symbol", "source", "healthStatus", "confidence")) {
        Require-Field -Object $candidate -Field $required
    }

    $confidence = [double]$candidate.confidence
    if ($confidence -lt 0 -or $confidence -gt 1) {
        Add-Error "candidates[$i].confidence must be between 0 and 1"
    }
}

if ($null -ne $artifact.process) {
    foreach ($required in @("pid", "name", "path", "commandLineAvailable", "launchKind", "launchReasonCode")) {
        Require-Field -Object $artifact.process -Field $required
    }
}

if ($Strict) {
    if ([string]::IsNullOrWhiteSpace([string]$artifact.moduleFingerprint) -or [string]$artifact.moduleFingerprint -eq "session_unavailable") {
        Add-Error "strict mode requires non-placeholder moduleFingerprint"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "calibration artifact validation failed:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host " - $err" -ForegroundColor Red
    }
    exit 1
}

Write-Host "calibration artifact validation passed: $ArtifactPath"
