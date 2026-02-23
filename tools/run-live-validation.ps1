param(
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "TestResults",
    [string]$ProfileRoot = "profiles/default",
    [switch]$NoBuild = $true,
    [string]$RunId = "",
    [ValidateSet("AOTR", "ROE", "TACTICAL", "FULL")][string]$Scope = "FULL",
    [bool]$EmitReproBundle = $true,
    [switch]$FailOnMissingArtifacts,
    [switch]$Strict,
    [switch]$RequireNonBlockedClassification
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
}

$runResultsDirectory = Join-Path $ResultsDirectory (Join-Path "runs" $RunId)
if (-not (Test-Path -Path $runResultsDirectory)) {
    New-Item -ItemType Directory -Path $runResultsDirectory -Force | Out-Null
}
$runResultsDirectory = (Resolve-Path -Path $runResultsDirectory).Path

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
                return @($candidate, "-3")
            }

            return @($candidate)
        }
    }

    return @()
}

function Should-RunTest {
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
        [Parameter(Mandatory = $true)][string]$TrxName
    )

    Write-Information "=== Running $Name ===" -InformationAction Continue

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
    $env:SWFOC_LIVE_OUTPUT_DIR = $runResultsDirectory
    $env:SWFOC_LIVE_TEST_NAME = $Name

    try {
        & $dotnetExe @dotnetArgs
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
    }

    $exitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "dotnet test failed for '$Name' with exit code $exitCode"
    }

    return Resolve-ArtifactPath -Path (Join-Path $runResultsDirectory $TrxName)
}

function Read-TrxSummary {
    param([Parameter(Mandatory = $true)][string]$TrxPath)

    $resolvedTrxPath = Resolve-ArtifactPath -Path $TrxPath
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

$dotnetExe = Resolve-DotnetCommand
$pythonCmd = @(Resolve-PythonCommand)
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
    },
    [PSCustomObject]@{
        Name = "Live ROE Runtime Health"
        TestName = "LiveRoeRuntimeHealthTests"
        Filter = "FullyQualifiedName~LiveRoeRuntimeHealthTests"
        TrxBase = "live-roe-health.trx"
        Scopes = @("ROE")
    },
    [PSCustomObject]@{
        Name = "Live Credits"
        TestName = "LiveCreditsTests"
        Filter = "FullyQualifiedName~LiveCreditsTests"
        TrxBase = "live-credits.trx"
        Scopes = @("AOTR", "ROE")
    }
)

$summaries = New-Object System.Collections.Generic.List[object]
$fatalError = $null

foreach ($test in $testDefinitions) {
    $trxPath = Join-Path $runResultsDirectory ("{0}-{1}" -f $RunId, $test.TrxBase)

    if (-not (Should-RunTest -Scopes $test.Scopes -SelectedScope $Scope)) {
        $summaries.Add([PSCustomObject]@{
            Name = $test.TestName
            Summary = [PSCustomObject]@{
                Trx = $trxPath
                Outcome = "Skipped"
                Passed = 0
                Failed = 0
                Skipped = 1
                Message = "scope_not_selected"
            }
        })
        continue
    }

    if ($null -ne $fatalError) {
        $summaries.Add([PSCustomObject]@{
            Name = $test.TestName
            Summary = [PSCustomObject]@{
                Trx = $trxPath
                Outcome = "Missing"
                Passed = 0
                Failed = 0
                Skipped = 0
                Message = "not_executed_due_to_prior_failure"
            }
        })
        continue
    }

    try {
        $executedTrx = Invoke-LiveTest -Name $test.Name -Filter $test.Filter -TrxName ("{0}-{1}" -f $RunId, $test.TrxBase)
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
            Summary = [PSCustomObject]@{
                Trx = $trxPath
                Outcome = "Failed"
                Passed = 0
                Failed = 1
                Skipped = 0
                Message = $fatalError.Message
            }
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
$pythonArgs = @(
    "tools/detect-launch-context.py",
    "--from-process-json", "tools/fixtures/launch_context_cases.json",
    "--profile-root", $ProfileRoot,
    "--pretty"
)

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
        $pythonInvocationArgs = @()
        if ($pythonCmd.Count -gt 1) {
            $pythonInvocationArgs += $pythonCmd[1..($pythonCmd.Count - 1)]
        }

        $pythonInvocationArgs += $pythonArgs
        $launchContextOutput = & $pythonCmd[0] @pythonInvocationArgs 2>&1
        $exitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
        $outputText = ($launchContextOutput | Out-String).Trim()

        if ($exitCode -ne 0) {
            throw ("python exited with code {0}. output: {1}" -f $exitCode, $outputText)
        }

        if ([string]::IsNullOrWhiteSpace($outputText)) {
            throw ("python produced no output. executable: {0}" -f $pythonCmd[0])
        }

        $launchContextOutput | Set-Content -Path $launchContextJson
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
- Diagnostics for degraded/unavailable actions: <fill>
- Repro bundle: $bundlePath
- Artifacts:
  - $($lineTactical.Trx)
  - $($lineHero.Trx)
  - $($lineRoe.Trx)
  - $($lineCredits.Trx)
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

| Profile | Attach summary | Tactical toggle workflow | Hero helper workflow | Result |
|---|---|---|---|---|
| aotr_1397421866_swfoc | <fill pid/mode/reasonCode> | <pass/fail/skip + reason> | <pass/fail/skip + reason> | <overall> |
| roe_3447786229_swfoc | <fill pid/mode/reasonCode> | <pass/fail/skip + reason> | <pass/fail/skip + reason> | <overall> |

Current local run snapshot:
- LiveTacticalToggleWorkflowTests: $($lineTactical.Outcome) ($($lineTactical.Message))
- LiveHeroHelperWorkflowTests: $($lineHero.Outcome) ($($lineHero.Message))
- LiveRoeRuntimeHealthTests: $($lineRoe.Outcome) ($($lineRoe.Message))
- LiveCreditsTests: $($lineCredits.Outcome) ($($lineCredits.Message))

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
