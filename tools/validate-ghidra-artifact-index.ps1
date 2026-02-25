param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$SchemaPath = "tools/schemas/ghidra-artifact-index.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "validation-helpers.ps1")

$errors = New-ValidationErrorList

if (-not (Test-Path -Path $Path)) {
    Add-ValidationError -Errors $errors -Message "ghidra artifact index file not found: $Path"
    Write-ValidationResult -Errors $errors -Label "ghidra artifact index" -Path $Path
}

if (-not (Test-Path -Path $SchemaPath)) {
    Add-ValidationError -Errors $errors -Message "schema file not found: $SchemaPath"
    Write-ValidationResult -Errors $errors -Label "ghidra artifact index" -Path $Path
}

$index = Get-Content -Raw -Path $Path | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json

foreach ($required in $schema.required) {
    Confirm-ValidationField -Object $index -Field $required -Errors $errors
}

if ($Strict) {
    if ([string]$index.schemaVersion -ne "1.0") {
        Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
    }

    foreach ($requiredFingerprintField in @("fingerprintId", "moduleName", "fileSha256")) {
        Confirm-ValidationField -Object $index.binaryFingerprint -Field $requiredFingerprintField -Errors $errors -Prefix "binaryFingerprint"
    }

    foreach ($requiredPointerField in @("rawSymbolsPath", "symbolPackPath", "analysisSummaryPath")) {
        Confirm-ValidationField -Object $index.artifactPointers -Field $requiredPointerField -Errors $errors -Prefix "artifactPointers"
    }
    Confirm-ValidationField -Object $index.artifactPointers -Field "decompileArchivePath" -Errors $errors -Prefix "artifactPointers" -AllowNull

    foreach ($requiredHashField in @("rawSymbolsSha256", "symbolPackSha256", "analysisSummarySha256")) {
        Confirm-ValidationField -Object $index.fileHashes -Field $requiredHashField -Errors $errors -Prefix "fileHashes"
    }
    Confirm-ValidationField -Object $index.fileHashes -Field "decompileArchiveSha256" -Errors $errors -Prefix "fileHashes" -AllowNull
}

Write-ValidationResult -Errors $errors -Label "ghidra artifact index" -Path $Path
