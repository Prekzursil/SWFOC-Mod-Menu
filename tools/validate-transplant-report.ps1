param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$SchemaPath = "tools/schemas/transplant-report.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "validation-helpers.ps1")

if (-not (Test-Path -Path $Path)) {
    throw "Transplant report file not found: $Path"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$report = Get-Content -Raw -Path $Path | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json
$errors = New-ValidationErrorList

foreach ($required in $schema.required) {
    Confirm-ValidationField -Object $report -Field ([string]$required) -Errors $errors
}

if ([string]$report.schemaVersion -ne "1.0") {
    Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
}

if ([int]$report.totalEntities -lt 0) {
    Add-ValidationError -Errors $errors -Message "totalEntities must be >= 0"
}

if ([int]$report.blockingEntityCount -lt 0) {
    Add-ValidationError -Errors $errors -Message "blockingEntityCount must be >= 0"
}

$entities = @($report.entities)
foreach ($entity in $entities) {
    foreach ($required in @("entityId", "sourceProfileId", "requiresTransplant", "resolved", "reasonCode", "message", "missingDependencies")) {
        Confirm-ValidationField -Object $entity -Field $required -Errors $errors -Prefix "entities[$($entity.entityId)]"
    }
}

if ($Strict) {
    $derivedBlocking = @($entities | Where-Object { -not [bool]$_.resolved }).Count
    if ([int]$report.blockingEntityCount -ne $derivedBlocking) {
        Add-ValidationError -Errors $errors -Message "blockingEntityCount does not match unresolved entity count"
    }

    $shouldAllResolve = $derivedBlocking -eq 0
    if ([bool]$report.allResolved -ne $shouldAllResolve) {
        Add-ValidationError -Errors $errors -Message "allResolved does not match unresolved entity count"
    }
}

Write-ValidationResult -Errors $errors -Label "transplant report" -Path $Path
