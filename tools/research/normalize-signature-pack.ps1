param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $InputPath)) {
    throw "Input signature pack not found: $InputPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = $InputPath
}

$pack = Get-Content -Raw -Path $InputPath | ConvertFrom-Json

$orderedAnchors = @($pack.anchors | Sort-Object id)
$orderedOperations = [ordered]@{}
foreach ($name in @($pack.operations.PSObject.Properties.Name | Sort-Object)) {
    $operation = $pack.operations.$name
    $required = @($operation.requiredAnchors | Sort-Object -Unique)
    $optional = @($operation.optionalAnchors | Sort-Object -Unique)
    $orderedOperations[$name] = [ordered]@{
        requiredAnchors = $required
        optionalAnchors = $optional
    }
}

$result = [ordered]@{
    schemaVersion = [string]$pack.schemaVersion
    runId = [string]$pack.runId
    generatedAtUtc = [string]$pack.generatedAtUtc
    fingerprintId = [string]$pack.fingerprintId
    defaultProfileId = [string]$pack.defaultProfileId
    sourceProfilePath = [string]$pack.sourceProfilePath
    anchors = $orderedAnchors
    operations = $orderedOperations
}

($result | ConvertTo-Json -Depth 12) | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host "Normalized signature pack written: $OutputPath"
