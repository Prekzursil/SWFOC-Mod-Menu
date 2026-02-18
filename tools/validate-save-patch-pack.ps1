param(
    [Parameter(Mandatory = $true)][string]$PatchPackPath,
    [string]$SchemaPath = "tools/schemas/save-patch-pack.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $PatchPackPath)) {
    throw "Patch-pack file not found: $PatchPackPath"
}

if (-not (Test-Path -Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$pack = Get-Content -Raw -Path $PatchPackPath | ConvertFrom-Json
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

    if ($null -eq $Object) {
        Add-Error "object is null while checking field '$Field'"
        return
    }

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
    Require-Field -Object $pack -Field ([string]$required)
}

if ($pack.metadata -eq $null) {
    Add-Error "metadata is required"
}
else {
    foreach ($required in @("schemaVersion", "profileId", "schemaId", "sourceHash", "createdAtUtc")) {
        Require-Field -Object $pack.metadata -Field $required
    }

    if ([string]$pack.metadata.schemaVersion -ne "1.0") {
        Add-Error "metadata.schemaVersion must equal '1.0'"
    }

    if (-not [System.Text.RegularExpressions.Regex]::IsMatch([string]$pack.metadata.sourceHash, "^[a-f0-9]{64}$")) {
        Add-Error "metadata.sourceHash must be lowercase sha256 hex"
    }
}

if ($pack.compatibility -eq $null) {
    Add-Error "compatibility is required"
}
else {
    foreach ($required in @("allowedProfileIds", "requiredSchemaId")) {
        Require-Field -Object $pack.compatibility -Field $required
    }

    $allowed = @($pack.compatibility.allowedProfileIds)
    if ($allowed.Count -lt 1) {
        Add-Error "compatibility.allowedProfileIds must include at least one profile"
    }
    else {
        foreach ($profileId in $allowed) {
            if ([string]::IsNullOrWhiteSpace([string]$profileId)) {
                Add-Error "compatibility.allowedProfileIds contains empty value"
            }
        }
    }
}

$operations = @($pack.operations)
if ($operations.Count -lt 1 -and $Strict) {
    Add-Error "operations must contain at least one entry in strict mode"
}

for ($index = 0; $index -lt $operations.Count; $index++) {
    $operation = $operations[$index]
    foreach ($required in @("kind", "fieldPath", "fieldId", "valueType", "newValue", "offset")) {
        Require-Field -Object $operation -Field $required
    }

    if ([string]$operation.kind -ne "SetValue") {
        Add-Error "operations[$index].kind must be 'SetValue'"
    }

    if ([int]$operation.offset -lt 0) {
        Add-Error "operations[$index].offset must be >= 0"
    }
}

if ($errors.Count -gt 0) {
    Write-Host "save patch-pack validation failed:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host " - $err" -ForegroundColor Red
    }
    exit 1
}

Write-Host "save patch-pack validation passed: $PatchPackPath"
