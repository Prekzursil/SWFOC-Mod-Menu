param(
    [Parameter(Mandatory = $true)][string]$SeedPath,
    [string]$OutputRoot = "profiles/custom",
    [string]$NamespaceRoot = "custom",
    [string]$FallbackBaseProfileId = "base_swfoc",
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $repoRoot

if (-not (Test-Path -Path $SeedPath)) {
    throw "Seed file not found: $SeedPath"
}

function Normalize-Token {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $normalized = $Value.Trim().ToLowerInvariant() -replace '[^a-z0-9]+', '_'
    while ($normalized.Contains("__")) {
        $normalized = $normalized.Replace("__", "_")
    }

    return $normalized.Trim('_')
}

function Resolve-SaveSchemaId {
    param([string]$BaseProfileId)

    if ($BaseProfileId -like "*sweaw*") {
        return "base_sweaw_steam_v1"
    }

    return "base_swfoc_steam_v1"
}

$seedPayload = Get-Content -Raw -Path $SeedPath | ConvertFrom-Json
$seeds = @($seedPayload.seeds)
if ($seeds.Count -eq 0) {
    throw "No seed entries found in '$SeedPath'."
}

$profilesRoot = Join-Path $OutputRoot "profiles"
New-Item -ItemType Directory -Path $profilesRoot -Force | Out-Null

$results = New-Object System.Collections.Generic.List[object]
foreach ($seed in $seeds) {
    $workshopId = [string]$seed.workshopId
    if ([string]::IsNullOrWhiteSpace($workshopId)) {
        continue
    }

    $title = [string]$seed.title
    $titleToken = Normalize-Token -Value $title
    if ([string]::IsNullOrWhiteSpace($titleToken)) {
        $titleToken = "mod"
    }

    $profileId = "custom_{0}_{1}_swfoc" -f $titleToken, $workshopId
    if ($profileId.Length -gt 96) {
        $profileId = $profileId.Substring(0, 96).TrimEnd('_')
    }

    $baseProfile = [string]$seed.candidateBaseProfile
    if ([string]::IsNullOrWhiteSpace($baseProfile)) {
        $baseProfile = $FallbackBaseProfileId
    }

    $outputPath = Join-Path $profilesRoot "$profileId.json"
    if ((Test-Path -Path $outputPath) -and -not $Force) {
        $results.Add([PSCustomObject]@{
            profileId = $profileId
            outputPath = $outputPath
            status = "skipped_existing"
        })
        continue
    }

    $aliases = New-Object System.Collections.Generic.List[string]
    $aliases.Add($profileId)
    if (-not [string]::IsNullOrWhiteSpace($titleToken)) {
        $aliases.Add($titleToken)
    }

    $modPathHints = @($seed.launchHints.modPathHints | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $steamModIds = @($seed.launchHints.steamModIds | ForEach-Object { [string]$_ } | Where-Object { $_ -match '^[0-9]{4,}$' })
    if ($steamModIds -notcontains $workshopId) {
        $steamModIds = @($workshopId) + $steamModIds
    }

    $metadata = [ordered]@{
        origin = "auto_discovery"
        sourceRunId = [string]$seed.sourceRunId
        confidence = [string]$seed.confidence
        parentProfile = $baseProfile
        profileAliases = ($aliases | Select-Object -Unique) -join ","
        localPathHints = ($modPathHints | Select-Object -Unique) -join ","
        requiredWorkshopIds = ($steamModIds | Select-Object -Unique) -join ","
    }

    $profile = [ordered]@{
        id = $profileId
        displayName = $title
        inherits = $baseProfile
        exeTarget = "Swfoc"
        steamWorkshopId = $workshopId
        backendPreference = "auto"
        requiredCapabilities = @($seed.requiredCapabilities)
        hostPreference = "starwarsg_preferred"
        experimentalFeatures = @()
        signatureSets = @()
        fallbackOffsets = [ordered]@{}
        actions = [ordered]@{}
        featureFlags = [ordered]@{
            customModDraft = $true
        }
        catalogSources = @()
        saveSchemaId = Resolve-SaveSchemaId -BaseProfileId $baseProfile
        helperModHooks = @()
        metadata = $metadata
    }

    $profile | ConvertTo-Json -Depth 12 | Set-Content -Path $outputPath

    $results.Add([PSCustomObject]@{
        profileId = $profileId
        outputPath = $outputPath
        status = "generated"
    })
}

Write-Output "generated profiles: $(@($results | Where-Object { $_.status -eq 'generated' }).Count)"
Write-Output "profiles root: $(Resolve-Path $profilesRoot)"
