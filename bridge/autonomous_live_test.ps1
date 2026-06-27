# autonomous_live_test.ps1
# Hits the SWFOC bridge with a battery of pre-baked probes and writes a
# structured pass/fail/expected-error report. Does NOT require any in-game
# input — fires every probe over the named pipe, classifies the response,
# logs everything to a timestamped file.

$ErrorActionPreference = "Continue"
$logPath = "C:\Users\Prekzursil\Downloads\swfoc_memory\.remember\autonomous_live_test_$(Get-Date -Format yyyy-MM-dd_HHmmss).log"

function Send-LuaCmd {
    param([string]$cmd, [int]$readMs = 350)
    # NamedPipeClientStream performs the full pipe handshake synchronously
    # whereas File.Open returns a handle before the server has acknowledged.
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "swfoc_bridge", "InOut")
    try {
        $pipe.Connect(3000)
    } catch {
        $pipe.Dispose()
        return "CONNECT_FAIL: $_"
    }
    try {
        $b = [System.Text.Encoding]::UTF8.GetBytes($cmd) + [byte[]]@(0)
        $pipe.Write($b, 0, $b.Length)
        $pipe.Flush()
        Start-Sleep -Milliseconds $readMs
        $buf = New-Object byte[] 65536
        $n = $pipe.Read($buf, 0, 65536)
        return [System.Text.Encoding]::UTF8.GetString($buf, 0, $n).TrimEnd()
    } finally {
        $pipe.Dispose()
    }
}

function Classify {
    param([string]$response, [string]$expectedPattern = $null)
    if ($response -match "^CONNECT_FAIL") { return "CONNECT_FAIL" }
    if ($response -match "^ERR: .*Phase 2") { return "P2-PENDING (honest)" }
    if ($response -match "Phase 2 hook pending") { return "P2-PENDING (honest)" }
    if ($response -match "non-tactical state") { return "TACTICAL-ONLY (expected in galactic)" }
    if ($response -match "engine error rc=") { return "ENGINE-ERR (helper raised)" }
    if ($response -match "^ERR: .*expected ") { return "NEEDS-ARGS" }
    if ($response -match "^ERR:") { return "ERR" }
    if ($expectedPattern -and -not ($response -match $expectedPattern)) { return "UNEXPECTED" }
    return "PASS"
}

# ---- TEST BATTERY ----
# Each test: name, cmd, category, optional expected-pattern regex
$tests = @(
    # === Meta / version / state ===
    @{ Cat="META"; Name="GetVersion";              Cmd="return SWFOC_GetVersion()"; Expect="v1\.5-dev" },
    @{ Cat="META"; Name="GetBuildInfo";            Cmd="return SWFOC_GetBuildInfo()"; Expect="\d{4}" },
    @{ Cat="META"; Name="StateInfo";               Cmd="return SWFOC_StateInfo()"; Expect="Game states" },
    @{ Cat="META"; Name="DiagPipeStats";           Cmd="return SWFOC_DiagPipeStats()"; Expect="received=\d+" },
    @{ Cat="META"; Name="DiagSelfTest";            Cmd="return SWFOC_DiagSelfTest()"; Expect="passed=" },
    @{ Cat="META"; Name="DiagGameTick";            Cmd="return SWFOC_DiagGameTick()" },
    @{ Cat="META"; Name="DiagSelection";           Cmd="return SWFOC_DiagSelection()" },
    @{ Cat="META"; Name="GetGameModeLua";          Cmd="return SWFOC_GetGameModeLua()" },
    @{ Cat="META"; Name="ListMods";                Cmd="return SWFOC_ListMods()" },
    @{ Cat="META"; Name="GetCurrentMod";           Cmd="return SWFOC_GetCurrentMod()" },

    # === Player + faction reads ===
    @{ Cat="READ"; Name="GetLocalPlayerLua";       Cmd="return SWFOC_GetLocalPlayerLua()" },
    @{ Cat="READ"; Name="GetAllPlayers";           Cmd="return SWFOC_GetAllPlayers()"; Expect="count=" },
    @{ Cat="READ"; Name="GetCredits";              Cmd="return SWFOC_GetCredits()"; Expect="\d+" },
    @{ Cat="READ"; Name="GetCreditsForSlot(6)";    Cmd="return SWFOC_GetCreditsForSlot(6)"; Expect="\d+" },
    @{ Cat="READ"; Name="GetTechForSlot(6)";       Cmd="return SWFOC_GetTechForSlot(6)"; Expect="^\d+$" },
    @{ Cat="READ"; Name="GetMaxCredits";           Cmd="return SWFOC_GetMaxCredits()"; Expect="\d+" },
    @{ Cat="READ"; Name="GetPlayerKills(6)";       Cmd="return SWFOC_GetPlayerKills(6)" },
    @{ Cat="READ"; Name="GetPlayerDeaths(6)";      Cmd="return SWFOC_GetPlayerDeaths(6)" },
    @{ Cat="READ"; Name="ListFactions";            Cmd="return SWFOC_ListFactions()" },

    # === Multipliers + freezes (READ side) ===
    @{ Cat="MULT-READ"; Name="GetCreditsMultiplierGlobal"; Cmd="return SWFOC_GetCreditsMultiplierGlobal()" },
    @{ Cat="MULT-READ"; Name="GetCreditsFreezeGlobal";     Cmd="return SWFOC_GetCreditsFreezeGlobal()" },
    @{ Cat="MULT-READ"; Name="GetDamageMultiplierGlobal";  Cmd="return SWFOC_GetDamageMultiplierGlobal()" },
    @{ Cat="MULT-READ"; Name="GetFireRateMultiplierGlobal";Cmd="return SWFOC_GetFireRateMultiplierGlobal()" },

    # === World queries ===
    @{ Cat="WORLD"; Name="GetPlanets";             Cmd="return SWFOC_GetPlanets()" },
    @{ Cat="WORLD"; Name="ListHeroes";             Cmd="return SWFOC_ListHeroes()" },
    @{ Cat="WORLD"; Name="ListAbilities";          Cmd="return SWFOC_ListAbilities()" },
    @{ Cat="WORLD"; Name="ListTacticalUnits";      Cmd="return string.sub(SWFOC_ListTacticalUnits(), 1, 100)" },
    @{ Cat="WORLD"; Name="GetTotalUnitsAlive";     Cmd="return SWFOC_GetTotalUnitsAlive()" },
    @{ Cat="WORLD"; Name="GetSelectedUnit";        Cmd="return SWFOC_GetSelectedUnit()" },
    @{ Cat="WORLD"; Name="GetSelectedUnits";       Cmd="return SWFOC_GetSelectedUnits()" },
    @{ Cat="WORLD"; Name="GetFactionRoster(6)";    Cmd="return string.sub(SWFOC_GetFactionRoster(6), 1, 200)" },

    # === Camera reads (galactic returns 0,0,0; tactical returns 3D) ===
    @{ Cat="CAMERA"; Name="GetCameraPos";          Cmd="return SWFOC_GetCameraPos()" },

    # === Tactical-only (expected to fail in galactic with mode message) ===
    @{ Cat="TACTICAL-ONLY"; Name="BatchTypeExists";Cmd="return SWFOC_BatchTypeExists('TIE_Fighter|Vengeance_Frigate|NotARealUnit')" },

    # === Write-and-readback cycles ===
    @{ Cat="WRITE-CYCLE"; Name="SetCredits cycle"; Cmd="local v0=SWFOC_GetCreditsForSlot(6); SWFOC_SetCreditsForSlot(6,77777); local v1=SWFOC_GetCreditsForSlot(6); SWFOC_SetCreditsForSlot(6,v0); local v2=SWFOC_GetCreditsForSlot(6); return 'before='..v0..' mid='..v1..' after='..v2"; Expect="before=12000 mid=77777 after=12000" },
    @{ Cat="WRITE-CYCLE"; Name="SetTech cycle";    Cmd="local v0=SWFOC_GetTechForSlot(6); SWFOC_SetTechForSlot(6,4); local v1=SWFOC_GetTechForSlot(6); SWFOC_SetTechForSlot(6,v0); local v2=SWFOC_GetTechForSlot(6); return 'before='..v0..' mid='..v1..' after='..v2"; Expect="before=2 mid=4 after=2" },
    @{ Cat="WRITE-CYCLE"; Name="CreditsMult cycle";Cmd="local v0=SWFOC_GetCreditsMultiplierGlobal(); SWFOC_SetCreditsMultiplierGlobal(3.5); local v1=SWFOC_GetCreditsMultiplierGlobal(); SWFOC_SetCreditsMultiplierGlobal(v0); local v2=SWFOC_GetCreditsMultiplierGlobal(); return 'before='..v0..' mid='..v1..' after='..v2"; Expect="mid=3.5" },
    @{ Cat="WRITE-CYCLE"; Name="DamageMult cycle"; Cmd="local v0=SWFOC_GetDamageMultiplierGlobal(); SWFOC_SetDamageMultiplierGlobal(2.0); local v1=SWFOC_GetDamageMultiplierGlobal(); SWFOC_SetDamageMultiplierGlobal(v0); local v2=SWFOC_GetDamageMultiplierGlobal(); return 'before='..v0..' mid='..v1..' after='..v2"; Expect="mid=2" },
    @{ Cat="WRITE-CYCLE"; Name="FireRateMult cycle"; Cmd="local v0=SWFOC_GetFireRateMultiplierGlobal(); SWFOC_SetFireRateMultiplierGlobal(0.5); local v1=SWFOC_GetFireRateMultiplierGlobal(); SWFOC_SetFireRateMultiplierGlobal(v0); local v2=SWFOC_GetFireRateMultiplierGlobal(); return 'before='..v0..' mid='..v1..' after='..v2"; Expect="mid=0.5" },
    @{ Cat="WRITE-CYCLE"; Name="CreditsFreeze cycle"; Cmd="local v0=SWFOC_GetCreditsFreezeGlobal(); SWFOC_SetCreditsFreezeGlobal(1); local v1=SWFOC_GetCreditsFreezeGlobal(); SWFOC_SetCreditsFreezeGlobal(0); local v2=SWFOC_GetCreditsFreezeGlobal(); return 'before='..v0..' mid='..v1..' after='..v2"; Expect="mid=1 after=0" },

    # === P2-PENDING wires (catalog says these will return honest pending text) ===
    @{ Cat="P2-PENDING"; Name="FreezeAI(true)";    Cmd="return SWFOC_FreezeAI(1)" },
    @{ Cat="P2-PENDING"; Name="SetGameSpeed(2.0)"; Cmd="return SWFOC_SetGameSpeed(2.0)" },
    @{ Cat="P2-PENDING"; Name="TriggerVictory";    Cmd="return SWFOC_TriggerVictory('Galactic_Conquer')" },
    @{ Cat="P2-PENDING"; Name="FreeCam(true)";     Cmd="return SWFOC_FreeCam(1)" },

    # === Galactic-only writes (revertible) ===
    @{ Cat="GALACTIC"; Name="UncapCredits";        Cmd="return SWFOC_UncapCredits(true)" },
    @{ Cat="GALACTIC"; Name="UncapCredits revert"; Cmd="return SWFOC_UncapCredits(false)" },

    # === Hooks / abilities / lookup ===
    @{ Cat="LOOKUP"; Name="DiagListRegisteredFn (head)"; Cmd="return string.sub(SWFOC_DiagListRegisteredFunctions(), 1, 100)" },
    @{ Cat="LOOKUP"; Name="DoString round-trip";   Cmd='return SWFOC_DoString("return 1+2+3")'; Expect="6" },
    @{ Cat="LOOKUP"; Name="DoString global probe"; Cmd='return SWFOC_DoString("return _G[\"GameMode\"] or \"unknown\"")' }
)

# ---- Run ----
$results = @()
$counts = @{ PASS=0; "P2-PENDING (honest)"=0; "TACTICAL-ONLY (expected in galactic)"=0; "ENGINE-ERR (helper raised)"=0; "NEEDS-ARGS"=0; ERR=0; UNEXPECTED=0; CONNECT_FAIL=0 }

$startTime = Get-Date
Add-Content -LiteralPath $logPath -Value "=== SWFOC Bridge Autonomous Live Test ==="
Add-Content -LiteralPath $logPath -Value "Started: $startTime"
Add-Content -LiteralPath $logPath -Value ""

foreach ($t in $tests) {
    $response = Send-LuaCmd $t.Cmd
    $verdict = Classify $response $t.Expect
    $counts[$verdict]++
    $results += [PSCustomObject]@{ Cat=$t.Cat; Name=$t.Name; Verdict=$verdict; Response=$response }
    Add-Content -LiteralPath $logPath -Value ("[{0,-30}] {1,-25} {2,-30} -> {3}" -f $t.Cat, $t.Name, $verdict, ($response.Replace("`n"," | ").Substring(0, [Math]::Min(200, $response.Length))))
}

$endTime = Get-Date
$elapsed = [int]($endTime - $startTime).TotalSeconds

# ---- Summary ----
Add-Content -LiteralPath $logPath -Value ""
Add-Content -LiteralPath $logPath -Value "=== SUMMARY ==="
Add-Content -LiteralPath $logPath -Value "Elapsed: ${elapsed}s"
Add-Content -LiteralPath $logPath -Value "Total tests: $($results.Count)"
foreach ($k in $counts.Keys | Sort-Object) {
    $line = "  {0,-40} {1}" -f $k, $counts[$k]
    Add-Content -LiteralPath $logPath -Value $line
}
Add-Content -LiteralPath $logPath -Value ""

# Console output: condensed table by category
Write-Output ""
Write-Output "=== Autonomous live test (${elapsed}s, $($results.Count) probes) ==="
foreach ($cat in ($results | Group-Object Cat | Sort-Object Name)) {
    Write-Output ""
    Write-Output "--- $($cat.Name) ---"
    foreach ($r in $cat.Group) {
        $status = switch ($r.Verdict) {
            "PASS" { "OK" }
            "P2-PENDING (honest)" { "P2" }
            "TACTICAL-ONLY (expected in galactic)" { "TC" }
            "ENGINE-ERR (helper raised)" { "EN" }
            "NEEDS-ARGS" { "AR" }
            "UNEXPECTED" { "??" }
            "CONNECT_FAIL" { "XX" }
            default { "ER" }
        }
        $preview = $r.Response.Replace("`n", " | ")
        if ($preview.Length -gt 100) { $preview = $preview.Substring(0, 100) + "..." }
        Write-Output ("  [{0}] {1,-32} {2}" -f $status, $r.Name, $preview)
    }
}

Write-Output ""
Write-Output "=== TOTALS ==="
foreach ($k in $counts.Keys | Sort-Object) {
    if ($counts[$k] -gt 0) {
        Write-Output ("  {0,-40} {1}" -f $k, $counts[$k])
    }
}
Write-Output ""
Write-Output "Full log: $logPath"
