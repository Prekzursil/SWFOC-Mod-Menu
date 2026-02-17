param(
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][string]$RunDirectory,
    [Parameter(Mandatory = $true)][string]$SummaryPath,
    [Parameter(Mandatory = $true)][ValidateSet("AOTR", "ROE", "TACTICAL", "FULL")][string]$Scope,
    [string]$ProfileRoot = "profiles/default",
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

function Parse-SteamModIds {
    param([string]$CommandLine)

    if ([string]::IsNullOrWhiteSpace($CommandLine)) {
        return @()
    }

    $matches = [regex]::Matches($CommandLine, "STEAMMOD\s*=\s*([0-9]{4,})", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $ids = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($match in $matches) {
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
        $steamIds = Parse-SteamModIds -CommandLine $proc.CommandLine
        $snapshot.Add([PSCustomObject]@{
            pid = [int]$proc.ProcessId
            name = [string]$proc.Name
            path = [string]$proc.ExecutablePath
            commandLine = [string]$proc.CommandLine
            steamModIds = $steamIds
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

function Detect-LaunchContext {
    param(
        [object]$Process,
        [string]$ProfileRootPath
    )

    if ($null -eq $Process) {
        return [PSCustomObject]@{
            profileId = $null
            reasonCode = "no_process"
            confidence = 0.0
            launchKind = "Unknown"
        }
    }

    $pythonCmd = @(Resolve-PythonCommand)
    if ($pythonCmd.Count -eq 0) {
        return [PSCustomObject]@{
            profileId = $null
            reasonCode = "python_not_found"
            confidence = 0.0
            launchKind = "Unknown"
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
        "--profile-root", $ProfileRootPath,
        "--pretty"
    )

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
        }
    }
    catch {
        return [PSCustomObject]@{
            profileId = $null
            reasonCode = "launch_context_detection_failed"
            confidence = 0.0
            launchKind = "Unknown"
        }
    }
}

function Map-LiveTests {
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
                "LiveCreditsTests"
            )
        }
        "ROE" {
            return @(
                "LiveTacticalToggleWorkflowTests",
                "LiveHeroHelperWorkflowTests",
                "LiveRoeRuntimeHealthTests",
                "LiveCreditsTests"
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
                "LiveCreditsTests"
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
$summaryEntries = @($summaryRaw)
$liveTests = @()
foreach ($test in (Map-LiveTests -SummaryEntries $summaryEntries)) {
    $liveTests += $test
}
$relevantNames = Get-RelevantTestNames -SelectedScope $Scope
$relevantLiveTests = @($liveTests | Where-Object { $relevantNames -contains $_.name })
$processSnapshot = @()
foreach ($process in (Get-ProcessSnapshot)) {
    $processSnapshot += $process
}
$preferredProcess = Get-PreferredProcess -Snapshot $processSnapshot
$launchContext = Detect-LaunchContext -Process $preferredProcess -ProfileRootPath $ProfileRoot
$runtimeMode = Get-RuntimeMode -LiveTests $relevantLiveTests
$classification = Get-Classification -Relevant $relevantLiveTests -ProcessSnapshot $processSnapshot -SelectedScope $Scope

$nextAction = switch ($classification) {
    "passed" { "Attach bundle to issue and continue with fix or closure workflow." }
    "blocked_environment" { "Launch target SWFOC process and rerun validation." }
    "blocked_profile_mismatch" { "Relaunch with required STEAMMOD/MODPATH markers for selected scope and rerun." }
    "failed" { "Inspect failed/missing test artifacts and runtime diagnostics before retry." }
    default { "Review skipped reasons and gather additional live context for this scope." }
}

$bundle = [ordered]@{
    schemaVersion = "1.0"
    runId = $RunId
    startedAtUtc = if ([string]::IsNullOrWhiteSpace($StartedAtUtc)) { (Get-Date).ToUniversalTime().ToString("o") } else { $StartedAtUtc }
    scope = $Scope
    processSnapshot = @($processSnapshot)
    launchContext = $launchContext
    runtimeMode = $runtimeMode
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

@"
# Repro Bundle Summary

- runId: `$RunId`
- scope: `$Scope`
- classification: `$classification`
- launch profile: `$($launchContext.profileId)`
- launch reason: `$($launchContext.reasonCode)`
- confidence: `$($launchContext.confidence)`
- launch kind: `$($launchContext.launchKind)`
- runtime mode effective: `$($runtimeMode.effective)`
- runtime mode reason: `$($runtimeMode.reasonCode)`

## Process Snapshot

| PID | Name | SteamModIds | Command Line |
|---|---|---|---|
$((@($processSnapshot | ForEach-Object { "| $($_.pid) | $($_.name) | $(@($_.steamModIds) -join ',') | $([string]$_.commandLine).Replace('|','/') |" }) -join "`n"))

## Live Tests

| Test | Outcome | Pass/Fail/Skip | TRX | Message |
|---|---|---|---|---|
$($liveRows -join "`n")

## Next Action

$nextAction
"@ | Set-Content -Path $bundleMdPath

Write-Host "repro bundle json: $bundlePath"
Write-Host "repro bundle markdown: $bundleMdPath"
