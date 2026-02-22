param(
    [Parameter(Mandatory = $true)][string]$FingerprintPath,
    [string]$ProfilePath,
    [string]$OutputPath,
    [string]$RunId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -Path $FingerprintPath)) {
    throw "Fingerprint file not found: $FingerprintPath"
}

$fingerprint = Get-Content -Raw -Path $FingerprintPath | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = if ($fingerprint.runId) { [string]$fingerprint.runId } else { (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss") }
}

$repoRoot = Resolve-Path (Join-Path (Join-Path $PSScriptRoot "..") "..")
if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
    $ProfilePath = Join-Path $repoRoot "profiles/default/profiles/base_swfoc.json"
}
if (-not (Test-Path -Path $ProfilePath)) {
    throw "Profile file not found: $ProfilePath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDir = Join-Path $repoRoot "TestResults/research/$RunId"
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    $OutputPath = Join-Path $outputDir "signature-pack.json"
} else {
    $outputDir = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$profile = Get-Content -Raw -Path $ProfilePath | ConvertFrom-Json
$signatureSets = @($profile.signatureSets)
$anchors = @()
foreach ($set in $signatureSets) {
    foreach ($sig in @($set.signatures)) {
        $anchors += [ordered]@{
            id = [string]$sig.name
            kind = "symbol_signature"
            pattern = [string]$sig.pattern
            required = $true
            notes = "from profile signature set: $($set.name)"
        }
    }
}

$anchorIds = @($anchors | ForEach-Object { $_.id } | Sort-Object -Unique)

$operations = [ordered]@{
    list_selected = [ordered]@{ requiredAnchors = @(); optionalAnchors = @("selected_hp", "selected_shield", "selected_owner_faction") }
    list_nearby = [ordered]@{ requiredAnchors = @(); optionalAnchors = @("selected_hp", "selected_shield", "selected_owner_faction") }
    spawn = [ordered]@{ requiredAnchors = @("spawn_point_write"); optionalAnchors = @("planet_owner") }
    kill = [ordered]@{ requiredAnchors = @("selected_hp"); optionalAnchors = @("selected_shield") }
    set_owner = [ordered]@{ requiredAnchors = @("selected_owner_faction"); optionalAnchors = @("planet_owner") }
    teleport = [ordered]@{ requiredAnchors = @("selected_speed"); optionalAnchors = @("selected_owner_faction") }
    set_planet_owner = [ordered]@{ requiredAnchors = @("planet_owner"); optionalAnchors = @() }
    set_hp = [ordered]@{ requiredAnchors = @("selected_hp"); optionalAnchors = @("selected_shield") }
    set_shield = [ordered]@{ requiredAnchors = @("selected_shield"); optionalAnchors = @("selected_hp") }
    set_cooldown = [ordered]@{ requiredAnchors = @("selected_cooldown_multiplier"); optionalAnchors = @("selected_speed") }
}

$result = [ordered]@{
    schemaVersion = "1.0"
    runId = $RunId
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    fingerprintId = [string]$fingerprint.fingerprintId
    defaultProfileId = [string]$profile.id
    sourceProfilePath = [System.IO.Path]::GetFullPath($ProfilePath)
    anchors = $anchors
    operations = $operations
}

($result | ConvertTo-Json -Depth 12) | Set-Content -Path $OutputPath -Encoding UTF8
Write-Output "Signature candidates written: $OutputPath"
