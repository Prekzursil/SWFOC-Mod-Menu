param(
    [Parameter(Mandatory = $true)][string]$FingerprintPath,
    [string]$SchemaPath = "tools/schemas/binary-fingerprint.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "validation-helpers.ps1")

$errors = New-ValidationErrorList

if (-not (Test-Path -Path $FingerprintPath)) {
    Add-ValidationError -Errors $errors -Message "fingerprint file not found: $FingerprintPath"
    Write-ValidationResult -Errors $errors -Label "binary fingerprint" -Path $FingerprintPath
}

if (-not (Test-Path -Path $SchemaPath)) {
    Add-ValidationError -Errors $errors -Message "schema file not found: $SchemaPath"
    Write-ValidationResult -Errors $errors -Label "binary fingerprint" -Path $FingerprintPath
}

$fingerprint = Get-Content -Raw -Path $FingerprintPath | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json

foreach ($required in $schema.required) {
    Confirm-ValidationField -Object $fingerprint -Field $required -Errors $errors
}

if ($Strict) {
    if ([string]$fingerprint.schemaVersion -ne "1.0") {
        Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
    }

    if ([string]$fingerprint.fileSha256 -notmatch '^[a-f0-9]{64}$') {
        Add-ValidationError -Errors $errors -Message "fileSha256 must be 64 lowercase hex characters"
    }

    if ($fingerprint.moduleList -isnot [System.Collections.IEnumerable]) {
        Add-ValidationError -Errors $errors -Message "moduleList must be an array"
    }
}

Write-ValidationResult -Errors $errors -Label "binary fingerprint" -Path $FingerprintPath
