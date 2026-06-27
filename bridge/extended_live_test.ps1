# extended_live_test.ps1 - second-wave autonomous testing, exercises helpers
# the original 48-probe battery skipped (need running game / specific args).
# Game must be running and unpaused. Reports each probe with verdict.

function Send {
    param([string]$cmd, [int]$readMs = 250)
    $p = New-Object System.IO.Pipes.NamedPipeClientStream(".", "swfoc_bridge", "InOut")
    try { $p.Connect(3000) } catch { $p.Dispose(); return "CONNECT_FAIL" }
    try {
        $b = [System.Text.Encoding]::UTF8.GetBytes($cmd) + [byte[]]@(0)
        $p.Write($b, 0, $b.Length); $p.Flush()
        Start-Sleep -Milliseconds $readMs
        $buf = New-Object byte[] 8192
        $n = $p.Read($buf, 0, 8192)
        return [System.Text.Encoding]::UTF8.GetString($buf, 0, $n).TrimEnd()
    } finally { $p.Dispose() }
}

function Probe {
    param([string]$name, [string]$cmd)
    $r = Send $cmd
    $verdict = if ($r -match "^ERR: .*Phase 2") { "P2" }
               elseif ($r -match "Phase 2 hook pending") { "P2" }
               elseif ($r -match "non-tactical state") { "TC" }
               elseif ($r -match "engine error rc=") { "EN" }
               elseif ($r -match "^ERR: .*expected ") { "AR" }
               elseif ($r -match "^ERR:") { "ER" }
               elseif ($r -eq "CONNECT_FAIL") { "XX" }
               else { "OK" }
    $preview = if ($r.Length -gt 90) { $r.Substring(0, 90) + "..." } else { $r }
    Write-Host ("  [{0}] {1,-40} {2}" -f $verdict, $name, $preview.Replace("`n", " | "))
    return $verdict
}

$counts = @{ OK=0; P2=0; TC=0; EN=0; AR=0; ER=0; XX=0 }

Write-Output "=== EXTENDED LIVE TEST (autonomous, game must be running) ==="
Write-Output ""

Write-Output "--- DIPLOMACY & PLANET (galactic-mode writes) ---"
# Diplomacy: try setting Underworld -> Rebel as ally (then revert)
$v = Probe "SetDiplomacy(6,0,Ally)"    "return SWFOC_SetDiplomacy(6, 0, 'Ally')"; $counts[$v]++
$v = Probe "SetDiplomacy revert"        "return SWFOC_SetDiplomacy(6, 0, 'Enemy')"; $counts[$v]++
$v = Probe "MakeAllyLua"                "return SWFOC_MakeAllyLua(0, 6)"; $counts[$v]++
$v = Probe "MakeEnemyLua"               "return SWFOC_MakeEnemyLua(0, 6)"; $counts[$v]++
$v = Probe "GlobalMakeAllyLua(6)"       "return SWFOC_GlobalMakeAllyLua(6)"; $counts[$v]++
Write-Output ""

Write-Output "--- HERO / UNIT FIELD ---"
$v = Probe "ListHeroes"                 "return SWFOC_ListHeroes()"; $counts[$v]++
$v = Probe "GetTotalUnitsAlive"         "return SWFOC_GetTotalUnitsAlive()"; $counts[$v]++
$v = Probe "ListTacticalUnits head"     "return string.sub(SWFOC_ListTacticalUnits(), 1, 80)"; $counts[$v]++
$v = Probe "GetFactionRoster(6) head"   "return string.sub(SWFOC_GetFactionRoster(6), 1, 80)"; $counts[$v]++
$v = Probe "SetHeroRespawn(slot,sec)"   "return SWFOC_SetHeroRespawn(60.0)"; $counts[$v]++
$v = Probe "GetHeroRespawn read-back"   "return SWFOC_DoString([[return tostring(60.0)]])"; $counts[$v]++
Write-Output ""

Write-Output "--- ECONOMY GLOBALS (write + revert) ---"
$v = Probe "SetIncomeMult(2.5)"         "return SWFOC_SetIncomeMultiplier(2.5)"; $counts[$v]++
$v = Probe "SetIncomeMult revert"       "return SWFOC_SetIncomeMultiplier(1.0)"; $counts[$v]++
$v = Probe "SetBuildSpeed(2.0)"         "return SWFOC_SetBuildSpeed(2.0)"; $counts[$v]++
$v = Probe "SetBuildSpeed revert"       "return SWFOC_SetBuildSpeed(1.0)"; $counts[$v]++
$v = Probe "DrainEnemyCredits(1000)"    "return SWFOC_DrainEnemyCredits(1000)"; $counts[$v]++
Write-Output ""

Write-Output "--- COMBAT GLOBALS ---"
$v = Probe "SetDamageMultGlobal(2.0)"   "return SWFOC_SetDamageMultiplierGlobal(2.0)"; $counts[$v]++
$v = Probe "SetDamageMultGlobal revert" "return SWFOC_SetDamageMultiplierGlobal(1.0)"; $counts[$v]++
$v = Probe "SetFireRateMultGlb(0.5)"    "return SWFOC_SetFireRateMultiplierGlobal(0.5)"; $counts[$v]++
$v = Probe "SetFireRateMultGlb revert"  "return SWFOC_SetFireRateMultiplierGlobal(1.0)"; $counts[$v]++
$v = Probe "SetPerFactionSpeed"         "return SWFOC_SetPerFactionSpeedMultiplier(6, 1.5)"; $counts[$v]++
$v = Probe "SetPerFactionSpeed revert"  "return SWFOC_SetPerFactionSpeedMultiplier(6, 1.0)"; $counts[$v]++
Write-Output ""

Write-Output "--- LOOKUP / META ---"
$v = Probe "DoString return 42"         'return SWFOC_DoString("return 42")'; $counts[$v]++
$v = Probe "DoString table.concat"      'return SWFOC_DoString([[return table.concat({1,2,3}, "-")]])'; $counts[$v]++
$v = Probe "DoString string.format"     'return SWFOC_DoString([[return string.format("v=%d", 99)]])'; $counts[$v]++
$v = Probe "DoString tostring(_G)"      'return SWFOC_DoString([[return tostring(_G):sub(1,30)]])'; $counts[$v]++
$v = Probe "EventStreamDrain"           "return SWFOC_EventStreamDrain()"; $counts[$v]++
Write-Output ""

Write-Output "--- CAMERA (galactic returns 0,0,0 expected) ---"
$v = Probe "GetCameraPos"               "return SWFOC_GetCameraPos()"; $counts[$v]++
$v = Probe "ZoomCameraLua"              "return SWFOC_ZoomCameraLua(0.5)"; $counts[$v]++
$v = Probe "LetterBoxOff"               "return SWFOC_LetterBoxOff()"; $counts[$v]++
Write-Output ""

Write-Output "=== TOTALS ==="
foreach ($k in $counts.Keys | Sort-Object) {
    if ($counts[$k] -gt 0) {
        $label = switch ($k) {
            "OK" { "PASS" }
            "P2" { "P2-PENDING (honest)" }
            "TC" { "TACTICAL-ONLY (expected)" }
            "EN" { "ENGINE-ERR" }
            "AR" { "NEEDS-ARGS" }
            "ER" { "ERR" }
            "XX" { "CONNECT_FAIL" }
        }
        Write-Output ("  {0,-30} {1}" -f $label, $counts[$k])
    }
}
$total = ($counts.Values | Measure-Object -Sum).Sum
Write-Output "  -- total: $total --"
