param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string]$SchemaPath = "tools/schemas/support-bundle-manifest.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $ManifestPath)) {
    throw "Manifest file not found: $ManifestPath"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
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
    Require-Field -Object $manifest -Field ([string]$required)
}

if ($manifest.schemaVersion -ne "1.0") {
    Add-Error "schemaVersion must be 1.0"
}

$included = @($manifest.includedFiles)
if ($included.Count -eq 0) {
    Add-Error "includedFiles must contain at least one entry"
}

for ($i = 0; $i -lt $included.Count; $i++) {
    if ([string]::IsNullOrWhiteSpace([string]$included[$i])) {
        Add-Error "includedFiles[$i] must be non-empty"
    }
}

$warnings = @($manifest.warnings)
for ($i = 0; $i -lt $warnings.Count; $i++) {
    if ($null -eq $warnings[$i]) {
        Add-Error "warnings[$i] must not be null"
    }
}

if ($Strict) {
    if ($included -notcontains "manifest.json") {
        Add-Error "strict mode requires includedFiles to list manifest.json"
    }

    $hasRuntimeSnapshot = $included | Where-Object { $_ -match "^runtime-snapshot\.json$" }
    if (-not $hasRuntimeSnapshot) {
        Add-Error "strict mode requires runtime-snapshot.json in includedFiles"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "support bundle manifest validation failed:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host " - $err" -ForegroundColor Red
    }
    exit 1
}

Write-Host "support bundle manifest validation passed: $ManifestPath"
