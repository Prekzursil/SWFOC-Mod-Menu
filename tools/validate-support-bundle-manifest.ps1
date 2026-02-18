param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string]$SchemaPath = "tools/schemas/support-bundle-manifest.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "validation-helpers.ps1")

if (-not (Test-Path -Path $ManifestPath)) {
    throw "Manifest file not found: $ManifestPath"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json
$errors = New-ValidationErrorList

foreach ($required in $schema.required) {
    Require-ValidationField -Object $manifest -Field ([string]$required) -Errors $errors
}

if ($manifest.schemaVersion -ne "1.0") {
    Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
}

$included = @($manifest.includedFiles)
if ($included.Count -eq 0) {
    Add-ValidationError -Errors $errors -Message "includedFiles must contain at least one entry"
}

for ($i = 0; $i -lt $included.Count; $i++) {
    if ([string]::IsNullOrWhiteSpace([string]$included[$i])) {
        Add-ValidationError -Errors $errors -Message "includedFiles[$i] must be non-empty"
    }
}

$warnings = @($manifest.warnings)
for ($i = 0; $i -lt $warnings.Count; $i++) {
    if ($null -eq $warnings[$i]) {
        Add-ValidationError -Errors $errors -Message "warnings[$i] must not be null"
    }
}

if ($Strict) {
    if ($included -notcontains "manifest.json") {
        Add-ValidationError -Errors $errors -Message "strict mode requires includedFiles to list manifest.json"
    }

    $hasRuntimeSnapshot = $included | Where-Object { $_ -match "^runtime-snapshot\.json$" }
    if (-not $hasRuntimeSnapshot) {
        Add-ValidationError -Errors $errors -Message "strict mode requires runtime-snapshot.json in includedFiles"
    }
}

Write-ValidationResult -Errors $errors -Label "support bundle manifest" -Path $ManifestPath
