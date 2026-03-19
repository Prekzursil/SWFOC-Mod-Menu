param(
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "TestResults",
    [string]$ProfileRoot = "profiles/default",
    [string[]]$ForceWorkshopIds = @(),
    [string]$ForceProfileId = "",
    [switch]$AutoLaunch,
    [switch]$RunAllInstalledChainsDeep,
    [string]$GameRoot = "",
    [int]$LaunchWaitSeconds = 45,
    [int]$LaunchStabilizationSeconds = 8,
    [int]$LiveTestTimeoutSeconds = 600,
    [switch]$NoBuild,
    [string]$RunId = "",
    [ValidateSet("AOTR", "ROE", "TACTICAL", "FULL")][string]$Scope = "FULL",
    [bool]$EmitReproBundle = $true,
    [bool]$PreflightNativeHost = $true,
    [switch]$FailOnMissingArtifacts,
    [switch]$Strict,
    [switch]$RequireNonBlockedClassification
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $PSBoundParameters.ContainsKey("NoBuild")) {
    $NoBuild = $true
}
elseif ($NoBuild -and $RunAllInstalledChainsDeep) {
    Write-Warning "RunAllInstalledChainsDeep requires reliable test artifacts; overriding -NoBuild to false."
    $NoBuild = $false
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).ProviderPath
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
}

$runResultsDirectory = Join-Path $ResultsDirectory (Join-Path "runs" $RunId)
if (-not (Test-Path -Path $runResultsDirectory)) {
    New-Item -ItemType Directory -Path $runResultsDirectory -Force | Out-Null
}
$runResultsDirectory = (Resolve-Path -Path $runResultsDirectory).ProviderPath
$modDiscoveryDirectory = Join-Path $ResultsDirectory (Join-Path "mod-discovery" $RunId)
if (-not (Test-Path -Path $modDiscoveryDirectory)) {
    New-Item -ItemType Directory -Path $modDiscoveryDirectory -Force | Out-Null
}
$modDiscoveryDirectory = (Resolve-Path -Path $modDiscoveryDirectory).ProviderPath

$defaultAotrWorkshopChain = @("1397421866")
$defaultRoeWorkshopChain = @("1397421866", "3447786229")
$gameRootCandidates = @(
    "D:\\SteamLibrary\\steamapps\\common\\Star Wars Empire at War",
    "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Star Wars Empire at War"
)

function ConvertTo-ForcedWorkshopIds {
    param([string[]]$RawIds)

    $ids = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $ordered = New-Object System.Collections.Generic.List[string]
    foreach ($raw in $RawIds) {
        if ([string]::IsNullOrWhiteSpace($raw)) {
            continue
        }

        foreach ($token in ([string]$raw -split ",")) {
            $value = [string]$token
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $trimmed = $value.Trim()
                if ($ids.Add($trimmed)) {
                    [void]$ordered.Add($trimmed)
                }
            }
        }
    }

    return @($ordered)
}

function Resolve-DotnetCommand {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnet) {
        return $dotnet.Source
    }

    $candidates = @(
        (Join-Path $env:USERPROFILE ".dotnet\\dotnet.exe"),
        (Join-Path $env:ProgramFiles "dotnet\\dotnet.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -Path $candidate) {
            return $candidate
        }
    }

    throw "Could not resolve dotnet executable. Install .NET SDK or add dotnet to PATH."
}

function Resolve-GameRootPath {
    param([string]$OverrideRoot)

    if (-not [string]::IsNullOrWhiteSpace($OverrideRoot) -and (Test-Path -Path $OverrideRoot)) {
        return (Resolve-Path -Path $OverrideRoot).ProviderPath
    }

    $envRoot = $env:SWFOC_GAME_ROOT
    if (-not [string]::IsNullOrWhiteSpace($envRoot) -and (Test-Path -Path $envRoot)) {
        return (Resolve-Path -Path $envRoot).ProviderPath
    }

    foreach ($candidate in $gameRootCandidates) {
        if (Test-Path -Path $candidate) {
            return (Resolve-Path -Path $candidate).ProviderPath
        }
    }

    return ""
}

function Export-InstalledModGraph {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [string]$AppId = "32470"
    )

    $manifestCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:SWFOC_WORKSHOP_MANIFEST_PATH)) {
        $manifestCandidates += $env:SWFOC_WORKSHOP_MANIFEST_PATH
    }
    $manifestCandidates += @(
        "D:\\SteamLibrary\\steamapps\\workshop\\appworkshop_$AppId.acf",
        "C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\appworkshop_$AppId.acf"
    )

    $workshopRootCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:SWFOC_WORKSHOP_CONTENT_ROOT)) {
        $workshopRootCandidates += $env:SWFOC_WORKSHOP_CONTENT_ROOT
    }
    $workshopRootCandidates += @(
        "D:\\SteamLibrary\\steamapps\\workshop\\content\\$AppId",
        "C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\$AppId"
    )

    $installed = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $manifestUsed = ""
    foreach ($candidate in $manifestCandidates) {
        if (-not (Test-Path -Path $candidate)) {
            continue
        }

        $manifestUsed = (Resolve-Path -Path $candidate).ProviderPath
        $content = Get-Content -Raw -Path $candidate
        $matchCollection = [regex]::Matches($content, '"(?<id>\d{4,})"\s*\{')
        foreach ($match in $matchCollection) {
            $id = [string]$match.Groups["id"].Value
            if (-not [string]::IsNullOrWhiteSpace($id)) {
                [void]$installed.Add($id)
            }
        }
        break
    }

    foreach ($rootCandidate in $workshopRootCandidates) {
        if (-not (Test-Path -Path $rootCandidate)) {
            continue
        }

        foreach ($dir in (Get-ChildItem -Directory -Path $rootCandidate -ErrorAction SilentlyContinue)) {
            if ($dir.Name -match "^[0-9]{4,}$") {
                [void]$installed.Add($dir.Name)
            }
        }
    }

    $ids = @($installed | Sort-Object)
    $items = @()
    $diagnostics = @()
    if (-not [string]::IsNullOrWhiteSpace($manifestUsed)) {
        $diagnostics += "manifest=$manifestUsed"
    }
    $diagnostics += "installedCount=$($ids.Count)"

    if ($ids.Count -gt 0) {
        for ($index = 0; $index -lt $ids.Count; $index += 100) {
            $batch = @($ids[$index..([Math]::Min($index + 99, $ids.Count - 1))])
            $body = @{ itemcount = [string]$batch.Count }
            for ($i = 0; $i -lt $batch.Count; $i++) {
                $body["publishedfileids[$i]"] = $batch[$i]
            }

            try {
                $response = Invoke-RestMethod -Method Post -Uri "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/" -Body $body -ContentType "application/x-www-form-urlencoded" -TimeoutSec 20
                $details = @($response.response.publishedfiledetails)
                foreach ($detail in $details) {
                    if ($null -eq $detail) {
                        continue
                    }

                    $id = [string]$detail.publishedfileid
                    if ([string]::IsNullOrWhiteSpace($id)) {
                        continue
                    }

                    $title = if ([string]::IsNullOrWhiteSpace([string]$detail.title)) { "Workshop Item $id" } else { [string]$detail.title }
                    $description = ""
                    if ($null -ne $detail.PSObject.Properties["file_description"]) {
                        $description = [string]$detail.file_description
                    }
                    elseif ($null -ne $detail.PSObject.Properties["description"]) {
                        $description = [string]$detail.description
                    }
                    $parents = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
                    foreach ($match in [regex]::Matches($description, "STEAMMOD\s*=\s*(?<id>\d{4,})", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                        $parentId = [string]$match.Groups["id"].Value
                        if (-not [string]::IsNullOrWhiteSpace($parentId) -and $parentId -ne $id) {
                            [void]$parents.Add($parentId)
                        }
                    }

                    $tags = @()
                    foreach ($tag in @($detail.tags)) {
                        $tagValue = if ($null -ne $tag.tag) { [string]$tag.tag } else { [string]$tag }
                        if (-not [string]::IsNullOrWhiteSpace($tagValue)) {
                            $tags += $tagValue
                        }
                    }

                    $kind = "mod"
                    $reason = "independent_mod"
                    if ($parents.Count -gt 0) {
                        $kind = "submod"
                        $reason = "parent_dependency"
                    }
                    elseif ($title.ToLowerInvariant().Contains("submod") -or $description.ToLowerInvariant().Contains("submod")) {
                        $kind = "submod"
                        $reason = "keyword_submod_unknown_parent"
                    }

                    $items += [ordered]@{
                        workshopId = $id
                        title = $title
                        kind = $kind
                        parentWorkshopIds = @($parents | Sort-Object)
                        tags = @($tags | Sort-Object -Unique)
                        classificationReason = $reason
                    }
                }
            }
            catch {
                $diagnostics += "details_fetch_failed_batch=$index message=$($_.Exception.Message)"
            }
        }
    }

    $knownIds = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $items) {
        [void]$knownIds.Add([string]$item.workshopId)
    }
    foreach ($id in $ids) {
        if (-not $knownIds.Contains($id)) {
            $items += [ordered]@{
                workshopId = $id
                title = "Workshop Item $id"
                kind = "unknown"
                parentWorkshopIds = @()
                tags = @()
                classificationReason = "metadata_missing"
            }
        }
    }

    $chains = Resolve-LaunchChainsFromItems -Items @($items)

    $output = [ordered]@{
        schemaVersion = "1.0"
        appId = $AppId
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        items = @($items | Sort-Object workshopId)
        chains = @($chains)
        diagnostics = $diagnostics
    }

    $output | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8
    return (Resolve-ArtifactPath -Path $OutputPath)
}

function Resolve-LaunchChainsFromItems {
    param([object[]]$Items)

    $orderedItems = @($Items | Sort-Object { [string]$_.workshopId })
    if ($orderedItems.Count -eq 0) {
        return @()
    }

    $itemById = @{}
    foreach ($item in $orderedItems) {
        $id = [string]$item.workshopId
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            $itemById[$id] = $item
        }
    }

    $chains = New-Object System.Collections.Generic.List[object]
    $seen = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)

    foreach ($item in $orderedItems) {
        $id = [string]$item.workshopId
        if ([string]::IsNullOrWhiteSpace($id)) {
            continue
        }

        $parents = @($item.parentWorkshopIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | ForEach-Object { [string]$_ })
        if ($parents.Count -eq 0) {
            $ordered = @($id)
            $chainId = ($ordered -join ">")
            if ($seen.Add($chainId)) {
                $chains.Add([ordered]@{
                    chainId = $chainId
                    orderedWorkshopIds = $ordered
                    classificationReason = [string]$item.classificationReason
                    parentFirst = $true
                    missingParentIds = @()
                })
            }
            continue
        }

        $resolvedParents = @($parents | Where-Object { $itemById.ContainsKey([string]$_) })
        $missingParents = @($parents | Where-Object { -not $itemById.ContainsKey([string]$_) })

        if ($resolvedParents.Count -eq 0) {
            $ordered = @($id)
            $chainId = ($ordered -join ">")
            if ($seen.Add($chainId)) {
                $chains.Add([ordered]@{
                    chainId = $chainId
                    orderedWorkshopIds = $ordered
                    classificationReason = if ($missingParents.Count -gt 0) { "parent_dependency_missing" } else { [string]$item.classificationReason }
                    parentFirst = $true
                    missingParentIds = @($missingParents | Sort-Object -Unique)
                })
            }
            continue
        }

        foreach ($parentId in $resolvedParents) {
            $visited = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
            $ordered = New-Object System.Collections.Generic.List[string]

            function Add-Ancestors {
                param(
                    [string]$CurrentId,
                    [hashtable]$Map,
                    [System.Collections.Generic.HashSet[string]]$Visited,
                    [System.Collections.Generic.List[string]]$Ordered
                )

                if ([string]::IsNullOrWhiteSpace($CurrentId) -or -not $Map.ContainsKey($CurrentId)) {
                    return
                }

                if (-not $Visited.Add($CurrentId)) {
                    return
                }

                $current = $Map[$CurrentId]
                foreach ($ancestorId in @($current.parentWorkshopIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | ForEach-Object { [string]$_ })) {
                    Add-Ancestors -CurrentId $ancestorId -Map $Map -Visited $Visited -Ordered $Ordered
                }

                $Ordered.Add($CurrentId)
            }

            Add-Ancestors -CurrentId $parentId -Map $itemById -Visited $visited -Ordered $ordered
            if ($visited.Add($id)) {
                $ordered.Add($id)
            }

            $orderedArray = @($ordered)
            if ($orderedArray.Count -eq 0) {
                continue
            }

            $chainId = ($orderedArray -join ">")
            if ($seen.Add($chainId)) {
                $chains.Add([ordered]@{
                    chainId = $chainId
                    orderedWorkshopIds = $orderedArray
                    classificationReason = if ($missingParents.Count -gt 0) { "parent_dependency_partial_missing" } else { [string]$item.classificationReason }
                    parentFirst = $true
                    missingParentIds = @($missingParents | Sort-Object -Unique)
                })
            }
        }
    }

    return @($chains.ToArray())
}

function Export-ResolvedLaunchChains {
    param(
        [Parameter(Mandatory = $true)][string]$InstalledGraphPath,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    if (-not (Test-Path -Path $InstalledGraphPath)) {
        throw "Installed mod graph path not found: $InstalledGraphPath"
    }

    $graph = Get-Content -Raw -Path $InstalledGraphPath | ConvertFrom-Json
    $items = @($graph.items)
    $graphHasChains = ($null -ne $graph.chains -and @($graph.chains).Count -gt 0)
    $chainResolutionSource = if ($graphHasChains) { "items_recomputed_graph_validated" } else { "items_recomputed" }
    $existingMissingParentByChainId = @{}
    if ($graphHasChains) {
        foreach ($chain in @($graph.chains)) {
            $chainId = [string]$chain.chainId
            if ([string]::IsNullOrWhiteSpace($chainId)) {
                continue
            }

            $missing = @()
            if ($null -ne $chain.PSObject.Properties["missingParentIds"]) {
                $missing = @($chain.missingParentIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
            }

            $existingMissingParentByChainId[$chainId] = @($missing)
        }
    }

    $resolvedChains = @(Resolve-LaunchChainsFromItems -Items $items)
    $chains = @($resolvedChains | ForEach-Object {
        $chainId = [string]$_.chainId
        $missingParentIds = @()
        if ($null -ne $_.PSObject.Properties["missingParentIds"]) {
            $missingParentIds = @($_.missingParentIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        }
        if ($graphHasChains -and $existingMissingParentByChainId.ContainsKey($chainId)) {
            $missingParentIds = @($missingParentIds + @($existingMissingParentByChainId[$chainId]) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        }

        $classificationReason = [string]$_.classificationReason
        if ($missingParentIds.Count -gt 0 -and $classificationReason -notlike "parent_dependency*") {
            $classificationReason = "parent_dependency_missing"
        }

        [ordered]@{
            chainId = $chainId
            orderedWorkshopIds = @($_.orderedWorkshopIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            classificationReason = $classificationReason
            parentFirst = $true
            missingParentIds = @($missingParentIds)
            chainResolutionSource = $chainResolutionSource
        }
    })

    $output = [ordered]@{
        schemaVersion = "1.0"
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        sourceGraphPath = (Resolve-ArtifactPath -Path $InstalledGraphPath)
        chains = @($chains)
    }

    $output | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8
    return (Resolve-ArtifactPath -Path $OutputPath)
}

function Stop-LiveGameProcesses {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param()

    foreach ($name in @("sweaw", "swfoc", "StarWarsG")) {
        foreach ($proc in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
            try {
                if ($PSCmdlet.ShouldProcess("$($proc.ProcessName)#$($proc.Id)", "Stop-Process")) {
                    Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                }
            }
            catch {
                Write-Warning ("Failed stopping process {0} ({1}): {2}" -f $proc.ProcessName, $proc.Id, $_.Exception.Message)
            }
        }
    }
}

function Wait-ForAnyProcess {
    param(
        [string[]]$Names,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds([Math]::Max(5, $TimeoutSeconds))
    while ((Get-Date) -lt $deadline) {
        foreach ($name in $Names) {
            $match = Get-Process -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -ne $match) {
                return $match
            }
        }

        Start-Sleep -Milliseconds 750
    }

    return $null
}

function Resolve-ScopeLaunchPlan {
    param(
        [string]$SelectedScope,
        [string[]]$ForcedWorkshopIds,
        [string]$ForcedProfileId
    )

    $scopeUpper = $SelectedScope.ToUpperInvariant()
    $forcedIds = @($ForcedWorkshopIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $forceProfile = if ([string]::IsNullOrWhiteSpace($ForcedProfileId)) { "" } else { $ForcedProfileId.Trim().ToLowerInvariant() }

    $workshopIds = @()
    if ($scopeUpper -eq "AOTR") {
        $workshopIds = if ($forcedIds.Count -gt 0) { @($forcedIds) } else { @($defaultAotrWorkshopChain) }
    }
    elseif ($scopeUpper -eq "ROE") {
        $workshopIds = if ($forcedIds.Count -gt 0) { @($forcedIds) } else { @($defaultRoeWorkshopChain) }
    }
    elseif ($scopeUpper -eq "FULL") {
        if ($forceProfile.Contains("roe")) {
            $workshopIds = if ($forcedIds.Count -gt 0) { @($forcedIds) } else { @($defaultRoeWorkshopChain) }
        }
        elseif ($forceProfile.Contains("aotr")) {
            $workshopIds = if ($forcedIds.Count -gt 0) { @($forcedIds) } else { @($defaultAotrWorkshopChain) }
        }
        else {
            $workshopIds = @()
        }
    }

    if (($scopeUpper -eq "TACTICAL" -or $scopeUpper -eq "FULL") -and $workshopIds.Count -eq 0 -and $forcedIds.Count -gt 0) {
        $workshopIds = @($forcedIds)
    }

    $workshopIds = @($workshopIds)
    $requiredHostName = ""
    if ($scopeUpper -eq "ROE") {
        $requiredHostName = "StarWarsG"
    }
    elseif ($scopeUpper -eq "AOTR" -and @($workshopIds).Count -gt 0) {
        $requiredHostName = "StarWarsG"
    }
    elseif ($scopeUpper -eq "TACTICAL" -and @($workshopIds).Count -gt 0) {
        $requiredHostName = "StarWarsG"
    }
    elseif ($scopeUpper -eq "FULL" -and @($workshopIds).Count -gt 0) {
        $requiredHostName = "StarWarsG"
    }

    return [PSCustomObject]@{
        TargetExe = "swfoc.exe"
        HostNames = @("swfoc", "StarWarsG")
        WorkshopIds = @($workshopIds)
        RequiredHostName = $requiredHostName
    }
}

function Resolve-HelperOverlaySourcePath {
    param([string]$ProfileId)

    if ([string]::IsNullOrWhiteSpace($ProfileId)) {
        return ""
    }

    $candidate = Join-Path $env:LOCALAPPDATA (Join-Path "SwfocTrainer\helper_mod" $ProfileId.Trim())
    if (Test-Path -Path $candidate) {
        return (Resolve-Path -Path $candidate).ProviderPath
    }

    return ""
}

function Resolve-HelperOverlayMirrorPath {
    param(
        [string]$ProfileId,
        [string]$GameRoot
    )

    if ([string]::IsNullOrWhiteSpace($ProfileId) -or [string]::IsNullOrWhiteSpace($GameRoot)) {
        return ""
    }

    return (Join-Path $GameRoot (Join-Path "corruption\Mods\SwfocTrainer_Helper" $ProfileId.Trim()))
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $pathType = [System.IO.Path]
    $relativeMethod = $pathType.GetMethod("GetRelativePath", [System.Type[]]@([string], [string]))
    if ($null -ne $relativeMethod) {
        return $relativeMethod.Invoke($null, @($BasePath, $TargetPath))
    }

    $fullBasePath = [System.IO.Path]::GetFullPath($BasePath)
    if (-not $fullBasePath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fullBasePath += [System.IO.Path]::DirectorySeparatorChar
    }

    $fullTargetPath = [System.IO.Path]::GetFullPath($TargetPath)
    $baseUri = [System.Uri]::new($fullBasePath)
    $targetUri = [System.Uri]::new($fullTargetPath)
    return ([System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()) -replace '/', '\')
}

function Copy-DirectoryTree {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot
    )

    if (Test-Path -Path $DestinationRoot) {
        Remove-Item -Path $DestinationRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null

    Get-ChildItem -Path $SourceRoot -Recurse -Directory | ForEach-Object {
        $relative = Get-RelativePathCompat -BasePath $SourceRoot -TargetPath $_.FullName
        New-Item -ItemType Directory -Path (Join-Path $DestinationRoot $relative) -Force | Out-Null
    }

    Get-ChildItem -Path $SourceRoot -Recurse -File | ForEach-Object {
        $relative = Get-RelativePathCompat -BasePath $SourceRoot -TargetPath $_.FullName
        $destination = Join-Path $DestinationRoot $relative
        $destinationDirectory = Split-Path -Path $destination -Parent
        if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        Copy-Item -Path $_.FullName -Destination $destination -Force
    }
}

function Resolve-HelperOverlayModPath {
    param(
        [string]$ProfileId,
        [string]$GameRoot
    )

    $sourcePath = Resolve-HelperOverlaySourcePath -ProfileId $ProfileId
    if ([string]::IsNullOrWhiteSpace($sourcePath)) {
        return ""
    }

    $mirrorPath = Resolve-HelperOverlayMirrorPath -ProfileId $ProfileId -GameRoot $GameRoot
    if ([string]::IsNullOrWhiteSpace($mirrorPath)) {
        return $sourcePath
    }

    Copy-DirectoryTree -SourceRoot $sourcePath -DestinationRoot $mirrorPath
    return (Resolve-Path -Path $mirrorPath).ProviderPath
}

function Normalize-HelperOverlayLaunchPath {
    param(
        [string]$OverlayModPath,
        [string]$GameRoot
    )

    if ([string]::IsNullOrWhiteSpace($OverlayModPath)) {
        return ""
    }

    if ([string]::IsNullOrWhiteSpace($GameRoot) -or -not [System.IO.Path]::IsPathRooted($OverlayModPath)) {
        return $OverlayModPath
    }

    $modsRoot = [System.IO.Path]::GetFullPath((Join-Path $GameRoot "corruption\Mods"))
    $fullOverlayPath = [System.IO.Path]::GetFullPath($OverlayModPath)
    if (-not $fullOverlayPath.StartsWith($modsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullOverlayPath
    }

    $launchDirectory = Join-Path $GameRoot "corruption"
    return ((Get-RelativePathCompat -BasePath $launchDirectory -TargetPath $fullOverlayPath) -replace '/', '\')
}

function Start-AutoLaunchSession {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [string]$SelectedScope,
        [string[]]$ForcedWorkshopIds,
        [string]$ForcedProfileId,
        [string]$OverrideGameRoot,
        [int]$TimeoutSeconds = 45,
        [int]$StabilizationSeconds = 8
    )

    $root = Resolve-GameRootPath -OverrideRoot $OverrideGameRoot
    if ([string]::IsNullOrWhiteSpace($root)) {
        throw "Auto-launch requested but no game root was found. Provide -GameRoot or set SWFOC_GAME_ROOT."
    }

    $plan = Resolve-ScopeLaunchPlan -SelectedScope $SelectedScope -ForcedWorkshopIds $ForcedWorkshopIds -ForcedProfileId $ForcedProfileId
    $exePath = Join-Path $root ("corruption\" + $plan.TargetExe)
    if (-not (Test-Path -Path $exePath)) {
        throw ("Auto-launch executable missing: {0}" -f $exePath)
    }

    if ($PSCmdlet.ShouldProcess("live game processes", "Stop existing processes before auto-launch")) {
        Stop-LiveGameProcesses -Confirm:$false
    }

    $launchArgsParts = New-Object System.Collections.Generic.List[string]
    $overlayModPath = Resolve-HelperOverlayModPath -ProfileId $ForcedProfileId -GameRoot $root
    if ($null -ne $plan.WorkshopIds) {
        $normalizedWorkshopIds = @($plan.WorkshopIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($normalizedWorkshopIds.Count -gt 0) {
            foreach ($workshopId in $normalizedWorkshopIds) {
                [void]$launchArgsParts.Add("STEAMMOD=$workshopId")
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($overlayModPath)) {
        $launchOverlayPath = Normalize-HelperOverlayLaunchPath -OverlayModPath $overlayModPath -GameRoot $root
        [void]$launchArgsParts.Add(("MODPATH=""{0}""" -f $launchOverlayPath))
    }
    $launchArgs = ($launchArgsParts.ToArray() -join " ")
    Write-Output ("Auto-launch: exe='{0}' args='{1}' root='{2}' scope={3}" -f $exePath, $launchArgs, $root, $SelectedScope)
    $startParams = @{
        FilePath = $exePath
        WorkingDirectory = (Split-Path -Path $exePath -Parent)
        PassThru = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($launchArgs)) {
        $startParams.ArgumentList = $launchArgs
    }

    if (-not $PSCmdlet.ShouldProcess($exePath, ("Start process with args '{0}'" -f $launchArgs))) {
        throw "Auto-launch aborted by ShouldProcess."
    }

    $started = Start-Process @startParams
    if ($null -eq $started) {
        throw "Auto-launch failed: Start-Process returned null."
    }

    $visible = Wait-ForAnyProcess -Names $plan.HostNames -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $visible) {
        throw ("Auto-launch timeout: no target process visible after {0}s (started pid={1})." -f $TimeoutSeconds, $started.Id)
    }

    Write-Output ("Auto-launch process visible: {0} (pid={1})" -f $visible.ProcessName, $visible.Id)

    if (-not [string]::IsNullOrWhiteSpace([string]$plan.RequiredHostName)) {
        $requiredVisible = Wait-ForAnyProcess -Names @($plan.RequiredHostName) -TimeoutSeconds $TimeoutSeconds
        if ($null -eq $requiredVisible) {
            throw ("Auto-launch timeout: required host '{0}' not visible after {1}s (started pid={2})." -f $plan.RequiredHostName, $TimeoutSeconds, $started.Id)
        }

        Write-Output ("Auto-launch required host visible: {0} (pid={1})" -f $requiredVisible.ProcessName, $requiredVisible.Id)
    }

    if ($StabilizationSeconds -gt 0) {
        Write-Output ("Auto-launch stabilization wait: {0}s" -f $StabilizationSeconds)
        Start-Sleep -Seconds $StabilizationSeconds
    }

    $postLaunchVisible = Wait-ForAnyProcess -Names $plan.HostNames -TimeoutSeconds 3
    if ($null -eq $postLaunchVisible) {
        throw "Auto-launch failed: target process exited during stabilization window."
    }

    Write-Output ("Auto-launch post-stabilization host: {0} (pid={1})" -f $postLaunchVisible.ProcessName, $postLaunchVisible.Id)
}

function Get-LastExitCodeOrZero {
    if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) {
        return [int]$global:LASTEXITCODE
    }

    return 0
}

function New-LiveTestSummary {
    param(
        [Parameter(Mandatory = $true)][string]$Trx,
        [Parameter(Mandatory = $true)][string]$Outcome,
        [int]$Passed = 0,
        [int]$Failed = 0,
        [int]$Skipped = 0,
        [string]$Message = ""
    )

    return [PSCustomObject]@{
        Trx = $Trx
        Outcome = $Outcome
        Passed = $Passed
        Failed = $Failed
        Skipped = $Skipped
        Message = $Message
    }
}

function Get-LiveTestEntry {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Generic.List[object]]$Summaries,
        [Parameter(Mandatory = $true)][string]$TestName
    )

    return ($Summaries | Where-Object { $_.Name -eq $TestName } | Select-Object -First 1)
}

function Get-GalacticContextPreSkipMessage {
    param([Parameter(Mandatory = $true)][string]$TestName)

    switch ($TestName) {
        "LiveHeroHelperWorkflowTests" {
            return "hero helper precondition unmet: service wrapper did not observe a galactic/campaign script load. Enter galactic/campaign context and retry."
        }
        "LiveRoeRuntimeHealthTests" {
            return "set_credits precondition unmet: hook sync tick not observed. Enter galactic/campaign context and retry."
        }
        default {
            return ""
        }
    }
}

function Test-ShouldPreSkipForTacticalContext {
    param(
        [Parameter(Mandatory = $true)][System.Collections.Generic.List[object]]$Summaries,
        [Parameter(Mandatory = $true)][string]$TestName,
        [bool]$AutoLaunchEnabled = $false
    )

    if (-not $AutoLaunchEnabled) {
        return $null
    }

    $message = Get-GalacticContextPreSkipMessage -TestName $TestName
    if ([string]::IsNullOrWhiteSpace($message)) {
        return $null
    }

    $tacticalEntry = Get-LiveTestEntry -Summaries $Summaries -TestName "LiveTacticalToggleWorkflowTests"
    if ($null -eq $tacticalEntry -or $null -eq $tacticalEntry.Summary) {
        return $null
    }

    if ([string]$tacticalEntry.Summary.Outcome -ne "Passed") {
        return $null
    }

    return $message
}

function Invoke-CapturedCommand {
    param(
        [string[]]$Command,
        [string[]]$Arguments = @()
    )

    if ($Command.Count -eq 0 -or [string]::IsNullOrWhiteSpace($Command[0])) {
        return [PSCustomObject]@{
            ExitCode = 1
            Output = ""
        }
    }

    $invocationArgs = @()
    if ($Command.Count -gt 1) {
        $invocationArgs += $Command[1..($Command.Count - 1)]
    }
    $invocationArgs += $Arguments

    if (Test-IsWslPythonCommand -Command $Command) {
        $processArgs = @()
        foreach ($arg in $invocationArgs) {
            $argText = [string]$arg
            if ($argText.Contains('"')) {
                $argText = $argText.Replace('"', '\"')
            }

            if ($argText.Contains(" ") -or $argText.Contains("`t")) {
                $argText = '"' + $argText + '"'
            }

            $processArgs += $argText
        }

        $stdoutPath = [System.IO.Path]::GetTempFileName()
        $stderrPath = [System.IO.Path]::GetTempFileName()
        try {
            $proc = Start-Process `
                -FilePath $Command[0] `
                -ArgumentList $processArgs `
                -Wait `
                -NoNewWindow `
                -PassThru `
                -RedirectStandardOutput $stdoutPath `
                -RedirectStandardError $stderrPath

            $stdout = if (Test-Path -Path $stdoutPath) { Get-Content -Raw -Path $stdoutPath } else { "" }
            $stderr = if (Test-Path -Path $stderrPath) { Get-Content -Raw -Path $stderrPath } else { "" }
            $combined = $stdout
            if (-not [string]::IsNullOrWhiteSpace($stderr)) {
                if (-not [string]::IsNullOrWhiteSpace($combined)) {
                    $combined += [Environment]::NewLine
                }
                $combined += $stderr
            }

            return [PSCustomObject]@{
                ExitCode = [int]$proc.ExitCode
                Output = $combined
            }
        }
        finally {
            Remove-Item -Path $stdoutPath -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $stderrPath -Force -ErrorAction SilentlyContinue
        }
    }

    try {
        $output = & $Command[0] @invocationArgs 2>&1
        return [PSCustomObject]@{
            ExitCode = Get-LastExitCodeOrZero
            Output = ($output | Out-String)
        }
    }
    catch {
        return [PSCustomObject]@{
            ExitCode = 1
            Output = $_.Exception.Message
        }
    }
}

function Test-PythonInterpreter {
    param([string[]]$Command)

    if ($Command.Count -eq 0 -or [string]::IsNullOrWhiteSpace($Command[0])) {
        return $false
    }

    $probeArgs = @("-c", "print('ok')")
    $captured = Invoke-CapturedCommand -Command $Command -Arguments $probeArgs
    if ([int]$captured.ExitCode -ne 0) {
        return $false
    }

    $text = [string]$captured.Output
    $text = $text.Trim()
    return -not [string]::IsNullOrWhiteSpace($text) -and $text -match "(^|\r?\n)ok(\r?\n|$)"
}

function Resolve-PythonCommand {
    $candidates = New-Object System.Collections.Generic.List[string[]]

    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -ne $python) {
        $candidates.Add(@($python.Source))
    }

    $python3 = Get-Command python3 -ErrorAction SilentlyContinue
    if ($null -ne $python3) {
        $candidates.Add(@($python3.Source))
    }

    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($null -ne $py) {
        $candidates.Add(@($py.Source, "-3"))
    }

    $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if ($null -eq $wsl) {
        $wsl = Get-Command wsl -ErrorAction SilentlyContinue
    }
    if ($null -ne $wsl) {
        $candidates.Add(@($wsl.Source, "-e", "python3"))
    }

    $pathCandidates = @(
        (Join-Path $env:SystemRoot "py.exe"),
        (Join-Path $env:LocalAppData "Programs\\Python\\Python312\\python.exe"),
        (Join-Path $env:LocalAppData "Programs\\Python\\Python311\\python.exe"),
        (Join-Path $env:LocalAppData "Programs\\Python\\Python310\\python.exe"),
        (Join-Path $env:ProgramFiles "Python312\\python.exe"),
        (Join-Path $env:ProgramFiles "Python311\\python.exe"),
        (Join-Path $env:ProgramFiles "Python310\\python.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Python312\\python.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Python311\\python.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Python310\\python.exe")
    )

    foreach ($candidate in $pathCandidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -Path $candidate) {
            if ($candidate.ToLowerInvariant().EndsWith("py.exe")) {
                $candidates.Add(@($candidate, "-3"))
                continue
            }

            $candidates.Add(@($candidate))
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-PythonInterpreter -Command $candidate) {
            return $candidate
        }
    }

    if ($null -ne $wsl) {
        return @($wsl.Source, "-e", "python3")
    }

    return @()
}

function Test-IsWslPythonCommand {
    param([string[]]$Command)

    if ($Command.Count -eq 0 -or [string]::IsNullOrWhiteSpace($Command[0])) {
        return $false
    }

    $name = [System.IO.Path]::GetFileName($Command[0])
    return $name.Equals("wsl.exe", [System.StringComparison]::OrdinalIgnoreCase) `
        -or $name.Equals("wsl", [System.StringComparison]::OrdinalIgnoreCase)
}

function Convert-ToWslPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ($Path.StartsWith("/", [System.StringComparison]::Ordinal)) {
        return $Path
    }

    $windowsPathMatch = [Regex]::Match($Path, '^(?<drive>[A-Za-z]):\\(?<rest>.*)$')
    if ($windowsPathMatch.Success) {
        $drive = $windowsPathMatch.Groups["drive"].Value.ToLowerInvariant()
        $rest = ($windowsPathMatch.Groups["rest"].Value -replace "\\", "/")
        if ([string]::IsNullOrWhiteSpace($rest)) {
            return "/mnt/$drive"
        }

        return "/mnt/$drive/$rest"
    }

    $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if ($null -eq $wsl) {
        $wsl = Get-Command wsl -ErrorAction SilentlyContinue
    }

    if ($null -eq $wsl) {
        throw "WSL command was requested for python fallback but wsl executable is not available."
    }

    $converted = & $wsl.Source -e wslpath -a $Path 2>$null
    $exitCode = Get-LastExitCodeOrZero
    if ($exitCode -ne 0) {
        throw "Failed to convert '$Path' into a WSL path using wslpath."
    }

    $text = ($converted | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "WSL path conversion produced no output for '$Path'."
    }

    return $text
}

function Test-NativeHostPreflight {
    param(
        [string]$Config,
        [bool]$Enabled
    )

    if (-not $Enabled) {
        return ""
    }

    $buildScript = Join-Path $PSScriptRoot "native/build-native.ps1"
    if (-not (Test-Path -Path $buildScript)) {
        throw "ATTACH_NATIVE_HOST_PRECHECK_FAILED: build-native script missing at '$buildScript'."
    }

    # Stop a previously launched host process to avoid runtime artifact lock
    # collisions when build-native.ps1 refreshes native/runtime/SwfocExtender.Host.exe.
    Get-Process -Name "SwfocExtender.Host" -ErrorAction SilentlyContinue | Stop-Process -Force

    $buildOutput = & $buildScript -Mode Windows -Configuration $Config
    $buildExitCode = Get-LastExitCodeOrZero
    if ($buildExitCode -ne 0) {
        throw "ATTACH_NATIVE_HOST_PRECHECK_FAILED: native host build exited with code $buildExitCode."
    }
    foreach ($line in @($buildOutput)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$line)) {
            Write-Information ([string]$line) -InformationAction Continue
        }
    }

    $runtimeHostPath = Join-Path $repoRoot "native/runtime/SwfocExtender.Host.exe"
    if (-not (Test-Path -Path $runtimeHostPath)) {
        throw "ATTACH_NATIVE_HOST_MISSING: expected host artifact not found at '$runtimeHostPath'."
    }

    return (Resolve-ArtifactPath -Path $runtimeHostPath)
}

function Test-RunSelection {
    param(
        [string[]]$Scopes,
        [string]$SelectedScope
    )

    if ($SelectedScope -eq "FULL") {
        return $true
    }

    return $Scopes -contains $SelectedScope
}

function Resolve-ArtifactPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $candidate = $Path
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path (Get-Location) $candidate
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function Invoke-LiveTest {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Filter,
        [Parameter(Mandatory = $true)][string]$TrxName,
        [string]$NativeHostPath = ""
    )

    Write-Information "=== Running $Name ===" -InformationAction Continue

    $existingTrxPaths = @(Get-ChildItem -Path $runResultsDirectory -Filter "*.trx" -File -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName })

    $dotnetArgs = @(
        "test",
        "tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj",
        "-c", $Configuration,
        "--filter", $Filter,
        "--logger", "trx;LogFileName=$TrxName",
        "--results-directory", $runResultsDirectory
    )

    if ($NoBuild) {
        $dotnetArgs += "--no-build"
    }

    $previousOutputDir = $env:SWFOC_LIVE_OUTPUT_DIR
    $previousTestName = $env:SWFOC_LIVE_TEST_NAME
    $previousForcedWorkshopIds = $env:SWFOC_FORCE_WORKSHOP_IDS
    $previousForcedProfileId = $env:SWFOC_FORCE_PROFILE_ID
    $previousNativeHostPath = $env:SWFOC_EXTENDER_HOST_PATH
    $env:SWFOC_LIVE_OUTPUT_DIR = $runResultsDirectory
    $env:SWFOC_LIVE_TEST_NAME = $Name
    if ([string]::IsNullOrWhiteSpace($forceWorkshopIdsCsv)) {
        Remove-Item Env:SWFOC_FORCE_WORKSHOP_IDS -ErrorAction SilentlyContinue
    }
    else {
        $env:SWFOC_FORCE_WORKSHOP_IDS = $forceWorkshopIdsCsv
    }

    if ([string]::IsNullOrWhiteSpace($forceProfileIdNormalized)) {
        Remove-Item Env:SWFOC_FORCE_PROFILE_ID -ErrorAction SilentlyContinue
    }
    else {
        $env:SWFOC_FORCE_PROFILE_ID = $forceProfileIdNormalized
    }

    if ([string]::IsNullOrWhiteSpace($NativeHostPath)) {
        Remove-Item Env:SWFOC_EXTENDER_HOST_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:SWFOC_EXTENDER_HOST_PATH = $NativeHostPath
    }

    try {
        $dotnetProcess = Start-Process `
            -FilePath $dotnetExe `
            -ArgumentList $dotnetArgs `
            -WorkingDirectory $repoRoot `
            -NoNewWindow `
            -PassThru

        if ($null -eq $dotnetProcess) {
            throw "dotnet test failed for '$Name': process did not start."
        }

        $waitMilliseconds = [Math]::Max(1000, $LiveTestTimeoutSeconds * 1000)
        $completed = $dotnetProcess.WaitForExit($waitMilliseconds)
        if (-not $completed) {
            try {
                Stop-Process -Id $dotnetProcess.Id -Force -ErrorAction SilentlyContinue
            }
            catch {
                Write-Warning ("Failed terminating timed out dotnet test process {0}: {1}" -f $dotnetProcess.Id, $_.Exception.Message)
            }

            throw ("dotnet test timed out for '{0}' after {1}s" -f $Name, $LiveTestTimeoutSeconds)
        }

        $global:LASTEXITCODE = [int]$dotnetProcess.ExitCode
    }
    finally {
        if ($null -eq $previousOutputDir) {
            Remove-Item Env:SWFOC_LIVE_OUTPUT_DIR -ErrorAction SilentlyContinue
        }
        else {
            $env:SWFOC_LIVE_OUTPUT_DIR = $previousOutputDir
        }

        if ($null -eq $previousTestName) {
            Remove-Item Env:SWFOC_LIVE_TEST_NAME -ErrorAction SilentlyContinue
        }
        else {
            $env:SWFOC_LIVE_TEST_NAME = $previousTestName
        }

        if ($null -eq $previousForcedWorkshopIds) {
            Remove-Item Env:SWFOC_FORCE_WORKSHOP_IDS -ErrorAction SilentlyContinue
        }
        else {
            $env:SWFOC_FORCE_WORKSHOP_IDS = $previousForcedWorkshopIds
        }

        if ($null -eq $previousForcedProfileId) {
            Remove-Item Env:SWFOC_FORCE_PROFILE_ID -ErrorAction SilentlyContinue
        }
        else {
            $env:SWFOC_FORCE_PROFILE_ID = $previousForcedProfileId
        }

        if ($null -eq $previousNativeHostPath) {
            Remove-Item Env:SWFOC_EXTENDER_HOST_PATH -ErrorAction SilentlyContinue
        }
        else {
            $env:SWFOC_EXTENDER_HOST_PATH = $previousNativeHostPath
        }
    }

    $exitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "dotnet test failed for '$Name' with exit code $exitCode"
    }

    $expectedTrxPath = Resolve-ArtifactPath -Path (Join-Path $runResultsDirectory $TrxName)
    if (Test-Path -Path $expectedTrxPath) {
        return $expectedTrxPath
    }

    $newTrxCandidates = @(Get-ChildItem -Path $runResultsDirectory -Filter "*.trx" -File -ErrorAction SilentlyContinue |
        Where-Object { $existingTrxPaths -notcontains $_.FullName } |
        Sort-Object LastWriteTime -Descending)
    if ($newTrxCandidates.Count -gt 0) {
        return $newTrxCandidates[0].FullName
    }

    return $expectedTrxPath
}

function Read-TrxSummary {
    param([Parameter(Mandatory = $true)][string]$TrxPath)

    $resolvedTrxPath = Resolve-ArtifactPath -Path $TrxPath
    $trxDirectory = Split-Path -Path $resolvedTrxPath -Parent
    $trxLeafName = Split-Path -Path $resolvedTrxPath -Leaf
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    $lastReadError = ""
    $doc = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -Path $resolvedTrxPath) {
            try {
                [xml]$doc = Get-Content -Raw -Path $resolvedTrxPath
                break
            }
            catch {
                $lastReadError = $_.Exception.Message
            }
        }

        Start-Sleep -Milliseconds 250
    }

    if ($null -eq $doc -and (Test-Path -Path $trxDirectory)) {
        $fallbackCandidate = Get-ChildItem -Path $trxDirectory -Filter ("*-" + $trxLeafName) -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -ne $fallbackCandidate) {
            try {
                $resolvedTrxPath = $fallbackCandidate.FullName
                [xml]$doc = Get-Content -Raw -Path $resolvedTrxPath
            }
            catch {
                $lastReadError = $_.Exception.Message
            }
        }
    }

    if ($null -eq $doc) {
        $message = if ([string]::IsNullOrWhiteSpace($lastReadError)) {
            "TRX file not found"
        }
        else {
            "TRX file was not readable before timeout: $lastReadError"
        }
        return [PSCustomObject]@{
            Trx = $resolvedTrxPath
            Outcome = "Missing"
            Passed = 0
            Failed = 0
            Skipped = 0
            Message = $message
        }
    }
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")

    $counters = $doc.SelectSingleNode("//t:ResultSummary/t:Counters", $ns)
    $unitResult = $doc.SelectSingleNode("//t:UnitTestResult", $ns)
    $messageNode = $doc.SelectSingleNode("//t:UnitTestResult/t:Output/t:ErrorInfo/t:Message", $ns)

    $passed = 0
    $failed = 0
    $skipped = 0
    if ($null -ne $counters) {
        $passed = [int]$counters.passed
        $failed = [int]$counters.failed
        $skipped = [int]$counters.notExecuted
    }

    if ($null -ne $unitResult -and $unitResult.outcome -eq "NotExecuted" -and $passed -eq 0 -and $failed -eq 0 -and $skipped -eq 0) {
        $skipped = 1
    }

    $outcome = "Unknown"
    if ($failed -gt 0) {
        $outcome = "Failed"
    }
    elseif ($skipped -gt 0 -and $passed -eq 0) {
        $outcome = "Skipped"
    }
    elseif ($passed -gt 0 -and $failed -eq 0) {
        $outcome = "Passed"
    }

    return [PSCustomObject]@{
        Trx = $resolvedTrxPath
        Outcome = $outcome
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
        Message = if ($null -ne $messageNode) { $messageNode.InnerText } else { "" }
    }
}

function Resolve-EffectiveForceProfileId {
    param(
        [string]$SelectedScope,
        [string]$CurrentProfileId,
        [string[]]$ForcedWorkshopIds
    )

    if (-not [string]::IsNullOrWhiteSpace($CurrentProfileId)) {
        return $CurrentProfileId.Trim()
    }

    $scopeUpper = if ([string]::IsNullOrWhiteSpace($SelectedScope)) { "" } else { $SelectedScope.Trim().ToUpperInvariant() }
    switch ($scopeUpper) {
        "ROE" { return "roe_3447786229_swfoc" }
        "AOTR" { return "aotr_1397421866_swfoc" }
    }

    $forcedIds = @($ForcedWorkshopIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $forceChainKey = ($forcedIds -join ",")
    if ($forceChainKey -eq ($defaultRoeWorkshopChain -join ",")) {
        return "roe_3447786229_swfoc"
    }

    if ($forceChainKey -eq ($defaultAotrWorkshopChain -join ",")) {
        return "aotr_1397421866_swfoc"
    }

    return ""
}

$forceWorkshopIdsNormalized = @(ConvertTo-ForcedWorkshopIds -RawIds $ForceWorkshopIds)
$forceWorkshopIdsCsv = if ($forceWorkshopIdsNormalized.Count -eq 0) { "" } else { ($forceWorkshopIdsNormalized -join ",") }
$forceProfileIdNormalized = Resolve-EffectiveForceProfileId `
    -SelectedScope $Scope `
    -CurrentProfileId $ForceProfileId `
    -ForcedWorkshopIds $forceWorkshopIdsNormalized

$dotnetExe = Resolve-DotnetCommand
$pythonCmd = @(Resolve-PythonCommand)
$runtimeHostPath = Test-NativeHostPreflight -Config $Configuration -Enabled $PreflightNativeHost
$installedModGraphPath = Join-Path $modDiscoveryDirectory "installed-mod-graph.json"
$resolvedLaunchChainsPath = Join-Path $modDiscoveryDirectory "resolved-launch-chains.json"
$resolvedLaunchChains = @()
try {
    $resolvedInstalledModGraphPath = Export-InstalledModGraph -OutputPath $installedModGraphPath
    Write-Output ("Installed workshop graph: {0}" -f $resolvedInstalledModGraphPath)
    $resolvedLaunchChainsPath = Export-ResolvedLaunchChains `
        -InstalledGraphPath $resolvedInstalledModGraphPath `
        -OutputPath $resolvedLaunchChainsPath
    Write-Output ("Resolved launch chains: {0}" -f $resolvedLaunchChainsPath)
    $chainDoc = Get-Content -Raw -Path $resolvedLaunchChainsPath | ConvertFrom-Json
    $resolvedLaunchChains = @($chainDoc.chains)
}
catch {
    Write-Warning ("Installed workshop graph export failed: {0}" -f $_.Exception.Message)
}

$forcedChainMissingParentIds = @()
$forcedChainResolutionSource = "forced_workshop_ids"
if ($forceWorkshopIdsNormalized.Count -gt 0 -and $resolvedLaunchChains.Count -gt 0) {
    $forceChainKey = $forceWorkshopIdsNormalized -join ","
    $forcedChainCandidate = @(
        $resolvedLaunchChains |
            Where-Object {
                $chainIds = @($_.orderedWorkshopIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                (($chainIds -join ",") -eq $forceChainKey)
            } |
            Select-Object -First 1
    )

    if ($forcedChainCandidate.Count -eq 0) {
        $forcedChainCandidate = @(
            $resolvedLaunchChains |
                Where-Object {
                    $chainIds = @($_.orderedWorkshopIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
                    $matched = @($forceWorkshopIdsNormalized | Where-Object { $chainIds -contains $_ })
                    $matched.Count -eq $forceWorkshopIdsNormalized.Count
                } |
                Sort-Object {
                    @($_.orderedWorkshopIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
                } |
                Select-Object -First 1
        )
    }

    if ($forcedChainCandidate.Count -gt 0 -and $null -ne $forcedChainCandidate[0].PSObject.Properties["missingParentIds"]) {
        $forcedChainMissingParentIds = @(
            $forcedChainCandidate[0].missingParentIds |
                ForEach-Object { [string]$_ } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Sort-Object -Unique
        )
    }
    if ($forcedChainCandidate.Count -gt 0 -and $null -ne $forcedChainCandidate[0].PSObject.Properties["chainResolutionSource"]) {
        $forcedChainResolutionSource = [string]$forcedChainCandidate[0].chainResolutionSource
    }
}

if ($RunAllInstalledChainsDeep) {
    if ($resolvedLaunchChains.Count -eq 0) {
        throw "RunAllInstalledChainsDeep requested but no resolved launch chains were available."
    }

    $matrixResults = New-Object System.Collections.Generic.List[object]
    $matrixJsonPath = Join-Path $runResultsDirectory "chain-matrix-summary.json"
    $matrixMdPath = Join-Path $runResultsDirectory "chain-matrix-summary.md"
    $scriptPath = $PSCommandPath
    $chainIndex = 0
    foreach ($chain in $resolvedLaunchChains) {
        $chainIndex++
        $chainWorkshopIds = @($chain.orderedWorkshopIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $chainMissingParentIds = @()
        $chainResolutionSource = if ($null -ne $chain.PSObject.Properties["chainResolutionSource"]) { [string]$chain.chainResolutionSource } else { "unknown" }
        if ($null -ne $chain.PSObject.Properties["missingParentIds"]) {
            $chainMissingParentIds = @($chain.missingParentIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        }
        $chainRunId = "{0}-chain{1:D2}" -f $RunId, $chainIndex
        Write-Output ("Running deep live chain matrix entry {0}/{1}: runId={2} chain={3}" -f $chainIndex, $resolvedLaunchChains.Count, $chainRunId, ($chainWorkshopIds -join ","))

        if ($chainMissingParentIds.Count -gt 0) {
            Write-Warning ("Skipping chain '{0}' due missing parent dependencies: {1}" -f $chainRunId, ($chainMissingParentIds -join ","))
            $matrixResults.Add([ordered]@{
                chainId = [string]$chain.chainId
                runId = $chainRunId
                orderedWorkshopIds = @($chainWorkshopIds)
                classification = "blocked_dependency_missing_parent"
                # Dependency resolution block happens before any launch/test work and is
                # intentionally reported as non-execution failure for matrix continuity.
                exitCode = 0
                reproBundlePath = ""
                missingParentIds = @($chainMissingParentIds)
                chainResolutionSource = $chainResolutionSource
                launchAttempted = $false
            })

            $matrixSnapshot = @($matrixResults.ToArray())
            $matrixSnapshot | ConvertTo-Json -Depth 8 | Set-Content -Path $matrixJsonPath
            $matrixRowsSnapshot = @($matrixSnapshot | ForEach-Object {
                "| $([string]$_.chainId) | $([string]$_.runId) | $((@($_.orderedWorkshopIds) -join ',')) | $([string]$_.classification) | $([int]$_.exitCode) | $([string]$_.chainResolutionSource) | $([string]$_.launchAttempted) | $([string]$_.reproBundlePath) |"
            })
            if ($matrixRowsSnapshot.Count -eq 0) {
                $matrixRowsSnapshot = @("| _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ |")
            }
@"
# Chain Matrix Summary

| ChainId | RunId | WorkshopIds | Classification | ExitCode | ResolutionSource | LaunchAttempted | Bundle |
|---|---|---|---|---:|---|---|---|
$($matrixRowsSnapshot -join "`n")
"@ | Set-Content -Path $matrixMdPath
            continue
        }

        $invokeParams = [ordered]@{
            Configuration = $Configuration
            ResultsDirectory = $ResultsDirectory
            ProfileRoot = $ProfileRoot
            RunId = $chainRunId
            Scope = "FULL"
            LaunchWaitSeconds = $LaunchWaitSeconds
            LiveTestTimeoutSeconds = $LiveTestTimeoutSeconds
        }
        if ($FailOnMissingArtifacts) {
            $invokeParams["FailOnMissingArtifacts"] = $true
        }
        if ($Strict) {
            $invokeParams["Strict"] = $true
        }
        if ($RequireNonBlockedClassification) {
            $invokeParams["RequireNonBlockedClassification"] = $true
        }
        if ($NoBuild) {
            $invokeParams["NoBuild"] = $true
        }
        if ($AutoLaunch) {
            $invokeParams["AutoLaunch"] = $true
        }
        if (-not [string]::IsNullOrWhiteSpace($GameRoot)) {
            $invokeParams["GameRoot"] = $GameRoot
        }
        if ($chainWorkshopIds.Count -gt 0) {
            $invokeParams["ForceWorkshopIds"] = @($chainWorkshopIds)
        }

        $chainExitCode = 0
        try {
            & $scriptPath @invokeParams
        }
        catch {
            $chainExitCode = 1
            Write-Warning ("Chain run '{0}' failed: {1}" -f $chainRunId, $_.Exception.Message)
        }
        $chainBundlePath = Join-Path $ResultsDirectory (Join-Path "runs" (Join-Path $chainRunId "repro-bundle.json"))
        $chainClassification = ""
        if (Test-Path -Path $chainBundlePath) {
            try {
                $chainBundle = Get-Content -Raw -Path $chainBundlePath | ConvertFrom-Json
                $chainClassification = [string]$chainBundle.classification
            }
            catch {
                $chainClassification = "unknown"
            }
        }
        elseif ($chainExitCode -eq 0) {
            $chainClassification = "missing_bundle"
        }

        $matrixResults.Add([ordered]@{
            chainId = [string]$chain.chainId
            runId = $chainRunId
            orderedWorkshopIds = @($chainWorkshopIds)
            classification = $chainClassification
            exitCode = $chainExitCode
            reproBundlePath = $chainBundlePath
            missingParentIds = @($chainMissingParentIds)
            chainResolutionSource = $chainResolutionSource
            launchAttempted = $true
        })

        # Persist progress after each chain so interruption still yields usable matrix evidence.
        $matrixSnapshot = @($matrixResults.ToArray())
        $matrixSnapshot | ConvertTo-Json -Depth 8 | Set-Content -Path $matrixJsonPath
        $matrixRowsSnapshot = @($matrixSnapshot | ForEach-Object {
            "| $([string]$_.chainId) | $([string]$_.runId) | $((@($_.orderedWorkshopIds) -join ',')) | $([string]$_.classification) | $([int]$_.exitCode) | $([string]$_.chainResolutionSource) | $([string]$_.launchAttempted) | $([string]$_.reproBundlePath) |"
        })
        if ($matrixRowsSnapshot.Count -eq 0) {
            $matrixRowsSnapshot = @("| _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ |")
        }
@"
# Chain Matrix Summary

| ChainId | RunId | WorkshopIds | Classification | ExitCode | ResolutionSource | LaunchAttempted | Bundle |
|---|---|---|---|---:|---|---|---|
$($matrixRowsSnapshot -join "`n")
"@ | Set-Content -Path $matrixMdPath
    }

    $matrixArray = @($matrixResults.ToArray())
    $matrixArray | ConvertTo-Json -Depth 8 | Set-Content -Path $matrixJsonPath

    $matrixRows = @($matrixArray | ForEach-Object {
        "| $([string]$_.chainId) | $([string]$_.runId) | $((@($_.orderedWorkshopIds) -join ',')) | $([string]$_.classification) | $([int]$_.exitCode) | $([string]$_.chainResolutionSource) | $([string]$_.launchAttempted) | $([string]$_.reproBundlePath) |"
    })
    if ($matrixRows.Count -eq 0) {
        $matrixRows = @("| _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ |")
    }

    @"
# Chain Matrix Summary

| ChainId | RunId | WorkshopIds | Classification | ExitCode | ResolutionSource | LaunchAttempted | Bundle |
|---|---|---|---|---:|---|---|---|
$($matrixRows -join "`n")
"@ | Set-Content -Path $matrixMdPath

    Write-Output ("chain matrix summary json: {0}" -f $matrixJsonPath)
    Write-Output ("chain matrix summary markdown: {0}" -f $matrixMdPath)

    $matrixFailures = @($matrixArray | Where-Object { $_.exitCode -ne 0 -or $_.classification -in @("failed", "blocked_environment", "blocked_profile_mismatch", "unknown", "") })
    if ($matrixFailures.Count -gt 0) {
        throw ("Chain matrix deep run detected failures in {0} chain entries." -f $matrixFailures.Count)
    }

    return
}

if ($AutoLaunch) {
    if ($forcedChainMissingParentIds.Count -gt 0) {
        Write-Warning ("Skipping auto-launch due missing parent dependencies for forced chain: {0}" -f ($forcedChainMissingParentIds -join ","))
    }
    else {
        Start-AutoLaunchSession `
            -SelectedScope $Scope `
            -ForcedWorkshopIds $forceWorkshopIdsNormalized `
            -ForcedProfileId $forceProfileIdNormalized `
            -OverrideGameRoot $GameRoot `
            -TimeoutSeconds $LaunchWaitSeconds `
            -StabilizationSeconds $LaunchStabilizationSeconds
    }
}

$runTimestamp = Get-Date
$iso = $runTimestamp.ToString("yyyy-MM-dd HH:mm:ss zzz")
$runStartedUtc = $runTimestamp.ToUniversalTime().ToString("o")

$testDefinitions = @(
    [PSCustomObject]@{
        Name = "Live Tactical Toggles"
        TestName = "LiveTacticalToggleWorkflowTests"
        Filter = "FullyQualifiedName~LiveTacticalToggleWorkflowTests"
        TrxBase = "live-tactical.trx"
        Scopes = @("AOTR", "ROE", "TACTICAL")
    },
    [PSCustomObject]@{
        Name = "Live Hero Helper"
        TestName = "LiveHeroHelperWorkflowTests"
        Filter = "FullyQualifiedName~LiveHeroHelperWorkflowTests"
        TrxBase = "live-hero-helper.trx"
        Scopes = @("AOTR", "ROE")
        RequiresGalacticContext = $true
    },
    [PSCustomObject]@{
        Name = "Live ROE Runtime Health"
        TestName = "LiveRoeRuntimeHealthTests"
        Filter = "FullyQualifiedName~LiveRoeRuntimeHealthTests"
        TrxBase = "live-roe-health.trx"
        Scopes = @("ROE")
        RequiresGalacticContext = $true
    },
    [PSCustomObject]@{
        Name = "Live Credits"
        TestName = "LiveCreditsTests"
        Filter = "FullyQualifiedName~LiveCreditsTests"
        TrxBase = "live-credits.trx"
        Scopes = @("AOTR", "ROE")
    },
    [PSCustomObject]@{
        Name = "Live Promoted Action Matrix"
        TestName = "LivePromotedActionMatrixTests"
        Filter = "FullyQualifiedName~LivePromotedActionMatrixTests"
        TrxBase = "live-promoted-action-matrix.trx"
        Scopes = @("AOTR", "ROE")
    }
)

$summaries = New-Object System.Collections.Generic.List[object]
$fatalError = $null
$forcedMissingParentMessage = ""
if ($forcedChainMissingParentIds.Count -gt 0) {
    $forcedMissingParentMessage = "parent_dependency_missing: missing parent dependency IDs = " + ($forcedChainMissingParentIds -join ",") + "; chainResolutionSource=" + $forcedChainResolutionSource
}

foreach ($test in $testDefinitions) {
    $trxPath = Join-Path $runResultsDirectory ("{0}-{1}" -f $RunId, $test.TrxBase)

    if (-not [string]::IsNullOrWhiteSpace($forcedMissingParentMessage)) {
        $summaries.Add([PSCustomObject]@{
            Name = $test.TestName
            Summary = New-LiveTestSummary -Trx $trxPath -Outcome "Skipped" -Skipped 1 -Message $forcedMissingParentMessage
        })
        continue
    }

    if (-not (Test-RunSelection -Scopes $test.Scopes -SelectedScope $Scope)) {
        $summaries.Add([PSCustomObject]@{
            Name = $test.TestName
            Summary = New-LiveTestSummary -Trx $trxPath -Outcome "Skipped" -Skipped 1 -Message "scope_not_selected"
        })
        continue
    }

    if ($null -ne $fatalError) {
        $summaries.Add([PSCustomObject]@{
            Name = $test.TestName
            Summary = New-LiveTestSummary -Trx $trxPath -Outcome "Missing" -Message "not_executed_due_to_prior_failure"
        })
        continue
    }

    $requiresGalacticContext = $false
    if ($null -ne $test.PSObject.Properties["RequiresGalacticContext"]) {
        $requiresGalacticContext = [bool]$test.RequiresGalacticContext
    }

    if ($requiresGalacticContext) {
        $preSkipMessage = Test-ShouldPreSkipForTacticalContext -Summaries $summaries -TestName $test.TestName -AutoLaunchEnabled $AutoLaunch
        if (-not [string]::IsNullOrWhiteSpace($preSkipMessage)) {
            Write-Output ("Pre-skip {0}: LiveTacticalToggleWorkflowTests already confirmed tactical context during auto-launch." -f $test.TestName)
            $summaries.Add([PSCustomObject]@{
                Name = $test.TestName
                Summary = New-LiveTestSummary -Trx $trxPath -Outcome "Skipped" -Skipped 1 -Message $preSkipMessage
            })
            continue
        }
    }

    try {
        $executedTrx = Invoke-LiveTest `
            -Name $test.Name `
            -Filter $test.Filter `
            -TrxName ("{0}-{1}" -f $RunId, $test.TrxBase) `
            -NativeHostPath $runtimeHostPath
        $summary = Read-TrxSummary -TrxPath $executedTrx
        $summaries.Add([PSCustomObject]@{
            Name = $test.TestName
            Summary = $summary
        })
    }
    catch {
        $fatalError = $_.Exception
        $summaries.Add([PSCustomObject]@{
            Name = $test.TestName
            Summary = New-LiveTestSummary -Trx $trxPath -Outcome "Failed" -Failed 1 -Message $fatalError.Message
        })
    }
}

$missingCount = (@($summaries | Where-Object { $_.Summary.Outcome -eq "Missing" })).Count
if ($FailOnMissingArtifacts -and $missingCount -gt 0) {
    $fatalError = [InvalidOperationException]::new("Missing TRX artifacts detected ($missingCount).")
}

$summaryPath = Join-Path $runResultsDirectory "live-validation-summary.json"
$summaries | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath

$launchContextJson = Join-Path $runResultsDirectory "launch-context-fixture.json"
$launchContextScriptPath = Resolve-ArtifactPath -Path "tools/detect-launch-context.py"
$launchContextFixturePath = Resolve-ArtifactPath -Path "tools/fixtures/launch_context_cases.json"
$launchContextProfileRoot = Resolve-ArtifactPath -Path $ProfileRoot

if ($pythonCmd.Count -eq 0 -or $null -eq $pythonCmd[0]) {
    Write-Warning "Python was not found in this shell; skipping launch-context fixture generation."
    [PSCustomObject]@{
        status = "skipped"
        reason = "python_not_found"
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    } | ConvertTo-Json -Depth 4 | Set-Content -Path $launchContextJson
}
else {
    try {
        $pythonArgs = @()
        if (Test-IsWslPythonCommand -Command $pythonCmd) {
            $pythonArgs += @(
                (Convert-ToWslPath -Path $launchContextScriptPath),
                "--from-process-json", (Convert-ToWslPath -Path $launchContextFixturePath),
                "--profile-root", (Convert-ToWslPath -Path $launchContextProfileRoot),
                "--pretty"
            )
        }
        else {
            $pythonArgs += @(
                $launchContextScriptPath,
                "--from-process-json", $launchContextFixturePath,
                "--profile-root", $launchContextProfileRoot,
                "--pretty"
            )
        }

        $launchContextResult = Invoke-CapturedCommand -Command $pythonCmd -Arguments $pythonArgs
        $exitCode = [int]$launchContextResult.ExitCode
        $outputText = [string]$launchContextResult.Output
        $outputText = $outputText.Trim()

        if ($exitCode -ne 0) {
            throw ("python exited with code {0}. output: {1}" -f $exitCode, $outputText)
        }

        if ([string]::IsNullOrWhiteSpace($outputText)) {
            throw ("python produced no output. executable: {0}" -f $pythonCmd[0])
        }

        $outputText | Set-Content -Path $launchContextJson
    }
    catch {
        Write-Warning ("Launch-context fixture generation failed: {0}" -f $_.Exception.Message)
        [PSCustomObject]@{
            status = "failed"
            reason = "python_invocation_failed"
            detail = $_.Exception.Message
            generatedAtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        } | ConvertTo-Json -Depth 4 | Set-Content -Path $launchContextJson
    }
}

$bundlePath = Join-Path $runResultsDirectory "repro-bundle.json"
$bundleMdPath = Join-Path $runResultsDirectory "repro-bundle.md"
$bundleClassification = ""
if ($EmitReproBundle) {
    try {
        & (Join-Path $PSScriptRoot "collect-mod-repro-bundle.ps1") `
            -RunId $RunId `
            -RunDirectory $runResultsDirectory `
            -SummaryPath $summaryPath `
            -Scope $Scope `
            -ProfileRoot $ProfileRoot `
            -ForceWorkshopIds $forceWorkshopIdsNormalized `
            -ForceProfileId $forceProfileIdNormalized `
            -StartedAtUtc $runStartedUtc

        $collectExitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
        if ($collectExitCode -ne 0) {
            throw "collect-mod-repro-bundle.ps1 failed with exit code $collectExitCode"
        }

        & (Join-Path $PSScriptRoot "validate-repro-bundle.ps1") `
            -BundlePath $bundlePath `
            -SchemaPath "tools/schemas/repro-bundle.schema.json" `
            -Strict:$Strict

        $validateExitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
        if ($validateExitCode -ne 0) {
            throw "validate-repro-bundle.ps1 failed with exit code $validateExitCode"
        }

        if (Test-Path -Path $bundlePath) {
            $bundleJson = Get-Content -Raw -Path $bundlePath | ConvertFrom-Json
            $bundleClassification = [string]$bundleJson.classification
        }
    }
    catch {
        Write-Warning ("Repro bundle generation/validation failed: {0}" -f $_.Exception.Message)
        $fatalError = $_.Exception
    }
}

if ($RequireNonBlockedClassification) {
    if (-not $EmitReproBundle) {
        $fatalError = [InvalidOperationException]::new("-RequireNonBlockedClassification requires repro-bundle emission.")
    }
    elseif ([string]::IsNullOrWhiteSpace($bundleClassification)) {
        $fatalError = [InvalidOperationException]::new("Hard gate could not determine repro-bundle classification.")
    }
    elseif ($bundleClassification -in @("blocked_environment", "blocked_profile_mismatch")) {
        $fatalError = [InvalidOperationException]::new(
            "Hard gate failed: classification '$bundleClassification' is blocked. Launch with correct process context and rerun.")
    }
}

Write-Output ""
Write-Output "=== Live Validation Summary ($iso) ==="
Write-Output "run id: $RunId"
Write-Output "scope: $Scope"
Write-Output "auto launch: $AutoLaunch"
if (-not [string]::IsNullOrWhiteSpace($runtimeHostPath)) {
    Write-Output "native host path: $runtimeHostPath"
}
foreach ($entry in $summaries) {
    $s = $entry.Summary
    Write-Output ("{0}: outcome={1} passed={2} failed={3} skipped={4} message='{5}'" -f $entry.Name, $s.Outcome, $s.Passed, $s.Failed, $s.Skipped, $s.Message)
}
Write-Output "launch-context fixture: $launchContextJson"
Write-Output "summary json: $summaryPath"
if ($EmitReproBundle) {
    Write-Output "repro bundle json: $bundlePath"
    Write-Output "repro bundle markdown: $bundleMdPath"
    if (-not [string]::IsNullOrWhiteSpace($bundleClassification)) {
        Write-Output "repro bundle classification: $bundleClassification"
    }
}

$byName = @{}
foreach ($entry in $summaries) {
    $byName[$entry.Name] = $entry.Summary
}

function Get-Line {
    param([string]$Name)
    if ($byName.ContainsKey($Name)) {
        return $byName[$Name]
    }

    return [PSCustomObject]@{
        Trx = ""
        Outcome = "Missing"
        Passed = 0
        Failed = 0
        Skipped = 0
        Message = "missing_summary_entry"
    }
}

$lineTactical = Get-Line -Name "LiveTacticalToggleWorkflowTests"
$lineHero = Get-Line -Name "LiveHeroHelperWorkflowTests"
$lineRoe = Get-Line -Name "LiveRoeRuntimeHealthTests"
$lineCredits = Get-Line -Name "LiveCreditsTests"
$linePromoted = Get-Line -Name "LivePromotedActionMatrixTests"

$template34 = Join-Path $runResultsDirectory "issue-34-evidence-template.md"
$template19 = Join-Path $runResultsDirectory "issue-19-evidence-template.md"

@"
Live validation evidence update ($iso)

- runId: $RunId
- Date/time: $iso
- Scope: $Scope
- Profile id: <fill from live attach output>
- Launch recommendation: <profileId/reasonCode/confidence from live attach output>
- Runtime mode at attach: <fill>
- Tactical toggle workflow: $($lineTactical.Outcome) (p=$($lineTactical.Passed), f=$($lineTactical.Failed), s=$($lineTactical.Skipped))
  - detail: $($lineTactical.Message)
- Hero helper workflow: $($lineHero.Outcome) (p=$($lineHero.Passed), f=$($lineHero.Failed), s=$($lineHero.Skipped))
  - detail: $($lineHero.Message)
- ROE runtime health: $($lineRoe.Outcome) (p=$($lineRoe.Passed), f=$($lineRoe.Failed), s=$($lineRoe.Skipped))
  - detail: $($lineRoe.Message)
- Credits live diagnostic: $($lineCredits.Outcome) (p=$($lineCredits.Passed), f=$($lineCredits.Failed), s=$($lineCredits.Skipped))
  - detail: $($lineCredits.Message)
- Promoted action matrix: $($linePromoted.Outcome) (p=$($linePromoted.Passed), f=$($linePromoted.Failed), s=$($linePromoted.Skipped))
  - detail: $($linePromoted.Message)
- Diagnostics for degraded/unavailable actions: <fill>
- Repro bundle: $bundlePath
- Artifacts:
  - $($lineTactical.Trx)
  - $($lineHero.Trx)
  - $($lineRoe.Trx)
  - $($lineCredits.Trx)
  - $($linePromoted.Trx)
  - $launchContextJson
  - $summaryPath
  - $bundleMdPath

Status gate for closure:
- [ ] At least one successful tactical toggle + revert in tactical mode
- [ ] At least one helper workflow result captured per target profile (AOTR + ROE)
"@ | Set-Content -Path $template34

@"
AOTR/ROE checklist evidence update ($iso)

- runId: $RunId
- scope: $Scope
- repro bundle: $bundlePath

| Profile | Attach summary | Tactical toggle workflow | Hero helper workflow | Promoted action matrix | Result |
|---|---|---|---|---|---|
| aotr_1397421866_swfoc | <fill pid/mode/reasonCode> | <pass/fail/skip + reason> | <pass/fail/skip + reason> | <pass/fail/skip + reason> | <overall> |
| roe_3447786229_swfoc | <fill pid/mode/reasonCode> | <pass/fail/skip + reason> | <pass/fail/skip + reason> | <pass/fail/skip + reason> | <overall> |

Current local run snapshot:
- LiveTacticalToggleWorkflowTests: $($lineTactical.Outcome) ($($lineTactical.Message))
- LiveHeroHelperWorkflowTests: $($lineHero.Outcome) ($($lineHero.Message))
- LiveRoeRuntimeHealthTests: $($lineRoe.Outcome) ($($lineRoe.Message))
- LiveCreditsTests: $($lineCredits.Outcome) ($($lineCredits.Message))
- LivePromotedActionMatrixTests: $($linePromoted.Outcome) ($($linePromoted.Message))

Artifacts:
- $summaryPath
- $launchContextJson
- $bundlePath
- $bundleMdPath
"@ | Set-Content -Path $template19

Write-Output "issue template (34): $template34"
Write-Output "issue template (19): $template19"

if ($null -ne $fatalError) {
    throw $fatalError
}
