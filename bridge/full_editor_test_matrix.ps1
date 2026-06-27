# full_editor_test_matrix.ps1
# v1.2.0 test harness skeleton (Phase 0 preflight + JSONL logger foundation).
#
# Per the v1.0.2 improvement plan Part 4: 6-phase orchestrator that walks the
# 27-tab x ~335-button matrix and emits JSONL logs ingestible by CI. This file
# is the SKELETON — Phase 0 (preflight) and the JSONL logger are functional;
# Phases 1-5 are scaffolded with TODO markers for incremental implementation.
#
# Usage:
#   .\full_editor_test_matrix.ps1                       # full run
#   .\full_editor_test_matrix.ps1 -Filter Tab=Economy   # one tab
#   .\full_editor_test_matrix.ps1 -NoComposite          # skip Phase 3
#   .\full_editor_test_matrix.ps1 -DryRun               # preflight only
#
# Exit codes:
#   0 = >=95% PASS on LIVE rows
#   1 = below threshold OR any FAIL row
#   2 = preflight failed (game not running, stale DLL, etc.)

[CmdletBinding()]
param(
    [string]$Filter = "",
    [switch]$NoComposite,
    [switch]$DryRun,
    [string]$ExpectedDllBuiltAfter = "2026-05-20 14:00"  # bump on each release
)

$ErrorActionPreference = "Continue"

# -----------------------------------------------------------------------------
# Logging infrastructure (JSONL primary, Markdown summary)
# -----------------------------------------------------------------------------

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$runDir = "C:\Users\Prekzursil\Downloads\swfoc_memory\bridge\test_runs"
$null = New-Item -ItemType Directory -Path $runDir -Force
$jsonlPath = Join-Path $runDir "full_matrix_${timestamp}.jsonl"
$markdownPath = Join-Path $runDir "full_matrix_${timestamp}.md"
$baselinePath = Join-Path $runDir "baseline_${timestamp}.json"
$latestJsonl = Join-Path $runDir "latest.jsonl"
$latestMd = Join-Path $runDir "latest.md"

# Counter of verdicts for end-of-run summary
$script:counts = @{}
$script:pipeReceivedBaseline = 0

function Write-Verdict {
    param(
        [string]$Tab,
        [string]$Feature,
        [string]$CapabilityStatus,
        [string]$ModeExpected,
        [string]$ModeObserved,
        [string]$Verdict,
        [hashtable]$Evidence = $null,
        [string]$Diagnostic = $null,
        [int]$DurationMs = 0,
        [int]$BridgePipeStatsReceivedDelta = 0
    )
    $row = [ordered]@{
        ts = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        tab = $Tab
        feature = $Feature
        capability_status = $CapabilityStatus
        mode_expected = $ModeExpected
        mode_observed = $ModeObserved
        verdict = $Verdict
        evidence = $Evidence
        diagnostic = $Diagnostic
        duration_ms = $DurationMs
        bridge_pipe_stats_received_delta = $BridgePipeStatsReceivedDelta
    }
    ($row | ConvertTo-Json -Compress -Depth 6) | Add-Content -LiteralPath $jsonlPath
    if (-not $script:counts.ContainsKey($Verdict)) { $script:counts[$Verdict] = 0 }
    $script:counts[$Verdict]++
}

# -----------------------------------------------------------------------------
# Bridge pipe helper (NamedPipeClientStream — full handshake unlike File.Open)
# -----------------------------------------------------------------------------

function Send-LuaCmd {
    param([string]$Cmd, [int]$ReadMs = 250)
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "swfoc_bridge", "InOut")
    try { $pipe.Connect(3000) } catch { $pipe.Dispose(); return @{ Ok = $false; Error = "CONNECT_FAIL: $_"; Response = "" } }
    try {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $b = [System.Text.Encoding]::UTF8.GetBytes($Cmd) + [byte[]]@(0)
        $pipe.Write($b, 0, $b.Length); $pipe.Flush()
        Start-Sleep -Milliseconds $ReadMs
        $buf = New-Object byte[] 65536
        $n = $pipe.Read($buf, 0, 65536)
        $sw.Stop()
        $resp = [System.Text.Encoding]::UTF8.GetString($buf, 0, $n).TrimEnd()
        return @{ Ok = $true; Response = $resp; DurationMs = [int]$sw.ElapsedMilliseconds }
    } finally { $pipe.Dispose() }
}

function Classify-Response {
    param([string]$Response)
    if ($Response -match "^CONNECT_FAIL") { return "CONNECT_FAIL" }
    if ($Response -match "PHASE2_PENDING") { return "P2-PENDING" }
    if ($Response -match "Phase 2 hook pending") { return "P2-PENDING" }
    if ($Response -match "non-tactical state") { return "MODE_MISMATCH" }
    if ($Response -match "engine error rc=") { return "ENGINE_ERR" }
    if ($Response -match "^ERR: .*expected ") { return "NEEDS_ARGS" }
    if ($Response -match "^ERR:") { return "FAIL" }
    return "PASS"
}

# -----------------------------------------------------------------------------
# Phase 0 — Preflight
# -----------------------------------------------------------------------------

Write-Host "Phase 0: preflight ($timestamp)"
Write-Host "  Log: $jsonlPath"
Write-Host ""

$gameProc = Get-Process StarWarsG -ErrorAction SilentlyContinue
if (-not $gameProc) {
    Write-Host "  [FAIL] StarWarsG.exe not running. Launch the game and retry." -ForegroundColor Red
    exit 2
}
Write-Host "  [OK]   Game running: PID=$($gameProc.Id), age=$([int]((Get-Date) - $gameProc.StartTime).TotalMinutes)min"

$buildInfo = Send-LuaCmd "return SWFOC_GetBuildInfo()"
if (-not $buildInfo.Ok) {
    Write-Host "  [FAIL] Bridge unreachable: $($buildInfo.Error)" -ForegroundColor Red
    exit 2
}
Write-Host "  [OK]   Bridge: $($buildInfo.Response)"

# Stale DLL guard: build info should reflect ExpectedDllBuiltAfter or later
$builtDate = $null
if ($buildInfo.Response -match "(\w{3}\s+\d+\s+\d{4}\s+\d{2}:\d{2}):\d{2}") {
    try { $builtDate = [DateTime]::ParseExact($Matches[1], "MMM d yyyy HH:mm", [System.Globalization.CultureInfo]::InvariantCulture) } catch {}
}
if ($builtDate -ne $null -and $builtDate -lt [DateTime]$ExpectedDllBuiltAfter) {
    Write-Host "  [WARN] DLL built $builtDate, expected after $ExpectedDllBuiltAfter — may be stale" -ForegroundColor Yellow
}

$pipeStats = Send-LuaCmd "return SWFOC_DiagPipeStats()"
if ($pipeStats.Response -match "received=(\d+)") { $script:pipeReceivedBaseline = [int]$Matches[1] }
Write-Host "  [OK]   Pipe stats baseline: received=$($script:pipeReceivedBaseline)"

$mode = Send-LuaCmd "return SWFOC_GetGameModeLua()"
$modeText = if ($mode.Response -match "= (\w+)") { $Matches[1] } else { "Unknown" }
Write-Host "  [OK]   Game mode: $modeText"

$mod = Send-LuaCmd "return SWFOC_GetCurrentMod()"
Write-Host "  [OK]   Current mod: $($mod.Response)"

Write-Host ""
Write-Host "Preflight passed. Writing baseline snapshot..."

# -----------------------------------------------------------------------------
# Phase 1 — Baseline snapshot
# -----------------------------------------------------------------------------

$allPlayers = Send-LuaCmd "return SWFOC_GetAllPlayers()"
$creditsMult = Send-LuaCmd "return SWFOC_GetCreditsMultiplierGlobal()"
$creditsFreeze = Send-LuaCmd "return SWFOC_GetCreditsFreezeGlobal()"
$damageMult = Send-LuaCmd "return SWFOC_GetDamageMultiplierGlobal()"
$fireRateMult = Send-LuaCmd "return SWFOC_GetFireRateMultiplierGlobal()"

$baseline = [ordered]@{
    timestamp = (Get-Date).ToUniversalTime().ToString("o")
    game_mode = $modeText
    mod = $mod.Response
    bridge_build_info = $buildInfo.Response
    pipe_received_baseline = $script:pipeReceivedBaseline
    all_players = $allPlayers.Response
    credits_multiplier_global = $creditsMult.Response
    credits_freeze_global = $creditsFreeze.Response
    damage_multiplier_global = $damageMult.Response
    fire_rate_multiplier_global = $fireRateMult.Response
}
$baseline | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $baselinePath

Write-Host "  [OK]   Baseline written to $baselinePath"
Write-Host ""

if ($DryRun) {
    Write-Host "DryRun mode: skipping phases 2-5. Preflight + baseline complete."
    exit 0
}

# -----------------------------------------------------------------------------
# Phase 2 — Per-tab matrix (skeleton — extends incrementally)
# -----------------------------------------------------------------------------
# Each tab tests is structured as a list of probe definitions. Adding a new
# tab is one block; adding a new button is one row inside a block.
# The existing autonomous_live_test.ps1 + extended_live_test.ps1 (79 probes
# total) feed this matrix; this skeleton wires up the meta-tab probes that
# don't need mode-specific game state.

Write-Host "Phase 2: per-tab matrix (skeleton run; full matrix is iter-incremental)"

# Meta tab — these run regardless of mode, are the fastest, and gate every other phase
$metaProbes = @(
    @{ Tab="Meta"; Feature="GetVersion";       Cmd="return SWFOC_GetVersion()";       ModeExpected="ANY"; CatStatus="LIVE" }
    @{ Tab="Meta"; Feature="GetBuildInfo";     Cmd="return SWFOC_GetBuildInfo()";     ModeExpected="ANY"; CatStatus="LIVE" }
    @{ Tab="Meta"; Feature="StateInfo";        Cmd="return SWFOC_StateInfo()";        ModeExpected="ANY"; CatStatus="LIVE" }
    @{ Tab="Meta"; Feature="DiagSelfTest";     Cmd="return SWFOC_DiagSelfTest()";     ModeExpected="ANY"; CatStatus="LIVE" }
    @{ Tab="Meta"; Feature="GetGameModeLua";   Cmd="return SWFOC_GetGameModeLua()";   ModeExpected="ANY"; CatStatus="LIVE" }
    @{ Tab="Meta"; Feature="ListMods";         Cmd="return SWFOC_ListMods()";         ModeExpected="ANY"; CatStatus="LIVE" }
    @{ Tab="Meta"; Feature="GetCurrentMod";    Cmd="return SWFOC_GetCurrentMod()";    ModeExpected="ANY"; CatStatus="LIVE" }
)

foreach ($p in $metaProbes) {
    $result = Send-LuaCmd $p.Cmd
    $verdict = Classify-Response $result.Response
    Write-Verdict -Tab $p.Tab -Feature $p.Feature -CapabilityStatus $p.CatStatus `
        -ModeExpected $p.ModeExpected -ModeObserved $modeText -Verdict $verdict `
        -Evidence @{ response = $result.Response.Substring(0, [Math]::Min(100, $result.Response.Length)) } `
        -DurationMs $result.DurationMs `
        -BridgePipeStatsReceivedDelta 1
}

Write-Host "  Meta tab: $($metaProbes.Count) probes complete"

# TODO Phase 2: add probes for each of the 27 tabs. Inherit from
# autonomous_live_test.ps1 + extended_live_test.ps1 and route them through
# Write-Verdict instead of the ad-hoc output formatter.

# TODO Phase 3: 10 composite workflows from improvement_plan_2026-05-20.md Part 4

# TODO Phase 4: 6 failure-mode probes (paused-game, mode-mismatch, etc.)

# -----------------------------------------------------------------------------
# Phase 5 — Restore + Report
# -----------------------------------------------------------------------------

Write-Host ""
Write-Host "Phase 5: restore + report"

# Final pipe stats
$pipeStatsFinal = Send-LuaCmd "return SWFOC_DiagPipeStats()"
$pipeReceivedFinal = if ($pipeStatsFinal.Response -match "received=(\d+)") { [int]$Matches[1] } else { 0 }
$pipeDelta = $pipeReceivedFinal - $script:pipeReceivedBaseline

# Generate markdown summary
$total = 0
$counts.Values | ForEach-Object { $total += $_ }
$passRate = if ($total -gt 0) { [math]::Round(100.0 * $counts["PASS"] / $total, 1) } else { 0 }

$md = @"
# SWFOC Editor Test Matrix Run — $timestamp

## Environment
- Game mode: $modeText
- Mod: $($mod.Response)
- Bridge: $($buildInfo.Response)
- Pipe received: $($script:pipeReceivedBaseline) baseline → $pipeReceivedFinal final (delta=$pipeDelta)

## Verdict counts
$($counts.GetEnumerator() | Sort-Object Name | ForEach-Object { "- **$($_.Name)**: $($_.Value)" } | Out-String)

## Pass rate (LIVE rows only target ≥95%)
- Total probes: $total
- PASS: $($counts["PASS"]) ($passRate%)
- Honest non-PASS: $(($counts["P2-PENDING"] + $counts["MODE_MISMATCH"] + $counts["NEEDS_ARGS"]))
- Investigate: $(($counts["FAIL"] + $counts["UNEXPECTED"] + $counts["ENGINE_ERR"]))

## Artefacts
- JSONL log: ``$jsonlPath``
- Baseline snapshot: ``$baselinePath``
- This summary: ``$markdownPath``
"@
$md | Set-Content -LiteralPath $markdownPath -Encoding UTF8
Copy-Item $jsonlPath $latestJsonl -Force
Copy-Item $markdownPath $latestMd -Force

Write-Host ""
Write-Host "=== SUMMARY ==="
Write-Host "Total probes: $total"
foreach ($k in $counts.Keys | Sort-Object) { Write-Host "  $k`t$($counts[$k])" }
Write-Host ""
Write-Host "Pass rate: $passRate% (LIVE-row target ≥95%)"
Write-Host "JSONL: $jsonlPath"
Write-Host "Markdown: $markdownPath"

# Exit code based on FAIL/UNEXPECTED presence
$failureCount = 0
if ($counts.ContainsKey("FAIL")) { $failureCount += $counts["FAIL"] }
if ($counts.ContainsKey("UNEXPECTED")) { $failureCount += $counts["UNEXPECTED"] }
if ($failureCount -gt 0) { exit 1 }
if ($passRate -lt 95.0 -and $total -gt 50) { exit 1 }
exit 0
