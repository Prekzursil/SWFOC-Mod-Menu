param(
    [Parameter(Mandatory = $true)][string]$SeedsPath,
    [string]$ProfilesRootPath = "profiles/custom",
    [string]$Namespace = "profiles",
    [string]$BaseProfilesPath = "profiles/default/profiles"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $repoRoot

function Convert-ToProfileSlug {
    param([string]$Value)

    $raw = [string]$Value
    $slug = $raw.ToLowerInvariant() -replace "[^a-z0-9]+", "-"
    $slug = $slug.Trim("-")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return "workshop-mod"
    }

    return $slug
}

function Get-StringList {
    param([object]$Value)

    $out = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($Value)) {
        $text = [string]$item
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            $out.Add($text)
        }
    }

    return @($out)
}

if (-not (Test-Path -Path $SeedsPath)) {
    throw "Generated seed file not found: $SeedsPath"
}

$payload = Get-Content -Raw -Path $SeedsPath | ConvertFrom-Json
$seeds = @($payload.seeds)
if ($seeds.Count -eq 0) {
    throw "Generated seed payload has no seeds: $SeedsPath"
}

$targetRoot = Join-Path $ProfilesRootPath $Namespace
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null

$baseRoot = Resolve-Path $BaseProfilesPath
$written = New-Object System.Collections.Generic.List[string]

foreach ($seed in $seeds) {
    $workshopId = [string]$seed.workshopId
    if ([string]::IsNullOrWhiteSpace($workshopId)) {
        continue
    }

    $title = [string]$seed.title
    $slug = Convert-ToProfileSlug -Value $title
    $profileId = "${slug}_${workshopId}_auto"
    $displayName = "$title ($workshopId)"

    $parentProfile = [string]$seed.candidateBaseProfile
    if ([string]::IsNullOrWhiteSpace($parentProfile)) {
        $parentProfile = "base_swfoc"
    }

    $baseProfilePath = Join-Path $baseRoot "$parentProfile.json"
    $baseProfile = $null
    if (Test-Path -Path $baseProfilePath) {
        $baseProfile = Get-Content -Raw -Path $baseProfilePath | ConvertFrom-Json
    }

    $requiredCapabilities = New-Object System.Collections.Generic.List[string]
    foreach ($cap in @(Get-StringList -Value $baseProfile.requiredCapabilities)) {
        if (-not $requiredCapabilities.Contains($cap)) {
            $requiredCapabilities.Add($cap)
        }
    }
    foreach ($cap in @(Get-StringList -Value $seed.requiredCapabilities)) {
        if (-not $requiredCapabilities.Contains($cap)) {
            $requiredCapabilities.Add($cap)
        }
    }

    $profile = [ordered]@{
        id = $profileId
        displayName = $displayName
        inherits = $parentProfile
        exeTarget = if ($null -ne $baseProfile) { [string]$baseProfile.exeTarget } else { "Swfoc" }
        steamWorkshopId = $workshopId
        backendPreference = if ($null -ne $baseProfile) { [string]$baseProfile.backendPreference } else { "auto" }
        requiredCapabilities = @($requiredCapabilities)
        hostPreference = if ($null -ne $baseProfile) { [string]$baseProfile.hostPreference } else { "starwarsg_preferred" }
        experimentalFeatures = @()
        signatureSets = @()
        fallbackOffsets = @{}
        actions = @{}
        featureFlags = [ordered]@{
            auto_discovery = $true
            allow_fog_patch_fallback = $false
            allow_unit_cap_patch_fallback = $false
            requires_calibration_before_mutation = $true
        }
        catalogSources = @()
        helperModHooks = @()
        metadata = [ordered]@{
            origin = "auto_discovery"
            sourceRunId = [string]$seed.sourceRunId
            confidence = [double]$seed.confidence
            parentProfile = $parentProfile
            profileLineage = "$parentProfile -> $profileId"
            riskLevel = [string]$seed.riskLevel
            parentDependencies = ([string[]](Get-StringList -Value $seed.parentDependencies)) -join ","
            launchHints = ([string[]](Get-StringList -Value $seed.launchHints)) -join ","
            anchorHints = ([string[]](Get-StringList -Value $seed.anchorHints)) -join ","
        }
    }

    if ($null -ne $baseProfile -and -not [string]::IsNullOrWhiteSpace([string]$baseProfile.saveSchemaId)) {
        $profile["saveSchemaId"] = [string]$baseProfile.saveSchemaId
    }

    $outputPath = Join-Path $targetRoot "$profileId.json"
    $json = $profile | ConvertTo-Json -Depth 50
    Set-Content -Path $outputPath -Value $json -Encoding utf8
    $written.Add($outputPath)
}

Write-Output "generated profile drafts: $($written.Count)"
foreach ($path in $written) {
    Write-Output " - $path"
}
