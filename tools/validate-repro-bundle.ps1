param(
    [Parameter(Mandatory = $true)][string]$BundlePath,
    [string]$SchemaPath = "tools/schemas/repro-bundle.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $BundlePath)) {
    throw "Bundle file not found: $BundlePath"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$bundle = Get-Content -Raw -Path $BundlePath | ConvertFrom-Json
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
    Require-Field -Object $bundle -Field ([string]$required)
}

if ($bundle.schemaVersion -ne "1.0") {
    Add-Error "schemaVersion must be 1.0"
}

$allowedScopes = @("AOTR", "ROE", "TACTICAL", "FULL")
if ($allowedScopes -notcontains [string]$bundle.scope) {
    Add-Error "scope must be one of: $($allowedScopes -join ', ')"
}

$allowedClassifications = @("passed", "skipped", "failed", "blocked_environment", "blocked_profile_mismatch")
if ($allowedClassifications -notcontains [string]$bundle.classification) {
    Add-Error "classification must be one of: $($allowedClassifications -join ', ')"
}

$allowedOutcomes = @("Passed", "Failed", "Skipped", "Missing", "Unknown")
$liveTests = @($bundle.liveTests)
if ($liveTests.Count -eq 0) {
    Add-Error "liveTests must contain at least one entry"
}

foreach ($test in $liveTests) {
    foreach ($required in @("name", "outcome", "trxPath", "message")) {
        if ($required -eq "message") {
            Require-Field -Object $test -Field $required -AllowNull
        }
        else {
            Require-Field -Object $test -Field $required
        }
    }

    if ($allowedOutcomes -notcontains [string]$test.outcome) {
        Add-Error "liveTests[$($test.name)] outcome invalid: $($test.outcome)"
    }

    if ([string]::IsNullOrWhiteSpace([string]$test.trxPath)) {
        Add-Error "liveTests[$($test.name)] trxPath is empty"
    }
}

$processSnapshot = @($bundle.processSnapshot)
foreach ($process in $processSnapshot) {
    Require-Field -Object $process -Field "pid"
    Require-Field -Object $process -Field "name"
    Require-Field -Object $process -Field "commandLine" -AllowNull
}

foreach ($required in @("profileId", "reasonCode", "confidence", "launchKind")) {
    Require-Field -Object $bundle.launchContext -Field $required -AllowNull
}

foreach ($required in @("hint", "effective", "reasonCode")) {
    Require-Field -Object $bundle.runtimeMode -Field $required
}

foreach ($required in @("dependencyState", "helperReadiness", "symbolHealthSummary")) {
    Require-Field -Object $bundle.diagnostics -Field $required
}

if ($Strict) {
    $hasPassed = (@($liveTests | Where-Object { $_.outcome -eq "Passed" })).Count -gt 0
    if ($bundle.classification -eq "passed" -and -not $hasPassed) {
        Add-Error "classification=passed requires at least one passed live test"
    }

    if ($bundle.classification -eq "blocked_environment" -and (@($bundle.processSnapshot)).Count -gt 0) {
        Add-Error "classification=blocked_environment requires empty processSnapshot"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "repro bundle validation failed:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host " - $err" -ForegroundColor Red
    }
    exit 1
}

Write-Host "repro bundle validation passed: $BundlePath"
