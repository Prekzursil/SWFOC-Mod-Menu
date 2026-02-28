param(
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [Parameter(Mandatory = $true)][string]$SummaryPath,
    [Parameter(Mandatory = $true)][ValidateSet("AOTR", "ROE", "TACTICAL", "FULL")][string]$Scope,
    [string]$ProfileRoot = "profiles/default",
    [string[]]$ForceWorkshopIds = @(),
    [string]$ForceProfileId = "",
    [string]$StartedAtUtc = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

function Resolve-PythonCommand {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -ne $python) {
        return @($python.Source)
    }

    $python3 = Get-Command python3 -ErrorAction SilentlyContinue
    if ($null -ne $python3) {
        return @($python3.Source)
    }

    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($null -ne $py) {
        return @($py.Source, "-3")
    }

    return @()
}

function Normalize-ForcedWorkshopIds {
    param([string[]]$RawIds)

    $ids = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($raw in $RawIds) {
        if ([string]::IsNullOrWhiteSpace($raw)) {
            continue
        }

        foreach ($token in ([string]$raw -split ",")) {
            $value = [string]$token
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                [void]$ids.Add($value.Trim())
            }
        }
    }

    return @($ids | Sort-Object)
}

function Get-SteamModIdsFromCommandLine {
    param([string]$CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return @()
    }

    $regexMatches = [regex]::Matches($CommandLine, "STEAMMOD\s*=\s*([0-9]{4,})", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $ids = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($match in $regexMatches) {
        if ($match.Groups.Count -gt 1) {
            [void]$ids.Add($match.Groups[1].Value)
        }
    }

    return @($ids)
}

function Get-ProcessSnapshot {
    $processes = Get-CimInstance Win32_Process |
        Where-Object {
            $_.Name -match "^(swfoc\.exe|StarWarsG\.exe|sweaw\.exe)$"
        }

    $snapshot = New-Object System.Collections.Generic.List[object]
    foreach ($proc in $processes) {
        $steamIds = @(
            Get-SteamModIdsFromCommandLine -CommandLine $proc.CommandLine
        )
        $steamIdCount = @($steamIds).Count
        $hostRole = if ($proc.Name -ieq "StarWarsG.exe") { "game_host" } elseif ($proc.Name -match "^(swfoc\\.exe|sweaw\\.exe)$") { "launcher" } else { "unknown" }
        $moduleSize = 0
        try {
            $psProc = Get-Process -Id ([int]$proc.ProcessId) -ErrorAction Stop
            $moduleSize = [int]$psProc.MainModule.ModuleMemorySize
        } catch {
            $moduleSize = 0
        }
        $hostScore = if ($hostRole -eq "game_host") { 200 } elseif ($hostRole -eq "launcher") { 100 } else { 0 }
        $selectionScore = ([double]($steamIdCount * 1000)) + [double]$hostScore + ($(if ([string]::IsNullOrWhiteSpace([string]$proc.CommandLine)) { 0 } else { 10 })) + ([double]$moduleSize / 1000000.0)
        $snapshot.Add([PSCustomObject]@{
            pid = [int]$proc.ProcessId
            name = [string]$proc.Name
            path = [string]$proc.ExecutablePath
            commandLine = [string]$proc.CommandLine
            steamModIds = $steamIds
            hostRole = $hostRole
            mainModuleSize = $moduleSize
            workshopMatchCount = [int]$steamIdCount
            selectionScore = [math]::Round($selectionScore, 2)
        })
    }

    return $snapshot
}

function Get-PreferredProcess {
    param([System.Collections.IEnumerable]$Snapshot)

    $items = New-Object System.Collections.Generic.List[object]
    foreach ($item in $Snapshot) {
        if ($null -ne $item) {
            $items.Add($item)
        }
    }
    if ($items.Count -eq 0) {
        return $null
    }

    function Get-ProcessNameValue {
        param([object]$Candidate)

        if ($null -eq $Candidate) {
            return ""
        }

        $nameProp = $Candidate.PSObject.Properties["name"]
        if ($null -ne $nameProp -and -not [string]::IsNullOrWhiteSpace([string]$nameProp.Value)) {
            return [string]$nameProp.Value
        }

        $upperNameProp = $Candidate.PSObject.Properties["Name"]
        if ($null -ne $upperNameProp -and -not [string]::IsNullOrWhiteSpace([string]$upperNameProp.Value)) {
            return [string]$upperNameProp.Value
        }

        return ""
    }

    $starWarsG = $items | Where-Object { (Get-ProcessNameValue $_) -ieq "StarWarsG.exe" } | Select-Object -First 1
    if ($null -ne $starWarsG) {
        return $starWarsG
    }

    $swfoc = $items | Where-Object { (Get-ProcessNameValue $_) -ieq "swfoc.exe" } | Select-Object -First 1
    if ($null -ne $swfoc) {
        return $swfoc
    }

    return $items[0]
}

function Get-LaunchContext {
    param(
        [object]$Process,
        [string]$ProfileRootPath,
        [string[]]$ForcedWorkshopIds,
        [string]$ForcedProfileId
    )

    $normalizedForcedWorkshopIds = @(Normalize-ForcedWorkshopIds -RawIds $ForcedWorkshopIds)
    $normalizedForcedProfileId = if ([string]::IsNullOrWhiteSpace($ForcedProfileId)) { $null } else { $ForcedProfileId.Trim() }
    $forcedSource = if ($normalizedForcedWorkshopIds.Count -gt 0 -or -not [string]::IsNullOrWhiteSpace($normalizedForcedProfileId)) { "forced" } else { "detected" }

    if ($null -eq $Process) {
        return [PSCustomObject]@{
            profileId = $null
            reasonCode = "no_process"
            confidence = 0.0
            launchKind = "Unknown"
            source = $forcedSource
            forcedWorkshopIds = $normalizedForcedWorkshopIds
            forcedProfileId = $normalizedForcedProfileId
        }
    }

    $pythonCmd = @(Resolve-PythonCommand)
    if ($pythonCmd.Count -eq 0) {
        return [PSCustomObject]@{
            profileId = $null
            reasonCode = "python_not_found"
            confidence = 0.0
            launchKind = "Unknown"
            source = $forcedSource
            forcedWorkshopIds = $normalizedForcedWorkshopIds
            forcedProfileId = $normalizedForcedProfileId
        }
    }

    $args = @()
    if ($pythonCmd.Count -gt 1) {
        $args += $pythonCmd[1..($pythonCmd.Count - 1)]
    }
    $args += @(
        "tools/detect-launch-context.py",
        "--command-line", $Process.commandLine,
        "--process-name", $Process.name,
        "--process-path", $Process.path,
        "--profile-root", $ProfileRootPath
    )

    $forcedWorkshopIdsCsv = if ($normalizedForcedWorkshopIds.Count -eq 0) { "" } else { $normalizedForcedWorkshopIds -join "," }
    if (-not [string]::IsNullOrWhiteSpace($forcedWorkshopIdsCsv)) {
        $args += @("--force-workshop-ids", $forcedWorkshopIdsCsv)
    }

    if (-not [string]::IsNullOrWhiteSpace($ForcedProfileId)) {
        $args += @("--force-profile-id", $normalizedForcedProfileId)
    }

    $args += "--pretty"

    try {
        $output = & $pythonCmd[0] @args 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "detect-launch-context.py exited with $LASTEXITCODE"
        }

        $raw = ($output | Out-String)
        if ([string]::IsNullOrWhiteSpace($raw)) {
            throw "detect-launch-context.py returned empty output"
        }

        $parsed = $raw | ConvertFrom-Json
        return [PSCustomObject]@{
            profileId = [string]$parsed.profileRecommendation.profileId
            reasonCode = [string]$parsed.profileRecommendation.reasonCode
            confidence = [double]$parsed.profileRecommendation.confidence
            launchKind = [string]$parsed.launchContext.launchKind
            source = if ([string]::IsNullOrWhiteSpace([string]$parsed.launchContext.source)) { "detected" } else { [string]$parsed.launchContext.source }
            forcedWorkshopIds = @($parsed.launchContext.forcedWorkshopIds)
            forcedProfileId = if ([string]::IsNullOrWhiteSpace([string]$parsed.launchContext.forcedProfileId)) { $null } else { [string]$parsed.launchContext.forcedProfileId }
        }
    }
    catch {
        return [PSCustomObject]@{
            profileId = $null
            reasonCode = "launch_context_detection_failed"
            confidence = 0.0
            launchKind = "Unknown"
            source = $forcedSource
            forcedWorkshopIds = $normalizedForcedWorkshopIds
            forcedProfileId = $normalizedForcedProfileId
        }
    }
}

function Get-ProfileRequiredCapabilities {
    param(
        [string]$ProfileRootPath,
        [string]$ProfileId
    )

    if ([string]::IsNullOrWhiteSpace($ProfileId)) {
        return @()
    }

    $profilePath = Join-Path (Join-Path $ProfileRootPath "profiles") "$ProfileId.json"
    if (-not (Test-Path -Path $profilePath)) {
        return @()
    }

    try {
        $json = Get-Content -Raw -Path $profilePath | ConvertFrom-Json
        if ($null -eq $json.requiredCapabilities) {
            return @()
        }

        $values = New-Object System.Collections.Generic.List[string]
        foreach ($item in @($json.requiredCapabilities)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$item)) {
                $values.Add([string]$item)
            }
        }

        return @($values)
    }
    catch {
        return @()
    }
}

function Get-RuntimeEvidence {
    param([string]$RunDirectoryPath)

    $path = Join-Path $RunDirectoryPath "live-roe-runtime-evidence.json"
    if (-not (Test-Path -Path $path)) {
        return $null
    }

    try {
        return Get-Content -Raw -Path $path | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Get-ActionStatusDiagnostics {
    param([string]$RunDirectoryPath)

    $path = Join-Path $RunDirectoryPath "live-promoted-action-matrix.json"
    if (-not (Test-Path -Path $path)) {
        return [ordered]@{
            status = "missing"
            source = "live-promoted-action-matrix.json"
            summary = [ordered]@{
                total = 0
                passed = 0
                failed = 0
                skipped = 0
            }
            entries = @()
        }
    }

    try {
        $payload = Get-Content -Raw -Path $path | ConvertFrom-Json
        $rawDiagnostics = if ($null -ne $payload.PSObject.Properties["actionStatusDiagnostics"]) {
            $payload.actionStatusDiagnostics
        }
        else {
            $payload
        }

        $entries = New-Object System.Collections.Generic.List[object]
        foreach ($entry in @($rawDiagnostics.entries)) {
            $entries.Add([ordered]@{
                profileId = [string]$entry.profileId
                actionId = [string]$entry.actionId
                outcome = [string]$entry.outcome
                backendRoute = if ([string]::IsNullOrWhiteSpace([string]$entry.backendRoute)) { $null } else { [string]$entry.backendRoute }
                routeReasonCode = if ([string]::IsNullOrWhiteSpace([string]$entry.routeReasonCode)) { $null } else { [string]$entry.routeReasonCode }
                capabilityProbeReasonCode = if ([string]::IsNullOrWhiteSpace([string]$entry.capabilityProbeReasonCode)) { $null } else { [string]$entry.capabilityProbeReasonCode }
                hybridExecution = if ($null -eq $entry.hybridExecution) { $null } else { [bool]$entry.hybridExecution }
                hasFallbackMarker = [bool]$entry.hasFallbackMarker
                message = [string]$entry.message
                skipReasonCode = if ([string]::IsNullOrWhiteSpace([string]$entry.skipReasonCode)) { $null } else { [string]$entry.skipReasonCode }
            })
        }

        $derivedSummary = [ordered]@{
            total = @($entries).Count
            passed = @($entries | Where-Object { $_.outcome -eq "Passed" }).Count
            failed = @($entries | Where-Object { $_.outcome -eq "Failed" }).Count
            skipped = @($entries | Where-Object { $_.outcome -eq "Skipped" }).Count
        }

        $rawSummary = $rawDiagnostics.summary
        $summary = [ordered]@{
            total = if ($null -ne $rawSummary -and $null -ne $rawSummary.total) { [int]$rawSummary.total } else { [int]$derivedSummary.total }
            passed = if ($null -ne $rawSummary -and $null -ne $rawSummary.passed) { [int]$rawSummary.passed } else { [int]$derivedSummary.passed }
            failed = if ($null -ne $rawSummary -and $null -ne $rawSummary.failed) { [int]$rawSummary.failed } else { [int]$derivedSummary.failed }
            skipped = if ($null -ne $rawSummary -and $null -ne $rawSummary.skipped) { [int]$rawSummary.skipped } else { [int]$derivedSummary.skipped }
        }

        return [ordered]@{
            status = if ([string]::IsNullOrWhiteSpace([string]$rawDiagnostics.status)) { "captured" } else { [string]$rawDiagnostics.status }
            source = if ([string]::IsNullOrWhiteSpace([string]$rawDiagnostics.source)) { "live-promoted-action-matrix.json" } else { [string]$rawDiagnostics.source }
            summary = $summary
            entries = @($entries)
        }
    }
    catch {
        return [ordered]@{
            status = "parse_error"
            source = "live-promoted-action-matrix.json"
            summary = [ordered]@{
                total = 0
                passed = 0
                failed = 0
                skipped = 0
            }
            entries = @()
            error = $_.Exception.Message
        }
    }
}

function ConvertTo-LiveTestSummary {
    param([object[]]$SummaryEntries)

    $tests = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $SummaryEntries) {
        $summary = $entry.Summary
        $tests.Add([PSCustomObject]@{
            name = [string]$entry.Name
            outcome = [string]$summary.Outcome
            passed = [int]$summary.Passed
            failed = [int]$summary.Failed
            skipped = [int]$summary.Skipped
            trxPath = [string]$summary.Trx
            message = [string]$summary.Message
        })
    }

    return $tests
}

function Get-RelevantTestNames {
    param([string]$SelectedScope)

    switch ($SelectedScope) {
        "AOTR" {
            return @(
                "LiveTacticalToggleWorkflowTests",
                "LiveHeroHelperWorkflowTests",
                "LiveCreditsTests",
                "LivePromotedActionMatrixTests"
            )
        }
        "ROE" {
            return @(
                "LiveTacticalToggleWorkflowTests",
                "LiveHeroHelperWorkflowTests",
                "LiveRoeRuntimeHealthTests",
                "LiveCreditsTests",
                "LivePromotedActionMatrixTests"
            )
        }
        "TACTICAL" {
            return @("LiveTacticalToggleWorkflowTests")
        }
        default {
            return @(
                "LiveTacticalToggleWorkflowTests",
                "LiveHeroHelperWorkflowTests",
                "LiveRoeRuntimeHealthTests",
                "LiveCreditsTests",
                "LivePromotedActionMatrixTests"
            )
        }
    }
}

function Get-RuntimeMode {
    param([object[]]$LiveTests)

    $tactical = $LiveTests | Where-Object { $_.name -eq "LiveTacticalToggleWorkflowTests" } | Select-Object -First 1
    if ($null -eq $tactical) {
        return [PSCustomObject]@{
            hint = "Unknown"
            effective = "Unknown"
            reasonCode = "tactical_test_absent"
        }
    }

    if ($tactical.outcome -eq "Passed") {
        return [PSCustomObject]@{
            hint = "Unknown"
            effective = "Tactical"
            reasonCode = "tactical_test_passed"
        }
    }

    $message = ([string]$tactical.message).ToLowerInvariant()
    if ($message.Contains("runtime mode is galactic")) {
        return [PSCustomObject]@{
            hint = "Galactic"
            effective = "Galactic"
            reasonCode = "tactical_skip_mode_galactic"
        }
    }

    if ($message.Contains("runtime mode is unknown")) {
        return [PSCustomObject]@{
            hint = "Unknown"
            effective = "Unknown"
            reasonCode = "tactical_skip_mode_unknown"
        }
    }

    return [PSCustomObject]@{
        hint = "Unknown"
        effective = "Unknown"
        reasonCode = "mode_not_determined"
    }
}

function Get-Classification {
    param(
        [object[]]$Relevant,
        [object[]]$ProcessSnapshot,
        [string]$SelectedScope
    )

    $relevantItems = New-Object System.Collections.Generic.List[object]
    foreach ($item in $Relevant) {
        if ($null -ne $item) {
            $relevantItems.Add($item)
        }
    }

    $processItems = New-Object System.Collections.Generic.List[object]
    foreach ($item in $ProcessSnapshot) {
        if ($null -ne $item) {
            $processItems.Add($item)
        }
    }

    if ($processItems.Count -eq 0) {
        return "blocked_environment"
    }

    if ($relevantItems.Count -eq 0) {
        return "failed"
    }

    if (@($relevantItems | Where-Object { $_.outcome -eq "Failed" -or $_.outcome -eq "Missing" }).Count -gt 0) {
        return "failed"
    }

    $allPassed = (@($relevantItems | Where-Object { $_.outcome -eq "Passed" })).Count -eq $relevantItems.Count
    if ($allPassed) {
        return "passed"
    }

    $allSkipped = (@($relevantItems | Where-Object { $_.outcome -eq "Skipped" })).Count -eq $relevantItems.Count
    if ($allSkipped) {
        $messages = ($relevantItems | ForEach-Object { ([string]$_.message).ToLowerInvariant() }) -join " "
        if (
            ($SelectedScope -eq "ROE" -and $messages.Contains("3447786229")) -or
            ($messages.Contains("no aotr/roe launch context"))
        ) {
            return "blocked_profile_mismatch"
        }

        return "skipped"
    }

    return "skipped"
}

if (-not (Test-Path -Path $SummaryPath)) {
    throw "Summary path not found: $SummaryPath"
}

if (-not (Test-Path -Path $RunDirectory)) {
    New-Item -Path $RunDirectory -ItemType Directory | Out-Null
}

$summaryRaw = Get-Content -Raw -Path $SummaryPath | ConvertFrom-Json
$forceWorkshopIdsNormalized = @(Normalize-ForcedWorkshopIds -RawIds $ForceWorkshopIds)
$forceProfileIdNormalized = if ([string]::IsNullOrWhiteSpace($ForceProfileId)) { "" } else { $ForceProfileId.Trim() }
$summaryEntries = @($summaryRaw)
$liveTests = @()
foreach ($test in (ConvertTo-LiveTestSummary -SummaryEntries $summaryEntries)) {
    $liveTests += $test
}
$relevantNames = Get-RelevantTestNames -SelectedScope $Scope
$relevantLiveTests = @($liveTests | Where-Object { $relevantNames -contains $_.name })
$processSnapshot = @()
foreach ($process in (Get-ProcessSnapshot)) {
    $processSnapshot += $process
}
$preferredProcess = Get-PreferredProcess -Snapshot $processSnapshot
$launchContext = Get-LaunchContext `
    -Process $preferredProcess `
    -ProfileRootPath $ProfileRoot `
    -ForcedWorkshopIds $forceWorkshopIdsNormalized `
    -ForcedProfileId $forceProfileIdNormalized
$runtimeMode = Get-RuntimeMode -LiveTests $relevantLiveTests
$classification = Get-Classification -Relevant $relevantLiveTests -ProcessSnapshot $processSnapshot -SelectedScope $Scope
$requiredCapabilities = Get-ProfileRequiredCapabilities -ProfileRootPath $ProfileRoot -ProfileId ([string]$launchContext.profileId)
$runtimeEvidence = Get-RuntimeEvidence -RunDirectoryPath $RunDirectory
$actionStatusDiagnostics = Get-ActionStatusDiagnostics -RunDirectoryPath $RunDirectory

$nextAction = switch ($classification) {
    "passed" { "Attach bundle to issue and continue with fix or closure workflow." }
    "blocked_environment" { "Launch target SWFOC process and rerun validation." }
    "blocked_profile_mismatch" { "Relaunch with required STEAMMOD/MODPATH markers for selected scope and rerun." }
    "failed" { "Inspect failed/missing test artifacts and runtime diagnostics before retry." }
    default { "Review skipped reasons and gather additional live context for this scope." }
}

$selectedHostProcess = if ($null -eq $preferredProcess) {
    [ordered]@{
        pid = $null
        name = $null
        hostRole = "unknown"
        selectionScore = 0.0
        workshopMatchCount = 0
        mainModuleSize = 0
    }
} else {
    [ordered]@{
        pid = [int]$preferredProcess.pid
        name = [string]$preferredProcess.name
        hostRole = [string]$preferredProcess.hostRole
        selectionScore = [double]$preferredProcess.selectionScore
        workshopMatchCount = [int]$preferredProcess.workshopMatchCount
        mainModuleSize = [int]$preferredProcess.mainModuleSize
    }
}

$backendRouteDecision = if ($null -ne $runtimeEvidence) {
    [ordered]@{
        backend = if ([string]::IsNullOrWhiteSpace([string]$runtimeEvidence.result.backendRoute)) { "unknown" } else { [string]$runtimeEvidence.result.backendRoute }
        allowed = [bool]$runtimeEvidence.result.succeeded
        reasonCode = if ([string]::IsNullOrWhiteSpace([string]$runtimeEvidence.result.routeReasonCode)) { "UNKNOWN" } else { [string]$runtimeEvidence.result.routeReasonCode }
        message = [string]$runtimeEvidence.result.message
        source = "live-roe-runtime-evidence.json"
    }
}
else {
    [ordered]@{
        backend = "memory"
        allowed = $true
        reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE"
        message = "Runtime evidence file missing; route reflects fallback default."
        source = "fallback_default"
    }
}

$capabilityProbeSnapshot = if ($null -ne $runtimeEvidence) {
    [ordered]@{
        backend = "extender"
        probeReasonCode = if ([string]::IsNullOrWhiteSpace([string]$runtimeEvidence.result.capabilityProbeReasonCode)) { "CAPABILITY_UNKNOWN" } else { [string]$runtimeEvidence.result.capabilityProbeReasonCode }
        capabilityCount = 1
        requiredCapabilities = @($requiredCapabilities)
        source = "live-roe-runtime-evidence.json"
    }
}
else {
    [ordered]@{
        backend = "extender"
        probeReasonCode = "CAPABILITY_BACKEND_UNAVAILABLE"
        capabilityCount = 0
        requiredCapabilities = @($requiredCapabilities)
        source = "fallback_default"
    }
}

$hookInstallReport = if ($null -ne $runtimeEvidence) {
    [ordered]@{
        state = if ([string]::IsNullOrWhiteSpace([string]$runtimeEvidence.result.hookState)) { "unknown" } else { [string]$runtimeEvidence.result.hookState }
        reasonCode = if ([bool]$runtimeEvidence.result.succeeded) { "CAPABILITY_PROBE_PASS" } else { "HOOK_INSTALL_FAILED" }
        details = "Derived from runtime action diagnostics."
        source = "live-roe-runtime-evidence.json"
    }
}
else {
    [ordered]@{
        state = "unknown"
        reasonCode = "HOOK_INSTALL_FAILED"
        details = "Hook lifecycle state not available (runtime evidence missing)."
        source = "fallback_default"
    }
}

$overlayState = [ordered]@{
    available = $false
    visible = $false
    reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE"
    source = if ($null -ne $runtimeEvidence) { "runtime_reported_unavailable" } else { "fallback_default" }
}

$bundle = [ordered]@{
    schemaVersion = "1.2"
    runId = $RunId
    startedAtUtc = if ([string]::IsNullOrWhiteSpace($StartedAtUtc)) { (Get-Date).ToUniversalTime().ToString("o") } else { $StartedAtUtc }
    scope = $Scope
    processSnapshot = @($processSnapshot)
    launchContext = $launchContext
    runtimeMode = $runtimeMode
    selectedHostProcess = $selectedHostProcess
    backendRouteDecision = $backendRouteDecision
    capabilityProbeSnapshot = $capabilityProbeSnapshot
    hookInstallReport = $hookInstallReport
    overlayState = $overlayState
    actionStatusDiagnostics = $actionStatusDiagnostics
    liveTests = @($liveTests)
    diagnostics = [ordered]@{
        dependencyState = "unknown"
        helperReadiness = "unknown"
        symbolHealthSummary = "not-captured"
    }
    classification = $classification
    nextAction = $nextAction
}

$bundlePath = Join-Path $RunDirectory "repro-bundle.json"
$bundle | ConvertTo-Json -Depth 8 | Set-Content -Path $bundlePath

$bundleMdPath = Join-Path $RunDirectory "repro-bundle.md"
$liveRows = $liveTests | ForEach-Object {
    "| {0} | {1} | {2}/{3}/{4} | {5} | {6} |" -f $_.name, $_.outcome, $_.passed, $_.failed, $_.skipped, $_.trxPath, ([string]$_.message).Replace("|", "/")
}
$actionStatusRows = @($actionStatusDiagnostics.entries | ForEach-Object {
    "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
        ([string]$_.profileId), `
        ([string]$_.actionId), `
        ([string]$_.outcome), `
        ([string](if ($null -eq $_.backendRoute) { "n/a" } else { $_.backendRoute })), `
        ([string](if ($null -eq $_.routeReasonCode) { "n/a" } else { $_.routeReasonCode })), `
        ([string](if ($null -eq $_.capabilityProbeReasonCode) { "n/a" } else { $_.capabilityProbeReasonCode })), `
        ([string](if ($null -eq $_.hybridExecution) { "n/a" } else { $_.hybridExecution })), `
        ([string]$_.hasFallbackMarker), `
        (([string]$_.message).Replace("|", "/"))
})

if (@($actionStatusRows).Count -eq 0) {
    $actionStatusRows = @("| _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ | _none_ |")
}

@"
# Repro Bundle Summary

- runId: $RunId
- scope: $Scope
- classification: $classification
- launch profile: $($launchContext.profileId)
- launch reason: $($launchContext.reasonCode)
- confidence: $($launchContext.confidence)
- launch kind: $($launchContext.launchKind)
- launch context source: $($launchContext.source)
- forced workshop ids: $((@($launchContext.forcedWorkshopIds) -join ','))
- forced profile id: $($launchContext.forcedProfileId)
- runtime mode effective: $($runtimeMode.effective)
- runtime mode reason: $($runtimeMode.reasonCode)
- selected host: $($selectedHostProcess.name) (pid=$($selectedHostProcess.pid), role=$($selectedHostProcess.hostRole), score=$($selectedHostProcess.selectionScore))
- backend route: $($backendRouteDecision.backend) ($($backendRouteDecision.reasonCode))
- capability probe: $($capabilityProbeSnapshot.backend) ($($capabilityProbeSnapshot.probeReasonCode), required=$((@($capabilityProbeSnapshot.requiredCapabilities) -join ', ')))
- overlay: available=$($overlayState.available) visible=$($overlayState.visible) ($($overlayState.reasonCode))
- promoted action diagnostics: status=$($actionStatusDiagnostics.status) checks=$($actionStatusDiagnostics.summary.total) passed=$($actionStatusDiagnostics.summary.passed) failed=$($actionStatusDiagnostics.summary.failed) skipped=$($actionStatusDiagnostics.summary.skipped)

## Process Snapshot

| PID | Name | Role | Score | SteamModIds | Command Line |
|---|---|---|---:|---|---|
$((@($processSnapshot | ForEach-Object { "| $($_.pid) | $($_.name) | $($_.hostRole) | $($_.selectionScore) | $(@($_.steamModIds) -join ',') | $([string]$_.commandLine).Replace('|','/') |" }) -join "`n"))

## Live Tests

| Test | Outcome | Pass/Fail/Skip | TRX | Message |
|---|---|---|---|---|
$($liveRows -join "`n")

## Action Status Diagnostics

| Profile | Action | Outcome | Backend | Route Reason | Probe Reason | Hybrid | Fallback Marker | Message |
|---|---|---|---|---|---|---|---|---|
$($actionStatusRows -join "`n")

## Next Action

$nextAction
"@ | Set-Content -Path $bundleMdPath

Write-Output "repro bundle json: $bundlePath"
Write-Output "repro bundle markdown: $bundleMdPath"
