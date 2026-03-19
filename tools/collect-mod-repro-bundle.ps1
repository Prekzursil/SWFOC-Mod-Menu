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
        (Join-Path ${env:ProgramFiles(x86)} "Python310\\python.exe"),
        (Join-Path $env:LocalAppData "Microsoft\\WindowsApps\\python.exe"),
        (Join-Path $env:LocalAppData "Microsoft\\WindowsApps\\python3.exe")
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

    $wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
    if ($null -eq $wsl) {
        $wsl = Get-Command wsl -ErrorAction SilentlyContinue
    }
    if ($null -ne $wsl) {
        $candidates.Add(@($wsl.Source, "-e", "python3"))
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

    return $Path
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
            ExitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
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

    $normalizedForcedWorkshopIds = @(ConvertTo-ForcedWorkshopIds -RawIds $ForcedWorkshopIds)
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

    $commandArgs = @()
    if ($pythonCmd.Count -gt 1) {
        $commandArgs += $pythonCmd[1..($pythonCmd.Count - 1)]
    }

    $detectScriptPath = Join-Path $repoRoot "tools/detect-launch-context.py"
    $profileRootArg = if ([System.IO.Path]::IsPathRooted($ProfileRootPath)) {
        $ProfileRootPath
    }
    else {
        Join-Path $repoRoot $ProfileRootPath
    }

    if (Test-IsWslPythonCommand -Command $pythonCmd) {
        $detectScriptPath = Convert-ToWslPath -Path $detectScriptPath
        $profileRootArg = Convert-ToWslPath -Path $profileRootArg
    }

    $commandArgs += @(
        $detectScriptPath,
        "--command-line", ([string]$Process.commandLine),
        "--process-name", ([string]$Process.name),
        "--process-path", ([string]$Process.path),
        "--profile-root", $profileRootArg
    )

    $forcedWorkshopIdsCsv = if ($normalizedForcedWorkshopIds.Count -eq 0) { "" } else { $normalizedForcedWorkshopIds -join "," }
    if (-not [string]::IsNullOrWhiteSpace($forcedWorkshopIdsCsv)) {
        $commandArgs += @("--force-workshop-ids", $forcedWorkshopIdsCsv)
    }

    if (-not [string]::IsNullOrWhiteSpace($ForcedProfileId)) {
        $commandArgs += @("--force-profile-id", $normalizedForcedProfileId)
    }

    $commandArgs += "--pretty"

    try {
        $commandResult = Invoke-CapturedCommand -Command $pythonCmd -Arguments $commandArgs
        if ([int]$commandResult.ExitCode -ne 0) {
            throw "detect-launch-context.py exited with $($commandResult.ExitCode)"
        }

        $raw = [string]$commandResult.Output
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

function Get-JsonMemberValue {
    param(
        [Parameter(Mandatory = $false)][object]$Object,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    if ($null -eq $Object) {
        return $null
    }

    foreach ($name in $Names) {
        if ($null -ne $Object.PSObject.Properties[$name]) {
            return $Object.$name
        }
    }

    return $null
}

function ConvertTo-NullableBoolean {
    param([Parameter(Mandatory = $false)][object]$Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [bool]) {
        return [bool]$Value
    }

    if ($Value -is [string]) {
        $normalized = $Value.Trim()
        if ([string]::IsNullOrWhiteSpace($normalized)) {
            return $null
        }

        switch -Regex ($normalized.ToLowerInvariant()) {
            "^(true|1|yes|y|on)$" { return $true }
            "^(false|0|no|n|off)$" { return $false }
            default { return $null }
        }
    }

    if ($Value -is [sbyte] -or
        $Value -is [byte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]) {
        return ([long]$Value) -ne 0
    }

    if ($Value -is [single] -or $Value -is [double] -or $Value -is [decimal]) {
        return ([double]$Value) -ne 0
    }

    return $null
}

function ConvertTo-BooleanOrDefault {
    param(
        [Parameter(Mandatory = $false)][object]$Value,
        [Parameter(Mandatory = $false)][bool]$Default = $false
    )

    $parsed = ConvertTo-NullableBoolean -Value $Value
    if ($null -eq $parsed) {
        return $Default
    }

    return [bool]$parsed
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
            $profileId = Get-JsonMemberValue -Object $entry -Names @("profileId", "ProfileId")
            $actionId = Get-JsonMemberValue -Object $entry -Names @("actionId", "ActionId")
            $outcome = Get-JsonMemberValue -Object $entry -Names @("outcome", "Outcome")
            $backendRouteRaw = Get-JsonMemberValue -Object $entry -Names @("backendRoute", "BackendRoute")
            $routeReasonCodeRaw = Get-JsonMemberValue -Object $entry -Names @("routeReasonCode", "RouteReasonCode")
            $capabilityProbeReasonCodeRaw = Get-JsonMemberValue -Object $entry -Names @("capabilityProbeReasonCode", "CapabilityProbeReasonCode")
            $hybridExecutionRaw = Get-JsonMemberValue -Object $entry -Names @("hybridExecution", "HybridExecution")
            $hasFallbackMarkerRaw = Get-JsonMemberValue -Object $entry -Names @("hasFallbackMarker", "HasFallbackMarker")
            $messageRaw = Get-JsonMemberValue -Object $entry -Names @("message", "Message")
            $skipReasonCodeRaw = Get-JsonMemberValue -Object $entry -Names @("skipReasonCode", "SkipReasonCode")

            $entries.Add([PSCustomObject]@{
                profileId = [string]$profileId
                actionId = [string]$actionId
                outcome = [string]$outcome
                backendRoute = if ([string]::IsNullOrWhiteSpace([string]$backendRouteRaw)) { $null } else { [string]$backendRouteRaw }
                routeReasonCode = if ([string]::IsNullOrWhiteSpace([string]$routeReasonCodeRaw)) { $null } else { [string]$routeReasonCodeRaw }
                capabilityProbeReasonCode = if ([string]::IsNullOrWhiteSpace([string]$capabilityProbeReasonCodeRaw)) { $null } else { [string]$capabilityProbeReasonCodeRaw }
                hybridExecution = ConvertTo-NullableBoolean -Value $hybridExecutionRaw
                hasFallbackMarker = ConvertTo-BooleanOrDefault -Value $hasFallbackMarkerRaw -Default $false
                message = [string]$messageRaw
                skipReasonCode = if ([string]::IsNullOrWhiteSpace([string]$skipReasonCodeRaw)) { $null } else { [string]$skipReasonCodeRaw }
            })
        }

        $entryArray = @($entries.ToArray())
        $derivedSummary = [ordered]@{
            total = @($entryArray).Count
            passed = @($entryArray | Where-Object { $_.outcome -eq "Passed" }).Count
            failed = @($entryArray | Where-Object { $_.outcome -eq "Failed" }).Count
            skipped = @($entryArray | Where-Object { $_.outcome -eq "Skipped" }).Count
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
            entries = $entryArray
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
            effective = "AnyTactical"
            reasonCode = "tactical_test_passed"
        }
    }

    $message = ([string]$tactical.message).ToLowerInvariant()
    if ($message.Contains("runtime mode is tacticalland")) {
        return [PSCustomObject]@{
            hint = "TacticalLand"
            effective = "TacticalLand"
            reasonCode = "tactical_skip_mode_land"
        }
    }

    if ($message.Contains("runtime mode is tacticalspace")) {
        return [PSCustomObject]@{
            hint = "TacticalSpace"
            effective = "TacticalSpace"
            reasonCode = "tactical_skip_mode_space"
        }
    }

    if ($message.Contains("runtime mode is anytactical")) {
        return [PSCustomObject]@{
            hint = "AnyTactical"
            effective = "AnyTactical"
            reasonCode = "tactical_skip_mode_any_tactical"
        }
    }

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

    if ($relevantItems.Count -eq 0) {
        return "failed"
    }

    $allSkipped = (@($relevantItems | Where-Object { $_.outcome -eq "Skipped" })).Count -eq $relevantItems.Count
    if ($allSkipped) {
        $messages = ($relevantItems | ForEach-Object { ([string]$_.message).ToLowerInvariant() }) -join " "
        if ($messages.Contains("parent_dependency_missing")) {
            return "blocked_dependency_missing_parent"
        }
    }

    if ($processItems.Count -eq 0) {
        return "blocked_environment"
    }

    if (@($relevantItems | Where-Object { $_.outcome -eq "Failed" -or $_.outcome -eq "Missing" }).Count -gt 0) {
        return "failed"
    }

    $allPassed = (@($relevantItems | Where-Object { $_.outcome -eq "Passed" })).Count -eq $relevantItems.Count
    if ($allPassed) {
        return "passed"
    }

    if ($allSkipped) {
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

function Get-ResolvedSubmodChain {
    param(
        [object]$LaunchContext,
        [object]$PreferredProcess
    )

    $forcedIds = @($LaunchContext.forcedWorkshopIds)
    if ($forcedIds.Count -gt 0) {
        return @($forcedIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    if ($null -ne $PreferredProcess) {
        $steamIds = @($PreferredProcess.steamModIds)
        if ($steamIds.Count -gt 0) {
            return @($steamIds | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
    }

    return @()
}

function Get-InstalledModContext {
    param(
        [object[]]$ProcessSnapshot,
        [object]$LaunchContext,
        [string]$RunDirectoryPath
    )

    $installed = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($proc in $ProcessSnapshot) {
        foreach ($id in @($proc.steamModIds)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$id)) {
                [void]$installed.Add(([string]$id))
            }
        }
    }

    foreach ($id in @($LaunchContext.forcedWorkshopIds)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$id)) {
            [void]$installed.Add(([string]$id))
        }
    }

    $source = "process_snapshot"
    $testResultsRoot = Split-Path -Parent (Split-Path -Parent $RunDirectoryPath)
    $runName = Split-Path -Leaf $RunDirectoryPath
    $graphPath = Join-Path $testResultsRoot (Join-Path "mod-discovery" (Join-Path $runName "installed-mod-graph.json"))
    if (Test-Path -Path $graphPath) {
        try {
            $graph = Get-Content -Raw -Path $graphPath | ConvertFrom-Json
            foreach ($item in @($graph.items)) {
                $id = [string](Get-JsonMemberValue -Object $item -Names @("workshopId", "WorkshopId"))
                if (-not [string]::IsNullOrWhiteSpace($id)) {
                    [void]$installed.Add($id)
                }
            }
            $source = "installed_mod_graph"
        }
        catch {
            $source = "process_snapshot"
        }
    }

    return [ordered]@{
        source = $source
        installedWorkshopIds = @($installed | Sort-Object)
        installedCount = [int]$installed.Count
        launchProfileId = [string]$LaunchContext.profileId
    }
}

function Get-MechanicGatingSummary {
    param(
        [object]$ActionStatusDiagnostics,
        [string]$HelperBridgeState
    )

    $entries = @($ActionStatusDiagnostics.entries)
    $blocked = @(
        $entries | Where-Object {
            $routeReason = [string]$_.routeReasonCode
            $skipReason = [string]$_.skipReasonCode
            ($_.outcome -eq "Failed") -or
            ($routeReason -like "CAPABILITY_*") -or
            ($routeReason -like "HELPER_*") -or
            (-not [string]::IsNullOrWhiteSpace($skipReason))
        }
    )
    $blockedIds = @($blocked | ForEach-Object { [string]$_.actionId } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

    return [ordered]@{
        helperBridgeState = $HelperBridgeState
        blockedActionCount = [int]$blockedIds.Count
        blockedActions = $blockedIds
        summary = [string]$ActionStatusDiagnostics.status
        totalActions = [int]$ActionStatusDiagnostics.summary.total
        passedActions = [int]$ActionStatusDiagnostics.summary.passed
        failedActions = [int]$ActionStatusDiagnostics.summary.failed
        skippedActions = [int]$ActionStatusDiagnostics.summary.skipped
    }
}

function ConvertTo-StringArray {
    param([object]$Value)

    if ($null -eq $Value) {
        return [string[]]@()
    }

    if ($Value -is [string]) {
        if ([string]::IsNullOrWhiteSpace($Value)) {
            return [string[]]@()
        }

        $parts = @($Value -split "," | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
        return [string[]]$parts
    }

    $output = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($Value)) {
        $text = [string]$item
        if (-not [string]::IsNullOrWhiteSpace($text)) {
            [void]$output.Add($text.Trim())
        }
    }

    return [string[]]$output.ToArray()
}

function Get-EntityOperationSummary {
    param([object]$ActionStatusDiagnostics)

    $entityActionIds = @(
        "spawn_context_entity",
        "spawn_tactical_entity",
        "spawn_galactic_entity",
        "place_planet_building",
        "set_context_allegiance",
        "set_context_faction"
    )

    $entries = @($ActionStatusDiagnostics.entries | Where-Object { $entityActionIds -contains [string]$_.actionId })
    $operations = @()
    foreach ($entry in $entries) {
        $actionId = [string]$entry.actionId
        $persistencePolicy = "unknown"
        $populationPolicy = "unknown"
        $allowCrossFaction = "unknown"
        $forceOverride = "unknown"

        switch -Regex ($actionId) {
            "^spawn_(context|tactical)_entity$" {
                $persistencePolicy = "EphemeralBattleOnly"
                $populationPolicy = "ForceZeroTactical"
                $allowCrossFaction = "true"
                $forceOverride = "false"
            }
            "^spawn_galactic_entity$" {
                $persistencePolicy = "PersistentGalactic"
                $populationPolicy = "Normal"
                $allowCrossFaction = "true"
                $forceOverride = "false"
            }
            "^place_planet_building$" {
                $persistencePolicy = "PersistentGalactic"
                $populationPolicy = "Normal"
                $allowCrossFaction = "true"
                $forceOverride = "false"
            }
            "^set_context_(allegiance|faction)$" {
                $persistencePolicy = "context_routed"
                $populationPolicy = "n/a"
                $allowCrossFaction = "true"
                $forceOverride = "false"
            }
        }

        $operations += [ordered]@{
            actionId = $actionId
            outcome = [string]$entry.outcome
            persistencePolicy = $persistencePolicy
            populationPolicy = $populationPolicy
            allowCrossFaction = $allowCrossFaction
            forceOverride = $forceOverride
        }
    }

    return [ordered]@{
        totalActions = [int]$operations.Count
        passedActions = [int](@($operations | Where-Object { $_.outcome -eq "Passed" }).Count)
        failedActions = [int](@($operations | Where-Object { $_.outcome -eq "Failed" -or $_.outcome -eq "Missing" }).Count)
        skippedActions = [int](@($operations | Where-Object { $_.outcome -eq "Skipped" }).Count)
        operations = @($operations)
    }
}

function Get-TransplantSummary {
    param(
        [object]$ActionStatusDiagnostics,
        [object]$RuntimeResult
    )

    $reasonCodes = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($ActionStatusDiagnostics.entries)) {
        foreach ($candidate in @([string]$entry.routeReasonCode, [string]$entry.skipReasonCode, [string]$entry.capabilityProbeReasonCode)) {
            if ([string]::IsNullOrWhiteSpace($candidate)) {
                continue
            }

            if ($candidate -like "TRANSPLANT_*" -or $candidate -eq "CROSS_MOD_TRANSPLANT_REQUIRED" -or $candidate -eq "ROSTER_VISUAL_MISSING") {
                [void]$reasonCodes.Add($candidate)
            }
        }
    }

    $blockingEntityIds = @(ConvertTo-StringArray -Value (Get-JsonMemberValue -Object $RuntimeResult -Names @("transplantBlockingEntityIds", "TransplantBlockingEntityIds")))
    if ($blockingEntityIds.Count -eq 0) {
        $blockingEntityIds = @(
            $ActionStatusDiagnostics.entries |
                Where-Object { ([string]$_.routeReasonCode) -eq "CROSS_MOD_TRANSPLANT_REQUIRED" -or ([string]$_.routeReasonCode) -like "TRANSPLANT_*" } |
                ForEach-Object { [string]$_.actionId } |
                Sort-Object -Unique
        )
    }

    $enabled = ConvertTo-BooleanOrDefault -Value (Get-JsonMemberValue -Object $RuntimeResult -Names @("transplantEnabled", "TransplantEnabled")) -Default $true
    $allResolvedRaw = ConvertTo-NullableBoolean -Value (Get-JsonMemberValue -Object $RuntimeResult -Names @("transplantAllResolved", "TransplantAllResolved"))
    $allResolved = if ($null -eq $allResolvedRaw) { $reasonCodes.Count -eq 0 } else { [bool]$allResolvedRaw }
    $blockingCountRaw = Get-JsonMemberValue -Object $RuntimeResult -Names @("transplantBlockingEntityCount", "TransplantBlockingEntityCount")
    $blockingCount = if ($null -eq $blockingCountRaw -or [string]::IsNullOrWhiteSpace([string]$blockingCountRaw)) {
        [int]$blockingEntityIds.Count
    }
    else {
        [int]$blockingCountRaw
    }

    return [ordered]@{
        enabled = $enabled
        allResolved = $allResolved
        blockingEntityCount = $blockingCount
        blockingEntityIds = @($blockingEntityIds)
        reasonCodes = @($reasonCodes | Sort-Object)
    }
}

function Get-RosterVisualCoverage {
    param(
        [object]$ActionStatusDiagnostics,
        [object]$RuntimeResult,
        [object]$TransplantSummary
    )

    $missingEntityIds = @(ConvertTo-StringArray -Value (Get-JsonMemberValue -Object $RuntimeResult -Names @("rosterVisualMissingEntityIds", "RosterVisualMissingEntityIds")))
    if ($missingEntityIds.Count -eq 0) {
        $missingEntityIds = @(
            $ActionStatusDiagnostics.entries |
                Where-Object { ([string]$_.routeReasonCode) -eq "ROSTER_VISUAL_MISSING" } |
                ForEach-Object { [string]$_.actionId } |
                Sort-Object -Unique
        )
    }

    $totalEntitiesRaw = Get-JsonMemberValue -Object $RuntimeResult -Names @("rosterEntityCount", "RosterEntityCount")
    $totalEntities = if ($null -eq $totalEntitiesRaw -or [string]::IsNullOrWhiteSpace([string]$totalEntitiesRaw)) {
        [int]([Math]::Max($TransplantSummary.blockingEntityCount, $missingEntityIds.Count))
    }
    else {
        [int]$totalEntitiesRaw
    }

    if ($totalEntities -lt $missingEntityIds.Count) {
        $totalEntities = $missingEntityIds.Count
    }

    $visualMissingCount = [int]$missingEntityIds.Count
    $visualResolvedCount = [int]([Math]::Max(0, $totalEntities - $visualMissingCount))

    return [ordered]@{
        totalEntities = $totalEntities
        visualResolvedCount = $visualResolvedCount
        visualMissingCount = $visualMissingCount
        missingEntityIds = @($missingEntityIds)
    }
}

function Get-AllegianceRoutingSummary {
    param([object]$ActionStatusDiagnostics)

    $entries = @(
        $ActionStatusDiagnostics.entries |
            Where-Object { ([string]$_.actionId) -eq "set_context_allegiance" -or ([string]$_.actionId) -eq "set_context_faction" }
    )
    $reasons = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        foreach ($candidate in @([string]$entry.routeReasonCode, [string]$entry.skipReasonCode)) {
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                [void]$reasons.Add($candidate)
            }
        }
    }

    return [ordered]@{
        totalActions = [int]$entries.Count
        routedActions = [int](@($entries | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.backendRoute) }).Count)
        blockedActions = [int](@($entries | Where-Object { ([string]$_.outcome) -eq "Failed" -or ([string]$_.outcome) -eq "Skipped" -or ([string]$_.outcome) -eq "Missing" }).Count)
        reasonCodes = @($reasons | Sort-Object)
    }
}

function Get-HeroMechanicsSummary {
    param(
        [object]$RuntimeResult,
        [object]$ActionStatusDiagnostics,
        [string]$ProfileId
    )

    $runtimeSummary = Get-JsonMemberValue -Object $RuntimeResult -Names @("heroMechanicsSummary", "HeroMechanicsSummary")
    if ($null -ne $runtimeSummary) {
        return [ordered]@{
            supportsRespawn = ConvertTo-BooleanOrDefault -Value (Get-JsonMemberValue -Object $runtimeSummary -Names @("supportsRespawn", "SupportsRespawn")) -Default $false
            supportsPermadeath = ConvertTo-BooleanOrDefault -Value (Get-JsonMemberValue -Object $runtimeSummary -Names @("supportsPermadeath", "SupportsPermadeath")) -Default $false
            supportsRescue = ConvertTo-BooleanOrDefault -Value (Get-JsonMemberValue -Object $runtimeSummary -Names @("supportsRescue", "SupportsRescue")) -Default $false
            defaultRespawnTime = Get-JsonMemberValue -Object $runtimeSummary -Names @("defaultRespawnTime", "DefaultRespawnTime")
            duplicateHeroPolicy = [string](Get-JsonMemberValue -Object $runtimeSummary -Names @("duplicateHeroPolicy", "DuplicateHeroPolicy"))
            respawnExceptionSources = @(ConvertTo-StringArray -Value (Get-JsonMemberValue -Object $runtimeSummary -Names @("respawnExceptionSources", "RespawnExceptionSources")))
        }
    }

    $entries = @($ActionStatusDiagnostics.entries)
    $actionIds = @($entries | ForEach-Object { [string]$_.actionId })
    $supportsRespawn = @($actionIds | Where-Object { $_ -eq "set_hero_state_helper" -or $_ -eq "toggle_roe_respawn_helper" -or $_ -eq "edit_hero_state" }).Count -gt 0
    $supportsRescue = $ProfileId -like "aotr_*"
    $supportsPermadeath = $ProfileId -like "roe_*"

    return [ordered]@{
        supportsRespawn = $supportsRespawn
        supportsPermadeath = $supportsPermadeath
        supportsRescue = $supportsRescue
        defaultRespawnTime = $null
        duplicateHeroPolicy = if ($supportsPermadeath) { "mod_defined_permadeath" } elseif ($supportsRescue) { "rescue_or_respawn" } else { "mod_defined" }
        respawnExceptionSources = @()
    }
}

function Get-OperationPolicySummary {
    param([object]$EntityOperationSummary)

    $operations = @($EntityOperationSummary.operations)
    return [ordered]@{
        tacticalEphemeralCount = [int](@($operations | Where-Object { [string]$_.persistencePolicy -eq "EphemeralBattleOnly" }).Count)
        galacticPersistentCount = [int](@($operations | Where-Object { [string]$_.persistencePolicy -eq "PersistentGalactic" }).Count)
        crossFactionEnabledCount = [int](@($operations | Where-Object { [string]$_.allowCrossFaction -eq "true" }).Count)
        forceOverrideCount = [int](@($operations | Where-Object { [string]$_.forceOverride -eq "true" }).Count)
    }
}

function Get-FleetTransferSafetySummary {
    param([object]$ActionStatusDiagnostics)

    $entries = @($ActionStatusDiagnostics.entries | Where-Object { ([string]$_.actionId) -eq "transfer_fleet_safe" })
    $reasons = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        foreach ($candidate in @([string]$entry.routeReasonCode, [string]$entry.skipReasonCode)) {
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                [void]$reasons.Add($candidate)
            }
        }
    }

    return [ordered]@{
        totalActions = [int]$entries.Count
        safeTransfers = [int](@($entries | Where-Object { ([string]$_.outcome) -eq "Passed" }).Count)
        blockedTransfers = [int](@($entries | Where-Object { ([string]$_.outcome) -ne "Passed" }).Count)
        reasonCodes = @($reasons | Sort-Object)
    }
}

function Get-PlanetFlipSummary {
    param([object]$ActionStatusDiagnostics)

    $entries = @($ActionStatusDiagnostics.entries | Where-Object { ([string]$_.actionId) -eq "flip_planet_owner" })
    $reasons = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    $emptyRetreatCount = 0
    $convertEverythingCount = 0
    foreach ($entry in $entries) {
        $message = [string]$entry.message
        if ($message -match "empty" -or $message -match "retreat") {
            $emptyRetreatCount++
        }
        elseif (($entry.outcome -eq "Passed")) {
            $convertEverythingCount++
        }

        foreach ($candidate in @([string]$entry.routeReasonCode, [string]$entry.skipReasonCode)) {
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                [void]$reasons.Add($candidate)
            }
        }
    }

    return [ordered]@{
        totalActions = [int]$entries.Count
        emptyRetreatCount = [int]$emptyRetreatCount
        convertEverythingCount = [int]$convertEverythingCount
        blockedActions = [int](@($entries | Where-Object { ([string]$_.outcome) -ne "Passed" }).Count)
        reasonCodes = @($reasons | Sort-Object)
    }
}

function Get-EntityTransplantBlockers {
    param([object]$TransplantSummary)

    $blockingEntityIds = @(ConvertTo-StringArray -Value $TransplantSummary.blockingEntityIds)
    return [ordered]@{
        hasBlockers = [bool](-not [bool]$TransplantSummary.allResolved -or $blockingEntityIds.Count -gt 0)
        blockingEntityCount = [int]$blockingEntityIds.Count
        blockingEntityIds = @($blockingEntityIds)
        reasonCodes = @(ConvertTo-StringArray -Value $TransplantSummary.reasonCodes)
    }
}

if (-not (Test-Path -Path $SummaryPath)) {
    throw "Summary path not found: $SummaryPath"
}

if (-not (Test-Path -Path $RunDirectory)) {
    New-Item -Path $RunDirectory -ItemType Directory | Out-Null
}

$summaryRaw = Get-Content -Raw -Path $SummaryPath | ConvertFrom-Json
$forceWorkshopIdsNormalized = @(ConvertTo-ForcedWorkshopIds -RawIds $ForceWorkshopIds)
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
$resolvedSubmodChain = @(Get-ResolvedSubmodChain -LaunchContext $launchContext -PreferredProcess $preferredProcess)
$installedModContext = Get-InstalledModContext `
    -ProcessSnapshot $processSnapshot `
    -LaunchContext $launchContext `
    -RunDirectoryPath $RunDirectory
$runtimeMode = Get-RuntimeMode -LiveTests $relevantLiveTests
$classification = Get-Classification -Relevant $relevantLiveTests -ProcessSnapshot $processSnapshot -SelectedScope $Scope
$requiredCapabilities = Get-ProfileRequiredCapabilities -ProfileRootPath $ProfileRoot -ProfileId ([string]$launchContext.profileId)
$runtimeEvidence = Get-RuntimeEvidence -RunDirectoryPath $RunDirectory
$runtimeResult = Get-JsonMemberValue -Object $runtimeEvidence -Names @("result", "Result")
$actionStatusDiagnostics = Get-ActionStatusDiagnostics -RunDirectoryPath $RunDirectory

$nextAction = switch ($classification) {
    "passed" { "Attach bundle to issue and continue with fix or closure workflow." }
    "blocked_environment" { "Launch target SWFOC process and rerun validation." }
    "blocked_dependency_missing_parent" { "Install required parent workshop dependency chain (or remove forced orphan submod) and rerun validation." }
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
    $runtimeSucceeded = ConvertTo-BooleanOrDefault -Value (Get-JsonMemberValue -Object $runtimeResult -Names @("succeeded", "Succeeded")) -Default $false
    $runtimeBackendRoute = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("backendRoute", "BackendRoute"))
    $runtimeRouteReasonCode = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("routeReasonCode", "RouteReasonCode"))
    $runtimeMessage = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("message", "Message"))
    [ordered]@{
        backend = if ([string]::IsNullOrWhiteSpace($runtimeBackendRoute)) { "unknown" } else { $runtimeBackendRoute }
        allowed = $runtimeSucceeded
        reasonCode = if ([string]::IsNullOrWhiteSpace($runtimeRouteReasonCode)) { "UNKNOWN" } else { $runtimeRouteReasonCode }
        message = $runtimeMessage
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
    $runtimeProbeReasonCode = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("capabilityProbeReasonCode", "CapabilityProbeReasonCode"))
    [ordered]@{
        backend = "extender"
        probeReasonCode = if ([string]::IsNullOrWhiteSpace($runtimeProbeReasonCode)) { "CAPABILITY_UNKNOWN" } else { $runtimeProbeReasonCode }
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
    $runtimeSucceeded = ConvertTo-BooleanOrDefault -Value (Get-JsonMemberValue -Object $runtimeResult -Names @("succeeded", "Succeeded")) -Default $false
    $runtimeHookState = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("hookState", "HookState"))
    [ordered]@{
        state = if ([string]::IsNullOrWhiteSpace($runtimeHookState)) { "unknown" } else { $runtimeHookState }
        reasonCode = if ($runtimeSucceeded) { "CAPABILITY_PROBE_PASS" } else { "HOOK_INSTALL_FAILED" }
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

$helperBridgeState = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("helperBridgeState", "HelperBridgeState"))
if ([string]::IsNullOrWhiteSpace($helperBridgeState)) {
    $helperBridgeState = "unknown"
}

$helperEntryPoint = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("helperEntryPoint", "HelperEntryPoint"))
if ([string]::IsNullOrWhiteSpace($helperEntryPoint)) {
    $helperEntryPoint = ""
}

$helperInvocationSource = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("helperInvocationSource", "HelperInvocationSource"))
if ([string]::IsNullOrWhiteSpace($helperInvocationSource)) {
    $helperInvocationSource = "unknown"
}

$helperVerifyState = [string](Get-JsonMemberValue -Object $runtimeResult -Names @("helperVerifyState", "HelperVerifyState"))
if ([string]::IsNullOrWhiteSpace($helperVerifyState)) {
    $helperVerifyState = "unknown"
}
$mechanicGatingSummary = Get-MechanicGatingSummary `
    -ActionStatusDiagnostics $actionStatusDiagnostics `
    -HelperBridgeState $helperBridgeState
$entityOperationSummary = Get-EntityOperationSummary -ActionStatusDiagnostics $actionStatusDiagnostics
$transplantSummary = Get-TransplantSummary -ActionStatusDiagnostics $actionStatusDiagnostics -RuntimeResult $runtimeResult
$rosterVisualCoverage = Get-RosterVisualCoverage `
    -ActionStatusDiagnostics $actionStatusDiagnostics `
    -RuntimeResult $runtimeResult `
    -TransplantSummary $transplantSummary
$allegianceRoutingSummary = Get-AllegianceRoutingSummary -ActionStatusDiagnostics $actionStatusDiagnostics
$heroMechanicsSummary = Get-HeroMechanicsSummary `
    -RuntimeResult $runtimeResult `
    -ActionStatusDiagnostics $actionStatusDiagnostics `
    -ProfileId ([string]$launchContext.profileId)
$operationPolicySummary = Get-OperationPolicySummary -EntityOperationSummary $entityOperationSummary
$fleetTransferSafetySummary = Get-FleetTransferSafetySummary -ActionStatusDiagnostics $actionStatusDiagnostics
$planetFlipSummary = Get-PlanetFlipSummary -ActionStatusDiagnostics $actionStatusDiagnostics
$entityTransplantBlockers = Get-EntityTransplantBlockers -TransplantSummary $transplantSummary

$bundle = [ordered]@{
    schemaVersion = "1.3"
    runId = $RunId
    startedAtUtc = if ([string]::IsNullOrWhiteSpace($StartedAtUtc)) { (Get-Date).ToUniversalTime().ToString("o") } else { $StartedAtUtc }
    scope = $Scope
    processSnapshot = @($processSnapshot)
    launchContext = $launchContext
    installedModContext = $installedModContext
    resolvedSubmodChain = @($resolvedSubmodChain)
    mechanicGatingSummary = $mechanicGatingSummary
    entityOperationSummary = $entityOperationSummary
    transplantSummary = $transplantSummary
    rosterVisualCoverage = $rosterVisualCoverage
    allegianceRoutingSummary = $allegianceRoutingSummary
    heroMechanicsSummary = $heroMechanicsSummary
    operationPolicySummary = $operationPolicySummary
    fleetTransferSafetySummary = $fleetTransferSafetySummary
    planetFlipSummary = $planetFlipSummary
    entityTransplantBlockers = $entityTransplantBlockers
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
        helperReadiness = $helperBridgeState
        helperBridgeState = $helperBridgeState
        helperEntryPoint = $helperEntryPoint
        helperInvocationSource = $helperInvocationSource
        helperVerifyState = $helperVerifyState
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
    $backendRoute = if ($null -eq $_.backendRoute) { "n/a" } else { [string]$_.backendRoute }
    $routeReasonCode = if ($null -eq $_.routeReasonCode) { "n/a" } else { [string]$_.routeReasonCode }
    $capabilityProbeReasonCode = if ($null -eq $_.capabilityProbeReasonCode) { "n/a" } else { [string]$_.capabilityProbeReasonCode }
    $hybridExecution = if ($null -eq $_.hybridExecution) { "n/a" } else { [string]$_.hybridExecution }

    "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
        ([string]$_.profileId), `
        ([string]$_.actionId), `
        ([string]$_.outcome), `
        $backendRoute, `
        $routeReasonCode, `
        $capabilityProbeReasonCode, `
        $hybridExecution, `
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
- installed mod context: source=$($installedModContext.source) count=$($installedModContext.installedCount)
- resolved submod chain: $((@($resolvedSubmodChain) -join ','))
- runtime mode effective: $($runtimeMode.effective)
- runtime mode reason: $($runtimeMode.reasonCode)
- selected host: $($selectedHostProcess.name) (pid=$($selectedHostProcess.pid), role=$($selectedHostProcess.hostRole), score=$($selectedHostProcess.selectionScore))
- backend route: $($backendRouteDecision.backend) ($($backendRouteDecision.reasonCode))
- capability probe: $($capabilityProbeSnapshot.backend) ($($capabilityProbeSnapshot.probeReasonCode), required=$((@($capabilityProbeSnapshot.requiredCapabilities) -join ', ')))
- overlay: available=$($overlayState.available) visible=$($overlayState.visible) ($($overlayState.reasonCode))
- helper ingress: state=$helperBridgeState entryPoint=$helperEntryPoint source=$helperInvocationSource verify=$helperVerifyState
- mechanic gating summary: blocked=$($mechanicGatingSummary.blockedActionCount) helperState=$($mechanicGatingSummary.helperBridgeState) blockedActions=$((@($mechanicGatingSummary.blockedActions) -join ','))
- entity operations: total=$($entityOperationSummary.totalActions) passed=$($entityOperationSummary.passedActions) failed=$($entityOperationSummary.failedActions) skipped=$($entityOperationSummary.skippedActions)
- transplant summary: enabled=$($transplantSummary.enabled) allResolved=$($transplantSummary.allResolved) blocking=$($transplantSummary.blockingEntityCount) reasons=$((@($transplantSummary.reasonCodes) -join ','))
- roster visual coverage: total=$($rosterVisualCoverage.totalEntities) resolved=$($rosterVisualCoverage.visualResolvedCount) missing=$($rosterVisualCoverage.visualMissingCount)
- allegiance routing summary: total=$($allegianceRoutingSummary.totalActions) routed=$($allegianceRoutingSummary.routedActions) blocked=$($allegianceRoutingSummary.blockedActions) reasons=$((@($allegianceRoutingSummary.reasonCodes) -join ','))
- hero mechanics summary: respawn=$($heroMechanicsSummary.supportsRespawn) permadeath=$($heroMechanicsSummary.supportsPermadeath) rescue=$($heroMechanicsSummary.supportsRescue) defaultRespawn=$($heroMechanicsSummary.defaultRespawnTime) duplicatePolicy=$($heroMechanicsSummary.duplicateHeroPolicy)
- operation policy summary: tacticalEphemeral=$($operationPolicySummary.tacticalEphemeralCount) galacticPersistent=$($operationPolicySummary.galacticPersistentCount) crossFactionEnabled=$($operationPolicySummary.crossFactionEnabledCount) forceOverride=$($operationPolicySummary.forceOverrideCount)
- fleet transfer safety summary: total=$($fleetTransferSafetySummary.totalActions) safe=$($fleetTransferSafetySummary.safeTransfers) blocked=$($fleetTransferSafetySummary.blockedTransfers) reasons=$((@($fleetTransferSafetySummary.reasonCodes) -join ','))
- planet flip summary: total=$($planetFlipSummary.totalActions) emptyRetreat=$($planetFlipSummary.emptyRetreatCount) convertEverything=$($planetFlipSummary.convertEverythingCount) blocked=$($planetFlipSummary.blockedActions)
- transplant blockers: hasBlockers=$($entityTransplantBlockers.hasBlockers) count=$($entityTransplantBlockers.blockingEntityCount) ids=$((@($entityTransplantBlockers.blockingEntityIds) -join ','))
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
