param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$SchemaPath = "tools/schemas/ghidra-symbol-pack.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "validation-helpers.ps1")

$errors = New-ValidationErrorList

if (-not (Test-Path -Path $Path)) {
    Add-ValidationError -Errors $errors -Message "ghidra symbol pack file not found: $Path"
    Write-ValidationResult -Errors $errors -Label "ghidra symbol pack" -Path $Path
}

if (-not (Test-Path -Path $SchemaPath)) {
    Add-ValidationError -Errors $errors -Message "schema file not found: $SchemaPath"
    Write-ValidationResult -Errors $errors -Label "ghidra symbol pack" -Path $Path
}

$pack = Get-Content -Raw -Path $Path | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json

foreach ($required in $schema.required) {
    Require-ValidationField -Object $pack -Field $required -Errors $errors
}

if ($Strict) {
    if ([string]$pack.schemaVersion -ne "1.0") {
        Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
    }

    $fingerprint = $pack.binaryFingerprint
    foreach ($requiredFingerprintField in @("fingerprintId", "moduleName", "fileSha256")) {
        Require-ValidationField -Object $fingerprint -Field $requiredFingerprintField -Errors $errors -Prefix "binaryFingerprint"
    }

    $metadata = $pack.buildMetadata
    foreach ($requiredMetadataField in @("analysisRunId", "generatedAtUtc", "toolchain")) {
        Require-ValidationField -Object $metadata -Field $requiredMetadataField -Errors $errors -Prefix "buildMetadata"
    }

    $anchors = @($pack.anchors)
    for ($i = 0; $i -lt $anchors.Count; $i++) {
        $anchor = $anchors[$i]
        foreach ($requiredAnchorField in @("id", "address", "module", "confidence", "source")) {
            Require-ValidationField -Object $anchor -Field $requiredAnchorField -Errors $errors -Prefix "anchors[$i]"
        }
    }

    $capabilities = @($pack.capabilities)
    for ($i = 0; $i -lt $capabilities.Count; $i++) {
        $cap = $capabilities[$i]
        foreach ($requiredCapabilityField in @("featureId", "available", "state", "reasonCode", "requiredAnchors")) {
            Require-ValidationField -Object $cap -Field $requiredCapabilityField -Errors $errors -Prefix "capabilities[$i]"
        }
    }
}

Write-ValidationResult -Errors $errors -Label "ghidra symbol pack" -Path $Path
