# focused_multiplier_test.ps1 — verify the credits multiplier fix exclusively
# affects positive deltas (income), not negative deltas (spends).
#
# Strategy: SetCreditsForSlot writes directly to memory (bypasses AddCredits),
# so we can use it to set a baseline without multiplier interference. Then we
# set the multiplier, set credits to a known value, sleep briefly to let game
# tick, and measure the delta. The bridge's AddCredits hook should multiply
# only positive deltas — the game's daily income.

function Send-LuaCmd {
    param([string]$cmd, [int]$readMs = 250)
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "swfoc_bridge", "InOut")
    try { $pipe.Connect(3000) } catch { $pipe.Dispose(); return "CONNECT_FAIL" }
    try {
        $b = [System.Text.Encoding]::UTF8.GetBytes($cmd) + [byte[]]@(0)
        $pipe.Write($b, 0, $b.Length); $pipe.Flush()
        Start-Sleep -Milliseconds $readMs
        $buf = New-Object byte[] 8192
        $n = $pipe.Read($buf, 0, 8192)
        return [System.Text.Encoding]::UTF8.GetString($buf, 0, $n).TrimEnd()
    } finally { $pipe.Dispose() }
}

$LOCAL_SLOT = 6  # Underworld

Write-Output "================================================================"
Write-Output "  FOCUSED CREDITS-MULTIPLIER TEST (sign-gate verification)"
Write-Output "  Started: $(Get-Date -Format HH:mm:ss)"
Write-Output "================================================================"
Write-Output ""

# Snapshot initial state
$buildInfo = Send-LuaCmd "return SWFOC_GetBuildInfo()"
Write-Output "Bridge build: $buildInfo"
Write-Output ""
Write-Output "[Step 1] Reset multiplier to 1.0 + set credits baseline 50000"
Send-LuaCmd "return SWFOC_SetCreditsMultiplierGlobal(1.0)" | Out-Null
Send-LuaCmd "return SWFOC_SetCreditsForSlot($LOCAL_SLOT, 50000)" | Out-Null
$v0 = Send-LuaCmd "return SWFOC_GetCreditsForSlot($LOCAL_SLOT)"
Write-Output "  credits after baseline-set: $v0"
Write-Output ""

# Phase A: 1x multiplier - measure natural tick
Write-Output "[Step 2] Phase A: mult=1x, let game tick ~20s, measure delta"
Write-Output "  (game must be unpaused for ticks to fire AddCredits with income)"
$tA0 = Send-LuaCmd "return SWFOC_GetCreditsForSlot($LOCAL_SLOT)"
Start-Sleep -Seconds 20
$tA1 = Send-LuaCmd "return SWFOC_GetCreditsForSlot($LOCAL_SLOT)"
$deltaA = [int]$tA1 - [int]$tA0
Write-Output "  before: $tA0 -> after: $tA1 (delta=$deltaA, mult=1x)"
Write-Output ""

# Phase B: 5x multiplier - measure same window
Write-Output "[Step 3] Phase B: mult=5.0x, let game tick ~20s, measure delta"
Send-LuaCmd "return SWFOC_SetCreditsMultiplierGlobal(5.0)" | Out-Null
$tB0 = Send-LuaCmd "return SWFOC_GetCreditsForSlot($LOCAL_SLOT)"
Start-Sleep -Seconds 20
$tB1 = Send-LuaCmd "return SWFOC_GetCreditsForSlot($LOCAL_SLOT)"
$deltaB = [int]$tB1 - [int]$tB0
Write-Output "  before: $tB0 -> after: $tB1 (delta=$deltaB, mult=5x)"
Write-Output ""

# Phase C: capture all-player snapshot to verify spends are NOT scaled
Write-Output "[Step 4] All-player snapshot (verify enemies spend at normal rate)"
$allPlayers = Send-LuaCmd "return SWFOC_GetAllPlayers()"
Write-Output "  $allPlayers"
Write-Output ""

# Restore
Send-LuaCmd "return SWFOC_SetCreditsMultiplierGlobal(1.0)" | Out-Null
Send-LuaCmd "return SWFOC_SetCreditsForSlot($LOCAL_SLOT, 12000)" | Out-Null
$vFinal = Send-LuaCmd "return SWFOC_GetCreditsForSlot($LOCAL_SLOT)"
Write-Output "[Step 5] Restored: mult=1.0, credits=$vFinal"
Write-Output ""

# Verdict
Write-Output "================================================================"
Write-Output "  VERDICT"
Write-Output "================================================================"

if ($deltaA -eq 0 -and $deltaB -eq 0) {
    Write-Output "  INCONCLUSIVE - game is paused; no AddCredits calls fired."
    Write-Output "  Both deltas are 0. Unpause the game to retest."
    exit 2
}

if ($deltaA -eq 0 -and $deltaB -gt 0) {
    Write-Output "  GAME WAS PAUSED THEN UNPAUSED MID-TEST"
    Write-Output "  Phase A had no ticks but Phase B did. Inconclusive on ratio."
    exit 2
}

$ratio = if ($deltaA -gt 0) { [math]::Round($deltaB / $deltaA, 2) } else { -1 }
Write-Output "  Phase A delta (mult=1x): $deltaA"
Write-Output "  Phase B delta (mult=5x): $deltaB"
Write-Output "  Observed ratio:          ${ratio}x (expected ~5x if income scales)"
Write-Output ""

if ($ratio -ge 4.0 -and $ratio -le 6.5) {
    Write-Output "  PASS: Multiplier ratio is in the expected ~5x range."
    Write-Output "  Income IS being scaled by AddCredits hook on positive deltas."
    exit 0
} elseif ($ratio -ge 0.5 -and $ratio -le 2.0) {
    Write-Output "  FAIL: Multiplier had little/no effect on income."
    Write-Output "  Sign-gate may have over-corrected; AddCredits hook not firing on income."
    exit 1
} else {
    Write-Output "  UNEXPECTED: Ratio outside both ranges. Investigate manually."
    Write-Output "  Could be timing window mismatch or game-tick granularity issue."
    exit 1
}
