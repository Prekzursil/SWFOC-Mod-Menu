param(
    [string]$Configuration = "Release",
    [string]$ResultsDirectory = "TestResults",
    [string]$ProfileRoot = "profiles/default",
    [switch]$NoBuild = $true,
    [string]$RunId = "",
    [ValidateSet("AOTR", "ROE", "TACTICAL", "FULL")][string]$Scope = "FULL",
    [bool]$EmitReproBundle = $true,
    [switch]$FailOnMissingArtifacts,
    [switch]$Strict
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

function Invoke-LiveTest {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Filter,
        [Parameter(Mandatory = $true)][string]$TrxName
    )

    Write-Host "=== Running $Name ==="

    $args = @(
        "test",
        "tests/SwfocTrainer.Tests/SwfocTrainer.Tests.csproj",
        "-c", $Configuration,
        "--filter", $Filter,
        "--logger", "trx;LogFileName=$TrxName",
        "--results-directory", $runResultsDirectory
    )

    if ($NoBuild) {
        $args += "--no-build"
    }

    & $dotnetExe @args

    $exitCode = if (Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue) { [int]$global:LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "dotnet test failed for '$Name' with exit code $exitCode"
    }

    return Join-Path $runResultsDirectory $TrxName
}

function Read-TrxSummary {
    param([Parameter(Mandatory = $true)][string]$TrxPath)

    if (-not (Test-Path -Path $TrxPath)) {
        return [PSCustomObject]@{
            Trx = $TrxPath
            Outcome = "Missing"
            Passed = 0
            Failed = 0
            Skipped = 0
            Message = "TRX file not found"
        }
    }

    [xml]$doc = Get-Content -Raw -Path $TrxPath
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
        Trx = $TrxPath
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
    }
    catch {
        Write-Warning ("Repro bundle generation/validation failed: {0}" -f $_.Exception.Message)
        $fatalError = $_.Exception
    }
}

Write-Host ""
Write-Host "=== Live Validation Summary ($iso) ==="
Write-Host "run id: $RunId"
Write-Host "scope: $Scope"
foreach ($entry in $summaries) {
    $s = $entry.Summary
    Write-Host ("{0}: outcome={1} passed={2} failed={3} skipped={4} message='{5}'" -f $entry.Name, $s.Outcome, $s.Passed, $s.Failed, $s.Skipped, $s.Message)
}
Write-Host "launch-context fixture: $launchContextJson"
Write-Host "summary json: $summaryPath"
if ($EmitReproBundle) {
    Write-Host "repro bundle json: $bundlePath"
    Write-Host "repro bundle markdown: $bundleMdPath"
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

Write-Host "issue template (34): $template34"
Write-Host "issue template (19): $template19"

if ($null -ne $fatalError) {
    throw $fatalError
}
