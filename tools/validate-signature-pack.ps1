param(
    [Parameter(Mandatory = $true)][string]$SignaturePackPath,
    [string]$SchemaPath = "tools/schemas/signature-pack.schema.json",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "validation-helpers.ps1")

$errors = New-ValidationErrorList

if (-not (Test-Path -Path $SignaturePackPath)) {
    Add-ValidationError -Errors $errors -Message "signature pack file not found: $SignaturePackPath"
    Write-ValidationResult -Errors $errors -Label "signature pack" -Path $SignaturePackPath
}

if (-not (Test-Path -Path $SchemaPath)) {
    Add-ValidationError -Errors $errors -Message "schema file not found: $SchemaPath"
    Write-ValidationResult -Errors $errors -Label "signature pack" -Path $SignaturePackPath
}

$pack = Get-Content -Raw -Path $SignaturePackPath | ConvertFrom-Json
$schema = Get-Content -Raw -Path $SchemaPath | ConvertFrom-Json

foreach ($required in $schema.required) {
    Confirm-ValidationField -Object $pack -Field $required -Errors $errors
}

if ($Strict) {
    if ([string]$pack.schemaVersion -ne "1.0") {
        Add-ValidationError -Errors $errors -Message "schemaVersion must be 1.0"
    }

    $anchors = @($pack.anchors)
    for ($i = 0; $i -lt $anchors.Count; $i++) {
        $anchor = $anchors[$i]
        foreach ($requiredAnchorField in @("id", "kind", "pattern", "required")) {
            Confirm-ValidationField -Object $anchor -Field $requiredAnchorField -Errors $errors -Prefix "anchors[$i]"
        }
    }

    if ($pack.operations -eq $null) {
        Add-ValidationError -Errors $errors -Message "operations must be an object"
    }
    else {
        $operationNames = @($pack.operations.PSObject.Properties.Name)
        if ($operationNames.Count -eq 0) {
            Add-ValidationError -Errors $errors -Message "operations must include at least one operation"
        }

        foreach ($opName in $operationNames) {
            $operation = $pack.operations.$opName
            foreach ($requiredOpField in @("requiredAnchors", "optionalAnchors")) {
                Confirm-ValidationField -Object $operation -Field $requiredOpField -Errors $errors -Prefix "operations.$opName"
            }
        }
    }
}

Write-ValidationResult -Errors $errors -Label "signature pack" -Path $SignaturePackPath
