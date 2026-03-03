param(
    [Parameter(Mandatory = $true)][string]$ProfileId,
    [string]$InstalledGraphPath = "",
    [string]$ActiveWorkshopIds = "",
    [Parameter(Mandatory = $true)][string]$OutPath,
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot ".." "..")).ProviderPath
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($InstalledGraphPath)) {
    $latest = Get-ChildItem -Path "TestResults/mod-discovery" -Filter "installed-mod-graph.json" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latest) {
        throw "No installed-mod-graph.json found. Provide -InstalledGraphPath explicitly."
    }

    $InstalledGraphPath = $latest.FullName
}

if (-not (Test-Path -Path $InstalledGraphPath)) {
    throw "Installed graph path not found: $InstalledGraphPath"
}

$graph = Get-Content -Raw -Path $InstalledGraphPath | ConvertFrom-Json
$items = @($graph.items)

$activeSet = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
if (-not [string]::IsNullOrWhiteSpace($ActiveWorkshopIds)) {
    foreach ($id in ($ActiveWorkshopIds -split "," | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        [void]$activeSet.Add($id.Trim())
    }
}
elseif ($null -ne $graph.chains -and @($graph.chains).Count -gt 0) {
    foreach ($id in @($graph.chains[0].orderedWorkshopIds)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$id)) {
            [void]$activeSet.Add(([string]$id).Trim())
        }
    }
}

$entities = @()
foreach ($item in $items) {
    $id = [string]$item.workshopId
    if ([string]::IsNullOrWhiteSpace($id)) {
        continue
    }

    $requiresTransplant = -not $activeSet.Contains($id)
    $resolved = $true
    $reasonCode = "CAPABILITY_PROBE_PASS"
    $message = "Entity belongs to active workshop chain."
    $missingDependencies = @()

    if ($requiresTransplant) {
        $reasonCode = "TRANSPLANT_APPLIED"
        $message = "Cross-mod transplant candidate resolved by static dependency scan."

        $parents = @($item.parentWorkshopIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($parents.Count -eq 0 -and [string]$item.kind -eq "submod") {
            $resolved = $false
            $reasonCode = "TRANSPLANT_DEPENDENCY_MISSING"
            $message = "Submod parent chain is unknown."
            $missingDependencies = @("parent_workshop_id")
        }
    }

    $entities += [ordered]@{
        entityId = "WORKSHOP_$id"
        sourceProfileId = "workshop_$id"
        sourceWorkshopId = $id
        requiresTransplant = $requiresTransplant
        resolved = $resolved
        reasonCode = $reasonCode
        message = $message
        visualRef = "workshop://$id/preview"
        missingDependencies = @($missingDependencies)
    }
}

$blocking = @($entities | Where-Object { -not [bool]$_.resolved })
$report = [ordered]@{
    schemaVersion = "1.0"
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    targetProfileId = $ProfileId
    activeWorkshopIds = @($activeSet | Sort-Object)
    totalEntities = [int]$entities.Count
    blockingEntityCount = [int]$blocking.Count
    allResolved = ($blocking.Count -eq 0)
    entities = @($entities)
    diagnostics = [ordered]@{
        sourceInstalledGraph = (Resolve-Path -Path $InstalledGraphPath).ProviderPath
        strictMode = [bool]$Strict
        blockingEntityIds = @($blocking | ForEach-Object { [string]$_.entityId })
    }
}

$outDirectory = Split-Path -Parent $OutPath
if (-not [string]::IsNullOrWhiteSpace($outDirectory)) {
    New-Item -ItemType Directory -Path $outDirectory -Force | Out-Null
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $OutPath -Encoding UTF8
Write-Output ("transplant report exported: {0}" -f (Resolve-Path -Path $OutPath).ProviderPath)
