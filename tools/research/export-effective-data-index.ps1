param(
    [Parameter(Mandatory = $true)][string]$ProfileId,
    [Parameter(Mandatory = $true)][string]$OutPath,
    [string]$GameRootPath = "",
    [string]$ModPath = "",
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath "..") -ChildPath "..")).ProviderPath
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($GameRootPath)) {
    $GameRootPath = Join-Path $repoRoot "tools/fixtures/data_index/$ProfileId/game"
}

if ([string]::IsNullOrWhiteSpace($ModPath)) {
    $candidateModRoot = Join-Path $repoRoot "tools/fixtures/data_index/$ProfileId/mod"
    if (Test-Path -Path $candidateModRoot) {
        $ModPath = $candidateModRoot
    }
}

if (-not (Test-Path -Path $GameRootPath)) {
    throw "GameRootPath does not exist: $GameRootPath"
}

$dataIndexAssembly = Join-Path $repoRoot "src/SwfocTrainer.DataIndex/bin/Release/net8.0/SwfocTrainer.DataIndex.dll"
$megAssembly = Join-Path $repoRoot "src/SwfocTrainer.Meg/bin/Release/net8.0/SwfocTrainer.Meg.dll"
if (-not (Test-Path -Path $dataIndexAssembly) -or -not (Test-Path -Path $megAssembly)) {
    dotnet build SwfocTrainer.sln -c Release --no-restore | Out-Null
}

Add-Type -Path $megAssembly
Add-Type -Path $dataIndexAssembly

$service = [SwfocTrainer.DataIndex.Services.EffectiveGameDataIndexService]::new()
$request = if ([string]::IsNullOrWhiteSpace($ModPath)) {
    [SwfocTrainer.DataIndex.Models.EffectiveGameDataIndexRequest]::new($ProfileId, $GameRootPath)
}
else {
    [SwfocTrainer.DataIndex.Models.EffectiveGameDataIndexRequest]::new($ProfileId, $GameRootPath, $ModPath)
}

$report = $service.Build($request)
if ($Strict -and $report.Diagnostics.Count -gt 0) {
    throw ("Strict mode failed; diagnostics: " + (($report.Diagnostics -join "; ")))
}

$outDirectory = Split-Path -Parent $OutPath
if (-not [string]::IsNullOrWhiteSpace($outDirectory) -and -not (Test-Path -Path $outDirectory)) {
    New-Item -Path $outDirectory -ItemType Directory | Out-Null
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $OutPath
Write-Output "effective-data-index exported to $OutPath"
