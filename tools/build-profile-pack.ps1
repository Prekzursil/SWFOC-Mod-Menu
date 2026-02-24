param(
    [Parameter(Mandatory = $true)][string]$ProfileId,
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$profilesDir = Join-Path $repoRoot "profiles/default/profiles"
$outDir = Join-Path $repoRoot "artifacts/profile-packs"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$profilePath = Join-Path $profilesDir "$ProfileId.json"
if (-not (Test-Path $profilePath)) {
    throw "Profile not found: $profilePath"
}

$packRoot = Join-Path $outDir "$ProfileId-$Version"
if (Test-Path $packRoot) {
    Remove-Item $packRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packRoot -Force | Out-Null

Copy-Item $profilePath (Join-Path $packRoot "$ProfileId.json") -Force

$zipPath = Join-Path $outDir "$ProfileId-$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path "$packRoot/*" -DestinationPath $zipPath -Force

$sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower()
Write-Output "Built: $zipPath"
Write-Output "SHA256: $sha"
