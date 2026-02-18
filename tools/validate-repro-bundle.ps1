param(
    [Parameter(Mandatory = $true)][string]$BundlePath,
    [string]$SchemaPath = "tools/schemas/repro-bundle.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "validation-helpers.ps1")

if (-not (Test-Path -Path $BundlePath)) {
    throw "Bundle file not found: $BundlePath"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$bundle = Get-Content -Raw -Path $BundlePath | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json
$errors = New-ValidationErrorList

foreach ($required in $schema.required) {
    Require-ValidationField -Object $bundle -Field ([string]$required) -Errors $errors
}

if ($bundle.schemaVersion -ne "1.0") {
    Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
}

$allowedScopes = @("AOTR", "ROE", "TACTICAL", "FULL")
if ($allowedScopes -notcontains [string]$bundle.scope) {
    Add-ValidationError -Errors $errors -Message "scope must be one of: $($allowedScopes -join ', ')"
}

$allowedClassifications = @("passed", "skipped", "failed", "blocked_environment", "blocked_profile_mismatch")
if ($allowedClassifications -notcontains [string]$bundle.classification) {
    Add-ValidationError -Errors $errors -Message "classification must be one of: $($allowedClassifications -join ', ')"
}

$allowedOutcomes = @("Passed", "Failed", "Skipped", "Missing", "Unknown")
$liveTests = @($bundle.liveTests)
if ($liveTests.Count -eq 0) {
    Add-ValidationError -Errors $errors -Message "liveTests must contain at least one entry"
}

foreach ($test in $liveTests) {
    foreach ($required in @("name", "outcome", "trxPath", "message")) {
        $allowNull = $required -eq "message"
        Require-ValidationField -Object $test -Field $required -Errors $errors -AllowNull:$allowNull -Prefix "liveTests[$($test.name)]"
    }

    if ($allowedOutcomes -notcontains [string]$test.outcome) {
        Add-ValidationError -Errors $errors -Message "liveTests[$($test.name)] outcome invalid: $($test.outcome)"
    }

    if ([string]::IsNullOrWhiteSpace([string]$test.trxPath)) {
        Add-ValidationError -Errors $errors -Message "liveTests[$($test.name)] trxPath is empty"
    }
}

$processSnapshot = @($bundle.processSnapshot)
foreach ($process in $processSnapshot) {
    Require-ValidationField -Object $process -Field "pid" -Errors $errors
    Require-ValidationField -Object $process -Field "name" -Errors $errors
    Require-ValidationField -Object $process -Field "commandLine" -Errors $errors -AllowNull
}

foreach ($required in @("profileId", "reasonCode", "confidence", "launchKind")) {
    Require-ValidationField -Object $bundle.launchContext -Field $required -Errors $errors -AllowNull
}

foreach ($required in @("hint", "effective", "reasonCode")) {
    Require-ValidationField -Object $bundle.runtimeMode -Field $required -Errors $errors
}

foreach ($required in @("dependencyState", "helperReadiness", "symbolHealthSummary")) {
    Require-ValidationField -Object $bundle.diagnostics -Field $required -Errors $errors
}

if ($Strict) {
    $hasPassed = (@($liveTests | Where-Object { $_.outcome -eq "Passed" })).Count -gt 0
    if ($bundle.classification -eq "passed" -and -not $hasPassed) {
        Add-ValidationError -Errors $errors -Message "classification=passed requires at least one passed live test"
    }

    if ($bundle.classification -eq "blocked_environment" -and (@($bundle.processSnapshot)).Count -gt 0) {
        Add-ValidationError -Errors $errors -Message "classification=blocked_environment requires empty processSnapshot"
    }
}

Write-ValidationResult -Errors $errors -Label "repro bundle" -Path $BundlePath
